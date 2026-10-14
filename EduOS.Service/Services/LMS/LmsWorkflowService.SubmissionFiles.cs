using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.LMS;

/// <summary>Atomic multi-file assignment submissions and private, authorized file metadata reads.</summary>
public sealed partial class LmsWorkflowService
{
    private async Task<bool> SubmissionAcademicEligibleAsync(
        Course course, CourseEnrollment enrollment, DateTime utcNow, CancellationToken ct)
    {
        if (!enrollment.StudentEnrollmentId.HasValue)
            return !course.AcademicProgramId.HasValue && !course.SubjectId.HasValue;
        if (_submissionYears == null || _submissionTerms == null ||
            _submissionBatches == null || _submissionCurricula == null) return false;
        var tenant = _user.TenantId;
        var today = DateOnly.FromDateTime(utcNow);
        var academic = await _academicEnrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == enrollment.StudentEnrollmentId.Value &&
            x.StudentId == enrollment.StudentId && x.State == EnrollmentState.Active &&
            x.IsCurrent && !x.IsDeleted, ct);
        if (academic == null || academic.EnrollmentDate > today ||
            (academic.EndDate.HasValue && academic.EndDate.Value < today) ||
            (course.AcademicProgramId.HasValue && academic.AcademicProgramId != course.AcademicProgramId.Value))
            return false;
        var year = await _submissionYears.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == academic.AcademicYearId && x.IsActive && !x.IsDeleted, ct);
        var batch = await _submissionBatches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == academic.AcademicBatchId && x.IsActive && !x.IsDeleted, ct);
        var curriculum = await _submissionCurricula.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == academic.AcademicCurriculumId && x.IsActive && !x.IsDeleted, ct);
        if (year == null || batch == null || curriculum == null ||
            today < year.StartDate || today > year.EndDate ||
            (year.CampusId.HasValue && year.CampusId.Value != academic.CampusId) ||
            batch.CampusId != academic.CampusId || batch.AcademicYearId != academic.AcademicYearId ||
            batch.AcademicProgramId != academic.AcademicProgramId || batch.AcademicLevelId != academic.AcademicLevelId ||
            (batch.AcademicTermId.HasValue && batch.AcademicTermId != academic.AcademicTermId) ||
            (batch.StartDate.HasValue && today < batch.StartDate.Value) ||
            (batch.EndDate.HasValue && today > batch.EndDate.Value) ||
            curriculum.AcademicProgramId != academic.AcademicProgramId ||
            today < curriculum.EffectiveFrom ||
            (curriculum.EffectiveTo.HasValue && today > curriculum.EffectiveTo.Value))
            return false;
        if (academic.AcademicTermId.HasValue)
        {
            var term = await _submissionTerms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == academic.AcademicTermId.Value &&
                x.AcademicYearId == academic.AcademicYearId && x.IsActive && !x.IsDeleted, ct);
            if (term == null || today < term.StartDate || today > term.EndDate) return false;
        }
        if (!course.SubjectId.HasValue) return true;
        return await (from offering in _offerings.GetQueryable().AsNoTracking()
            join subject in _curriculumSubjects.GetQueryable().AsNoTracking()
                on offering.CurriculumSubjectId equals subject.Id
            where offering.TenantId == tenant && subject.TenantId == tenant &&
                offering.IsActive && !offering.IsDeleted && subject.IsActive && !subject.IsDeleted &&
                offering.AcademicBatchId == academic.AcademicBatchId &&
                offering.AcademicYearId == academic.AcademicYearId &&
                (offering.AcademicTermId == null || offering.AcademicTermId == academic.AcademicTermId) &&
                subject.AcademicCurriculumId == academic.AcademicCurriculumId &&
                subject.AcademicLevelId == academic.AcademicLevelId &&
                subject.SubjectId == course.SubjectId.Value
            select offering.Id).AnyAsync(ct);
    }

    public async Task<ApiResponse<PagedResult<MyAssignmentWorkDto>>> GetMyAssignmentWorkAsync(
        int page, int pageSize, string? search, CancellationToken ct = default)
    {
        if (!Authenticated() || !_user.IsInRole("Student"))
            return Error<PagedResult<MyAssignmentWorkDto>>("Student permission required.", 403);
        if (page < 1 || pageSize is < 1 or > 50 || (long)(page - 1) * pageSize > int.MaxValue ||
            search?.Length > 80)
            return Error<PagedResult<MyAssignmentWorkDto>>("Invalid search or pagination.");
        var tenant = _user.TenantId;
        var studentId = await OwnStudentIdAsync(ct);
        if (studentId == 0) return Error<PagedResult<MyAssignmentWorkDto>>("Active student not found.", 404);
        var query = from enrollment in _enrollments.GetQueryable().AsNoTracking()
                    join course in _courses.GetQueryable().AsNoTracking() on enrollment.CourseId equals course.Id
                    join assignment in _assignments.GetQueryable().AsNoTracking() on course.Id equals assignment.CourseId
                    where enrollment.TenantId == tenant && enrollment.StudentId == studentId &&
                        enrollment.State == CourseEnrollmentState.Active && !enrollment.IsDeleted &&
                        course.TenantId == tenant && course.IsActive && !course.IsDeleted &&
                        assignment.TenantId == tenant && assignment.IsPublished && !assignment.IsDeleted
                    select new { Enrollment = enrollment, Course = course, Assignment = assignment };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Assignment.Title.Contains(term) || x.Course.Title.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Assignment.DueAt).ThenBy(x => x.Assignment.Id)
            .ThenBy(x => x.Enrollment.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var assignmentIds = rows.Select(x => x.Assignment.Id).Distinct().ToArray();
        var enrollmentIds = rows.Select(x => x.Enrollment.Id).Distinct().ToArray();
        var submissions = await _submissions.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && assignmentIds.Contains(x.AssignmentId) &&
            enrollmentIds.Contains(x.CourseEnrollmentId) && !x.IsDeleted).ToListAsync(ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        var items = new List<MyAssignmentWorkDto>(rows.Count);
        foreach (var x in rows)
        {
            var submission = submissions.FirstOrDefault(s =>
                s.AssignmentId == x.Assignment.Id && s.CourseEnrollmentId == x.Enrollment.Id);
            var open = (x.Assignment.OpensAt == null || x.Assignment.OpensAt <= now) && (x.Assignment.DueAt == null || x.Assignment.DueAt >= now);
            var eligible = submission == null && open &&
                await SubmissionAcademicEligibleAsync(x.Course, x.Enrollment, now, ct);
            items.Add(new MyAssignmentWorkDto
            {
                AssignmentReference = x.Assignment.PublicId, AssignmentTitle = x.Assignment.Title,
                CourseTitle = x.Course.Title, CourseEnrollmentId = x.Enrollment.Id,
                OpensAt = x.Assignment.OpensAt, DueAt = x.Assignment.DueAt,
                MaxMarks = x.Assignment.MaxMarks, SubmissionId = submission?.Id,
                State = submission?.State, SubmittedAt = submission?.SubmittedAt,
                Marks = submission?.Marks, Feedback = submission?.Feedback,
                CanSubmit = eligible,
                EligibilityMessage = submission != null ? "Already submitted" :
                    !open ? "Outside submission window" : eligible ? null : "Academic eligibility is inactive"
            });
        }
        return ApiResponse<PagedResult<MyAssignmentWorkDto>>.SuccessResponse(new PagedResult<MyAssignmentWorkDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total, Items = items
        });
    }

    public async Task<ApiResponse<IReadOnlyList<AssignmentSubmissionFileDto>>> GetSubmissionFilesAsync(
        long submissionId, CancellationToken ct = default)
    {
        if (!Authenticated() || submissionId <= 0)
            return Error<IReadOnlyList<AssignmentSubmissionFileDto>>("Submission not found.", 404);
        if (_submissionFiles == null)
            return Error<IReadOnlyList<AssignmentSubmissionFileDto>>("Submission file repository is not configured.", 503);
        var tenant = _user.TenantId;
        var row = await _submissions.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == submissionId && !x.IsDeleted, ct);
        var assignment = row == null ? null : await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == row.AssignmentId && !x.IsDeleted, ct);
        var course = assignment == null ? null : await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == assignment.CourseId && !x.IsDeleted, ct);
        if (row == null || course == null)
            return Error<IReadOnlyList<AssignmentSubmissionFileDto>>("Submission not found.", 404);
        var teacher = await CanEditAsync(course, ct);
        if (!teacher)
        {
            if (!_user.IsInRole("Student"))
                return Error<IReadOnlyList<AssignmentSubmissionFileDto>>("Submission access denied.", 403);
            var studentId = await OwnStudentIdAsync(ct);
            if (studentId == 0 || !await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == row.CourseEnrollmentId && x.CourseId == course.Id &&
                x.StudentId == studentId && !x.IsDeleted, ct))
                return Error<IReadOnlyList<AssignmentSubmissionFileDto>>("Submission access denied.", 403);
        }
        var links = await _submissionFiles.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AssignmentSubmissionId == row.Id && !x.IsDeleted)
            .OrderBy(x => x.Id).Take(11).ToListAsync(ct);
        if (links.Count > 10)
            return Error<IReadOnlyList<AssignmentSubmissionFileDto>>("Submission file limit exceeded.", 409);
        var ids = links.Select(x => x.FileAssetId).Append(row.FileAssetId ?? 0).Distinct().ToArray();
        var files = await _files.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && ids.Contains(x.Id) && x.IsVerifiedSafe && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Id, ct);
        var output = new List<AssignmentSubmissionFileDto>();
        if (row.FileAssetId.HasValue && files.TryGetValue(row.FileAssetId.Value, out var primary))
            output.Add(new AssignmentSubmissionFileDto
            {
                Id = 0, FileAssetReference = primary.PublicId, FileName = primary.OriginalFileName, IsPrimary = true
            });
        foreach (var link in links)
            if (files.TryGetValue(link.FileAssetId, out var file))
                output.Add(new AssignmentSubmissionFileDto
                {
                    Id = link.Id, FileAssetReference = file.PublicId, FileName = file.OriginalFileName
                });
        return ApiResponse<IReadOnlyList<AssignmentSubmissionFileDto>>.SuccessResponse(output);
    }
}
