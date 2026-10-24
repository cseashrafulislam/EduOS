using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.LMS;

public sealed partial class LmsWorkflowService
{
    public async Task<ApiResponse<PagedResult<CourseCompletionRegisterRowDto>>> GetCourseCompletionRegisterAsync(
        Guid courseReference, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        if (!CanTeach()) return Error<PagedResult<CourseCompletionRegisterRowDto>>("LMS authoring permission required.", 403);
        if (courseReference == Guid.Empty || page < 1 || pageSize is < 1 or > 25 || search?.Length > 100)
            return Error<PagedResult<CourseCompletionRegisterRowDto>>("Invalid course, search or pagination.");
        var course = await EditableCourseAsync(courseReference, ct);
        if (course == null) return Error<PagedResult<CourseCompletionRegisterRowDto>>("Course not found or instructor access denied.", 404);
        if (_completionQuizzes == null || _completionAttempts == null)
            return Error<PagedResult<CourseCompletionRegisterRowDto>>("Quiz completion repositories are unavailable.", 503);
        var tenant = _user.TenantId;
        var query = from enrollment in _enrollments.GetQueryable().AsNoTracking()
                    join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
                    where enrollment.TenantId == tenant && student.TenantId == tenant &&
                          enrollment.CourseId == course.Id && !enrollment.IsDeleted && !student.IsDeleted
                    select new { Enrollment = enrollment, Student = student };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Student.FullName.Contains(term) || x.Student.StudentCode.StartsWith(term));
        }
        var count = await query.CountAsync(ct);
        var selected = await query.OrderBy(x => x.Student.StudentCode).ThenBy(x => x.Enrollment.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var rows = new List<CourseCompletionRegisterRowDto>(selected.Count);
        foreach (var item in selected)
        {
            var eligibility = item.Enrollment.State == CourseEnrollmentState.Completed
                ? new CourseCompletionEligibilityDto { Ready = true }
                : await EvaluateCourseCompletionAsync(item.Enrollment, course, ct);
            rows.Add(MapCourseCompletion(item.Enrollment, item.Student.PublicId, item.Student.StudentCode,
                item.Student.FullName, eligibility));
        }
        return ApiResponse<PagedResult<CourseCompletionRegisterRowDto>>.SuccessResponse(
            new PagedResult<CourseCompletionRegisterRowDto>
            { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<CourseCompletionRegisterRowDto>> CompleteCourseEnrollmentAsync(
        long enrollmentId, CompleteCourseEnrollmentRequestDto request, CancellationToken ct = default)
    {
        if (!CanTeach()) return Error<CourseCompletionRegisterRowDto>("LMS authoring permission required.", 403);
        if (enrollmentId <= 0 || request == null || !TryVersion(request.RowVersion, out _))
            return Error<CourseCompletionRegisterRowDto>("Enrollment and valid row version are required.");
        if (_completionQuizzes == null || _completionAttempts == null)
            return Error<CourseCompletionRegisterRowDto>("Quiz completion repositories are unavailable.", 503);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var tenant = _user.TenantId;
                var enrollment = await _enrollments.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == enrollmentId && !x.IsDeleted, token);
                if (enrollment == null) return Error<CourseCompletionRegisterRowDto>("Course enrollment not found.", 404);
                var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == enrollment.CourseId && !x.IsDeleted, token);
                if (course == null || !await CanEditAsync(course, token))
                    return Error<CourseCompletionRegisterRowDto>("Course not found or instructor access denied.", 404);
                var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == enrollment.StudentId && !x.IsDeleted, token);
                if (student == null) return Error<CourseCompletionRegisterRowDto>("Student not found.", 404);
                if (enrollment.State == CourseEnrollmentState.Completed)
                    return ApiResponse<CourseCompletionRegisterRowDto>.SuccessResponse(
                        MapCourseCompletion(enrollment, student.PublicId, student.StudentCode, student.FullName,
                            new CourseCompletionEligibilityDto { Ready = true }), "Course already completed.");
                if (enrollment.State != CourseEnrollmentState.Active)
                    return Error<CourseCompletionRegisterRowDto>("Only active course enrollments can be completed.", 409);
                if (!Matches(enrollment.RowVersion, request.RowVersion))
                    return Error<CourseCompletionRegisterRowDto>("Enrollment changed. Reload and retry.", 409);
                var dateError = await CompletionAcademicScopeErrorAsync(enrollment, course, token);
                if (dateError != null) return Error<CourseCompletionRegisterRowDto>(dateError, 409);
                var eligibility = await EvaluateCourseCompletionAsync(enrollment, course, token);
                if (!eligibility.Ready)
                    return Error<CourseCompletionRegisterRowDto>(string.Join(" ", eligibility.Blockers), 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                enrollment.State = CourseEnrollmentState.Completed;
                enrollment.CompletedAt = now;
                enrollment.UpdatedAt = now;
                enrollment.UpdatedBy = _user.UserId;
                _enrollments.Update(enrollment);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<CourseCompletionRegisterRowDto>.SuccessResponse(
                    MapCourseCompletion(enrollment, student.PublicId, student.StudentCode, student.FullName,
                        eligibility), "Course completion recorded.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        { return Error<CourseCompletionRegisterRowDto>("Enrollment changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "LMS completion conflict tenant {TenantId}", _user.TenantId);
            return Error<CourseCompletionRegisterRowDto>("Course completion conflicts with another request.", 409);
        }
    }

    private async Task<string?> CompletionAcademicScopeErrorAsync(CourseEnrollment enrollment, Course course, CancellationToken ct)
    {
        if (enrollment.StudentEnrollmentId == null)
            return course.AcademicProgramId.HasValue || course.SubjectId.HasValue
                ? "Academic enrollment is required for this course." : null;
        if (_submissionYears == null || _submissionBatches == null || _submissionCurricula == null)
            return "Academic completion repositories are unavailable.";
        var tenant = _user.TenantId;
        var academic = await _academicEnrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == enrollment.StudentEnrollmentId &&
            x.StudentId == enrollment.StudentId && !x.IsDeleted, ct);
        if (academic == null || academic.State != EnrollmentState.Active || !academic.IsCurrent ||
            (course.AcademicProgramId.HasValue && course.AcademicProgramId != academic.AcademicProgramId))
            return "Active student academic enrollment does not match this course.";
        var year = await _submissionYears.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == academic.AcademicYearId && !x.IsDeleted, ct);
        var batch = await _submissionBatches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == academic.AcademicBatchId && !x.IsDeleted, ct);
        var curriculum = await _submissionCurricula.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == academic.AcademicCurriculumId && !x.IsDeleted, ct);
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        if (year == null || batch == null || curriculum == null ||
            batch.CampusId != academic.CampusId || batch.AcademicYearId != academic.AcademicYearId ||
            batch.AcademicProgramId != academic.AcademicProgramId ||
            curriculum.AcademicProgramId != academic.AcademicProgramId ||
            (year.CampusId.HasValue && year.CampusId != academic.CampusId) ||
            today < year.StartDate || today > year.EndDate ||
            today < academic.EnrollmentDate || (academic.EndDate.HasValue && today > academic.EndDate) ||
            today < curriculum.EffectiveFrom || (curriculum.EffectiveTo.HasValue && today > curriculum.EffectiveTo) ||
            (batch.StartDate.HasValue && today < batch.StartDate) ||
            (batch.EndDate.HasValue && today > batch.EndDate))
            return "Course completion is outside the student's campus, curriculum or academic dates.";
        if (course.SubjectId.HasValue)
        {
            var subjectAllowed = await (from offering in _offerings.GetQueryable().AsNoTracking()
                join item in _curriculumSubjects.GetQueryable().AsNoTracking()
                    on offering.CurriculumSubjectId equals item.Id
                where offering.TenantId == tenant && item.TenantId == tenant &&
                      !offering.IsDeleted && !item.IsDeleted && offering.IsActive && item.IsActive &&
                      offering.AcademicBatchId == academic.AcademicBatchId &&
                      offering.AcademicYearId == academic.AcademicYearId &&
                      item.AcademicCurriculumId == academic.AcademicCurriculumId &&
                      item.SubjectId == course.SubjectId.Value
                select offering.Id).AnyAsync(ct);
            if (!subjectAllowed) return "Course subject is not offered in the student's curriculum.";
        }
        return null;
    }

    private async Task<CourseCompletionEligibilityDto> EvaluateCourseCompletionAsync(
        CourseEnrollment enrollment, Course course, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var lessonIds = await _lessons.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.CourseId == course.Id && x.IsPublished && !x.IsDeleted)
            .Select(x => x.Id).ToListAsync(ct);
        var completedLessons = lessonIds.Count == 0 ? 0 :
            (await _progress.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.CourseEnrollmentId == enrollment.Id &&
                lessonIds.Contains(x.LessonId) && x.IsCompleted && x.ProgressPercent >= 100 && !x.IsDeleted)
                .Select(x => x.LessonId).Distinct().ToListAsync(ct)).Count;
        var assignmentIds = await _assignments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.CourseId == course.Id && x.IsPublished && !x.IsDeleted)
            .Select(x => x.Id).ToListAsync(ct);
        var gradedAssignments = assignmentIds.Count == 0 ? 0 :
            (await _submissions.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.CourseEnrollmentId == enrollment.Id &&
                assignmentIds.Contains(x.AssignmentId) && !x.IsDeleted &&
                x.State == LearningSubmissionState.Graded && x.Marks.HasValue)
                .Select(x => x.AssignmentId).Distinct().ToListAsync(ct)).Count;
        var quizzes = await _completionQuizzes!.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.CourseId == course.Id && x.IsPublished && !x.IsDeleted)
            .Select(x => new { x.Id, x.PassMarks }).ToListAsync(ct);
        var quizIds = quizzes.Select(x => x.Id).ToArray();
        var attempts = quizIds.Length == 0 ? new List<QuizAttempt>() :
            await _completionAttempts!.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.CourseEnrollmentId == enrollment.Id &&
                quizIds.Contains(x.QuizId) && !x.IsDeleted &&
                x.State == QuizAttemptState.Graded && x.ObtainedMarks.HasValue).ToListAsync(ct);
        var passedQuizzes = quizzes.Count(x => attempts.Any(a =>
            a.QuizId == x.Id && a.ObtainedMarks >= x.PassMarks));
        var blockers = new List<string>();
        if (lessonIds.Count == 0) blockers.Add("At least one published lesson is required.");
        if (completedLessons < lessonIds.Count) blockers.Add("Complete all published lessons.");
        if (gradedAssignments < assignmentIds.Count) blockers.Add("All published assignments must be graded.");
        if (passedQuizzes < quizzes.Count) blockers.Add("Pass every published quiz.");
        return new CourseCompletionEligibilityDto
        {
            RequiredLessons = lessonIds.Count, CompletedLessons = completedLessons,
            RequiredAssignments = assignmentIds.Count, GradedAssignments = gradedAssignments,
            RequiredQuizzes = quizzes.Count, PassedQuizzes = passedQuizzes,
            Ready = blockers.Count == 0, Blockers = blockers
        };
    }

    private static CourseCompletionRegisterRowDto MapCourseCompletion(CourseEnrollment enrollment,
        Guid studentReference, string studentCode, string studentName, CourseCompletionEligibilityDto eligibility) => new()
    {
        EnrollmentId = enrollment.Id, StudentReference = studentReference, StudentCode = studentCode,
        StudentName = studentName, State = enrollment.State, EnrolledAt = enrollment.EnrolledAt,
        CompletedAt = enrollment.CompletedAt, RowVersion = Convert.ToBase64String(enrollment.RowVersion),
        Eligibility = eligibility
    };
}
