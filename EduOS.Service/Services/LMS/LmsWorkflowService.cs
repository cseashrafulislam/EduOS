using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Transactions;

namespace EduOS.Service.Services.LMS;

public sealed class LmsWorkflowService : ILmsWorkflowService
{
    private readonly IGenericRepository<Course> _courses;
    private readonly IGenericRepository<Lesson> _lessons;
    private readonly IGenericRepository<LessonResource> _resources;
    private readonly IGenericRepository<Assignment> _assignments;
    private readonly IGenericRepository<AssignmentSubmission> _submissions;
    private readonly IGenericRepository<CourseEnrollment> _courseEnrollments;
    private readonly IGenericRepository<LessonProgress> _progress;
    private readonly IGenericRepository<StudentEnrollment> _academicEnrollments;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AcademicLevel> _levels;
    private readonly IGenericRepository<EduOS.Core.Entities.Academic.Subject> _subjects;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<LmsWorkflowService> _logger;

    public LmsWorkflowService(IGenericRepository<Course> courses, IGenericRepository<Lesson> lessons,
        IGenericRepository<LessonResource> resources, IGenericRepository<Assignment> assignments,
        IGenericRepository<AssignmentSubmission> submissions, IGenericRepository<CourseEnrollment> courseEnrollments,
        IGenericRepository<LessonProgress> progress, IGenericRepository<StudentEnrollment> academicEnrollments,
        IGenericRepository<Student> students, IGenericRepository<Employee> employees,
        IGenericRepository<AcademicBatch> batches, IGenericRepository<AcademicLevel> levels,
        IGenericRepository<EduOS.Core.Entities.Academic.Subject> subjects,
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, TimeProvider clock, ILogger<LmsWorkflowService> logger)
    {
        _courses = courses; _lessons = lessons; _resources = resources;
        _assignments = assignments; _submissions = submissions;
        _courseEnrollments = courseEnrollments; _progress = progress;
        _academicEnrollments = academicEnrollments; _students = students; _employees = employees;
        _batches = batches; _levels = levels; _subjects = subjects;
        _uow = unitOfWork; _user = currentUser; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<LmsCourseDto>> SaveCourseAsync(SaveCourseDto request, CancellationToken ct = default)
    {
        if (!CanTeach()) return Fail<LmsCourseDto>("LMS authoring permission required.", 403);
        if (request == null || string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200 ||
            request.SubjectId <= 0 || request.AcademicYearId <= 0 || request.ClassId <= 0 ||
            request.SectionId is not > 0 || request.ThumbnailUrl?.Length > 500)
            return Fail<LmsCourseDto>("This endpoint requires a valid academic batch, subject and course title.");
        var tenant = _user.TenantId;
        var teacher = await ResolveTeacherAsync(request.TeacherId, ct);
        if (teacher == null) return Fail<LmsCourseDto>("Active instructor is required.", 409);
        var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
            x.Id == request.SectionId.Value && x.IsActive &&
            x.AcademicYearId == request.AcademicYearId && x.AcademicLevelId == request.ClassId, ct);
        if (batch == null) return Fail<LmsCourseDto>("Selected academic batch is unavailable.", 409);
        if (!await _levels.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
            x.Id == batch.AcademicLevelId && x.AcademicProgramId == batch.AcademicProgramId, ct) ||
            !await _subjects.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
            x.Id == request.SubjectId && x.IsActive, ct))
            return Fail<LmsCourseDto>("Academic programme/subject is invalid.", 409);
        var activeCount = await _academicEnrollments.GetQueryable().AsNoTracking().CountAsync(x => x.TenantId == tenant &&
            x.AcademicBatchId == batch.Id && x.IsCurrent && x.IsActive && x.State == EnrollmentState.Active, ct);
        if (activeCount == 0) return Fail<LmsCourseDto>(
            "A batch must contain at least one enrolled student to use the legacy course endpoint. Use the canonical course API for empty cohorts.", 409);
        try
        {
            using var tx = SerializableScope();
            var course = request.Reference.HasValue
                ? await _courses.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                    x.PublicId == request.Reference.Value, ct) : null;
            if (request.Reference.HasValue && course == null) return Fail<LmsCourseDto>("Course not found.", 404);
            if (course != null)
            {
                if (!await CanEditAsync(course, ct)) return Fail<LmsCourseDto>("Course belongs to another instructor.", 403);
                if (!MatchesVersion(course.RowVersion, request.RowVersion))
                    return Fail<LmsCourseDto>("Course changed. Reload with the latest row version.", 409);
                if (course.AcademicProgramId != batch.AcademicProgramId || course.SubjectId != request.SubjectId ||
                    course.PrimaryInstructorEmployeeId != teacher.Id)
                    return Fail<LmsCourseDto>("Changing course cohort, subject or instructor requires a controlled transfer workflow.", 409);
                course.Title = request.Title.Trim(); course.Description = Trim(request.Description);
                course.ThumbnailUrl = Trim(request.ThumbnailUrl);
                course.UpdatedAt = _clock.GetUtcNow().UtcDateTime; course.UpdatedBy = _user.UserId;
            }
            else
            {
                var reference = Guid.NewGuid();
                course = new Course
                {
                    TenantId = tenant, PublicId = reference,
                    Code = "LMS-" + reference.ToString("N")[..26].ToUpperInvariant(),
                    Title = request.Title.Trim(), Description = Trim(request.Description),
                    ThumbnailUrl = Trim(request.ThumbnailUrl),
                    AcademicProgramId = batch.AcademicProgramId, SubjectId = request.SubjectId,
                    PrimaryInstructorEmployeeId = teacher.Id, IsActive = true,
                    CreatedAt = _clock.GetUtcNow().UtcDateTime, CreatedBy = _user.UserId
                };
                await _courses.AddAsync(course);
                await _uow.SaveChangesAsync(ct);
            }
            var anchors = await CohortBatchIdsAsync(course.Id, ct);
            if (anchors.Any(x => x != batch.Id))
                return Fail<LmsCourseDto>("Course is already linked to another academic batch.", 409);
            var newEnrollments = await SynchronizeAsync(course.Id, batch.Id, ct);
            await _uow.SaveChangesAsync(ct);
            var result = await MapCourseAsync(course, null, ct);
            tx.Complete();
            return ApiResponse<LmsCourseDto>.SuccessResponse(result,
                newEnrollments == 0 ? "Course saved." : $"Course saved and {newEnrollments} students enrolled.");
        }
        catch (DbUpdateConcurrencyException) { return Fail<LmsCourseDto>("Course changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Course write conflict for tenant {TenantId}", tenant);
            return Fail<LmsCourseDto>("Course setup conflicts with another transaction.", 409);
        }
        catch (TransactionAbortedException) { return Fail<LmsCourseDto>("Concurrent course setup rejected.", 409); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Course setup failed for tenant {TenantId}", tenant);
            return Fail<LmsCourseDto>("Course could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<LmsLessonDto>> SaveLessonAsync(SaveLessonDto request, CancellationToken ct = default)
    {
        if (request == null || request.CourseReference == Guid.Empty || string.IsNullOrWhiteSpace(request.Title) ||
            request.Title.Trim().Length > 200 || request.Content?.Length > 4000 ||
            request.VideoUrl?.Length > 500 ||
            (!string.IsNullOrWhiteSpace(request.VideoUrl) &&
             (!Uri.TryCreate(request.VideoUrl, UriKind.Absolute, out var validatedVideo) ||
              validatedVideo.Scheme is not ("http" or "https"))) ||
            request.AttachmentUrl?.Length > 1000 ||
            request.OrderNo < 1 || request.Duration != 0)
            return Fail<LmsLessonDto>("Lesson title, content and ordering are invalid. Duration requires a canonical lesson extension.");
        var course = await EditableCourseAsync(request.CourseReference, ct);
        if (course == null) return Fail<LmsLessonDto>("Course is missing or not editable.", 403);
        try
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var lesson = request.Reference.HasValue
                ? await _lessons.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == _user.TenantId &&
                    x.CourseId == course.Id && x.PublicId == request.Reference.Value, ct) : null;
            if (request.Reference.HasValue && lesson == null) return Fail<LmsLessonDto>("Lesson not found.", 404);
            if (lesson == null)
            {
                lesson = new Lesson
                {
                    TenantId = _user.TenantId, CourseId = course.Id, PublicId = Guid.NewGuid(),
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _lessons.AddAsync(lesson);
            }
            else if (!MatchesVersion(lesson.RowVersion, request.RowVersion))
                return Fail<LmsLessonDto>("Lesson changed. Reload before editing.", 409);
            lesson.Title = request.Title.Trim(); lesson.Content = Trim(request.Content);
            lesson.ContentUrl = Trim(request.VideoUrl); lesson.DisplayOrder = request.OrderNo;
            lesson.IsPublished = true; lesson.UpdatedAt = now; lesson.UpdatedBy = _user.UserId;
            await _uow.SaveChangesAsync(ct);
            var resource = await _resources.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == _user.TenantId &&
                x.LessonId == lesson.Id && x.Title == "Legacy attachment", ct);
            if (!string.IsNullOrWhiteSpace(request.AttachmentUrl))
            {
                if (resource == null)
                {
                    resource = new LessonResource { TenantId = _user.TenantId, LessonId = lesson.Id,
                        Title = "Legacy attachment", DisplayOrder = 1, CreatedAt = now, CreatedBy = _user.UserId };
                    await _resources.AddAsync(resource);
                }
                resource.ExternalUrl = Trim(request.AttachmentUrl);
                await _uow.SaveChangesAsync(ct);
            }
            else if (resource != null)
            {
                resource.ExternalUrl = null;
                await _uow.SaveChangesAsync(ct);
            }
            return ApiResponse<LmsLessonDto>.SuccessResponse(MapLesson(lesson, false, resource?.ExternalUrl),
                "Lesson saved.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Lesson conflict in tenant {TenantId}", _user.TenantId);
            return Fail<LmsLessonDto>("Lesson conflicts with another change.", 409);
        }
    }

    public async Task<ApiResponse<LmsAssignmentDto>> SaveAssignmentAsync(SaveAssignmentDto request, CancellationToken ct = default)
    {
        if (request == null || request.CourseReference == Guid.Empty || string.IsNullOrWhiteSpace(request.Title) ||
            request.Title.Trim().Length > 200 || request.Description?.Length > 4000 ||
            request.TotalMark < 0 || !string.IsNullOrWhiteSpace(request.AttachmentUrl))
            return Fail<LmsAssignmentDto>("Invalid assignment. Attachments must be linked using canonical FileAsset IDs.");
        if (request.DueDate <= _clock.GetUtcNow().UtcDateTime)
            return Fail<LmsAssignmentDto>("Assignment due date must be in the future.");
        var course = await EditableCourseAsync(request.CourseReference, ct);
        if (course == null) return Fail<LmsAssignmentDto>("Course not found or not editable.", 403);
        try
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var assignment = request.Reference.HasValue
                ? await _assignments.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == _user.TenantId &&
                    x.CourseId == course.Id && x.PublicId == request.Reference.Value, ct) : null;
            if (request.Reference.HasValue && assignment == null)
                return Fail<LmsAssignmentDto>("Assignment not found.", 404);
            if (assignment == null)
            {
                assignment = new Assignment
                {
                    TenantId = _user.TenantId, CourseId = course.Id, PublicId = Guid.NewGuid(),
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _assignments.AddAsync(assignment);
            }
            else if (!MatchesVersion(assignment.RowVersion, request.RowVersion))
                return Fail<LmsAssignmentDto>("Assignment changed. Reload before editing.", 409);
            assignment.Title = request.Title.Trim(); assignment.Instructions = Trim(request.Description);
            assignment.MaxMarks = request.TotalMark; assignment.DueAt = request.DueDate;
            assignment.IsPublished = true; assignment.UpdatedAt = now; assignment.UpdatedBy = _user.UserId;
            await _uow.SaveChangesAsync(ct);
            return ApiResponse<LmsAssignmentDto>.SuccessResponse(MapAssignment(assignment, null),
                "Assignment saved.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Assignment conflict for tenant {TenantId}", _user.TenantId);
            return Fail<LmsAssignmentDto>("Assignment conflicts with another change.", 409);
        }
    }

    public async Task<ApiResponse<int>> EnrollClassAsync(Guid courseReference, CancellationToken ct = default)
    {
        var course = await EditableCourseAsync(courseReference, ct);
        if (course == null) return Fail<int>("Course not found or not editable.", 403);
        var ids = await CohortBatchIdsAsync(course.Id, ct);
        if (ids.Count != 1)
            return Fail<int>("Course has ambiguous or missing cohort mapping. Use the canonical course enrollment API.", 409);
        try
        {
            using var tx = SerializableScope();
            var added = await SynchronizeAsync(course.Id, ids[0], ct);
            await _uow.SaveChangesAsync(ct);
            tx.Complete();
            return ApiResponse<int>.SuccessResponse(added, "Course enrollment synchronized.");
        }
        catch (DbUpdateException) { return Fail<int>("Course enrollment conflicts with existing records.", 409); }
        catch (TransactionAbortedException) { return Fail<int>("Concurrent course enrollment rejected.", 409); }
    }

    public async Task<ApiResponse<IReadOnlyList<LmsInstructorOptionDto>>> GetInstructorOptionsAsync(string? search, CancellationToken ct = default)
    {
        if (!IsManager() || !_user.IsAuthenticated || _user.TenantId <= 0)
            return Fail<IReadOnlyList<LmsInstructorOptionDto>>("LMS management permission required.", 403);
        if (search?.Length > 100)
            return Fail<IReadOnlyList<LmsInstructorOptionDto>>("Instructor search is too long.");
        var term = search?.Trim();
        var query = _employees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.CanTeach && x.State == EmployeeState.Active);
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.FullName.StartsWith(term) || x.EmployeeCode.StartsWith(term));
        IReadOnlyList<LmsInstructorOptionDto> rows = await query.OrderBy(x => x.EmployeeCode).ThenBy(x => x.Id)
            .Select(x => new LmsInstructorOptionDto { Id = x.Id, Name = x.FullName, EmployeeCode = x.EmployeeCode })
            .Take(100).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<LmsInstructorOptionDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<LmsCourseDto>>> GetMyCoursesAsync(CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated || _user.TenantId <= 0)
            return Fail<IReadOnlyList<LmsCourseDto>>("Authentication is required.", 403);
        var tenant = _user.TenantId;
        var student = _user.IsInRole("Student")
            ? await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.UserId == _user.UserId && x.IsActive, ct) : null;
        if (_user.IsInRole("Student") && student == null)
            return ApiResponse<IReadOnlyList<LmsCourseDto>>.SuccessResponse(Array.Empty<LmsCourseDto>());
        if (!_user.IsInRole("Student") && !CanTeach())
            return Fail<IReadOnlyList<LmsCourseDto>>("LMS permission required.", 403);
        var teacherId = !_user.IsInRole("Student") && !IsManager() ?
            await _employees.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.UserId == _user.UserId && x.CanTeach && x.State == EmployeeState.Active)
                .Select(x => x.Id).FirstOrDefaultAsync(ct) : 0;
        if (!_user.IsInRole("Student") && !IsManager() && teacherId == 0)
            return ApiResponse<IReadOnlyList<LmsCourseDto>>.SuccessResponse(Array.Empty<LmsCourseDto>());
        var query = _courses.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && x.IsActive);
        if (student != null)
        {
            var courseIds = _courseEnrollments.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && x.StudentId == student.Id &&
                    (x.State == CourseEnrollmentState.Active || x.State == CourseEnrollmentState.Completed))
                .Select(x => x.CourseId);
            query = query.Where(x => courseIds.Contains(x.Id));
        }
        else if (!IsManager()) query = query.Where(x => x.PrimaryInstructorEmployeeId == teacherId);
        var courses = await query.OrderBy(x => x.Title).ThenBy(x => x.Id).Take(200).ToListAsync(ct);
        var result = new List<LmsCourseDto>();
        foreach (var course in courses) result.Add(await MapCourseAsync(course, student?.Id, ct));
        return ApiResponse<IReadOnlyList<LmsCourseDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<LmsCourseDetailsDto>> GetCourseAsync(Guid reference, CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated || _user.TenantId <= 0 || reference == Guid.Empty)
            return Fail<LmsCourseDetailsDto>("Authenticated course reference required.", 403);
        var tenant = _user.TenantId;
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
            x.PublicId == reference && x.IsActive, ct);
        if (course == null) return Fail<LmsCourseDetailsDto>("Course not found.", 404);
        Student? student = null;
        CourseEnrollment? enrollment = null;
        if (_user.IsInRole("Student"))
        {
            student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.UserId == _user.UserId && x.IsActive, ct);
            if (student == null) return Fail<LmsCourseDetailsDto>("Student profile not found.", 403);
            enrollment = await _courseEnrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.CourseId == course.Id && x.StudentId == student.Id &&
                (x.State == CourseEnrollmentState.Active || x.State == CourseEnrollmentState.Completed), ct);
            if (enrollment == null) return Fail<LmsCourseDetailsDto>("Student is not enrolled.", 403);
        }
        else if (!await CanEditAsync(course, ct)) return Fail<LmsCourseDetailsDto>("Course access denied.", 403);
        var lessons = await _lessons.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.CourseId == course.Id && x.IsPublished).OrderBy(x => x.DisplayOrder).Take(200).ToListAsync(ct);
        var resources = await _resources.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            lessons.Select(v => v.Id).Contains(x.LessonId) && x.Title == "Legacy attachment").ToListAsync(ct);
        var res = resources.ToDictionary(x => x.LessonId, x => x.ExternalUrl);
        var assignments = await _assignments.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.CourseId == course.Id && x.IsPublished).OrderBy(x => x.DueAt).Take(200).ToListAsync(ct);
        var completed = enrollment == null ? Array.Empty<long>() : await _progress.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.CourseEnrollmentId == enrollment.Id && x.IsCompleted)
            .Select(x => x.LessonId).ToArrayAsync(ct);
        var assignmentIds = assignments.Select(x => x.Id).ToArray();
        var submissions = enrollment == null ? new List<AssignmentSubmission>() : await _submissions.GetQueryable()
            .AsNoTracking().Where(x => x.TenantId == tenant && x.CourseEnrollmentId == enrollment.Id &&
                assignmentIds.Contains(x.AssignmentId)).ToListAsync(ct);
        var byAssignment = submissions.GroupBy(x => x.AssignmentId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(v => v.Id).First());
        return ApiResponse<LmsCourseDetailsDto>.SuccessResponse(new LmsCourseDetailsDto
        {
            Course = await MapCourseAsync(course, student?.Id, ct),
            Lessons = lessons.Select(x => MapLesson(x, completed.Contains(x.Id), res.GetValueOrDefault(x.Id))).ToList(),
            Assignments = assignments.Select(x => MapAssignment(x, byAssignment.GetValueOrDefault(x.Id))).ToList()
        });
    }

    public async Task<ApiResponse<LmsAssignmentDto>> SubmitAssignmentAsync(SubmitAssignmentDto request, CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated || !_user.IsInRole("Student") || _user.TenantId <= 0)
            return Fail<LmsAssignmentDto>("Student assignment permission required.", 403);
        if (request == null || request.AssignmentReference == Guid.Empty || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.SubmissionText) || request.SubmissionText.Length > 4000 ||
            !string.IsNullOrWhiteSpace(request.SubmissionFile))
            return Fail<LmsAssignmentDto>("Submit assignment text; files must use the canonical FileAsset submission API.");
        var tenant = _user.TenantId;
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
            x.UserId == _user.UserId && x.IsActive, ct);
        if (student == null) return Fail<LmsAssignmentDto>("Student profile not found.", 403);
        try
        {
            using var tx = SerializableScope();
            var assignment = await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.AssignmentReference && x.IsPublished, ct);
            if (assignment == null) return Fail<LmsAssignmentDto>("Assignment not found.", 404);
            var enrollment = await _courseEnrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.CourseId == assignment.CourseId && x.StudentId == student.Id &&
                x.State == CourseEnrollmentState.Active, ct);
            if (enrollment == null) return Fail<LmsAssignmentDto>("Student is not enrolled in this course.", 403);
            var existing = await _submissions.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.AssignmentId == assignment.Id && x.CourseEnrollmentId == enrollment.Id, ct);
            if (existing != null)
            {
                if (existing.SubmissionText != request.SubmissionText.Trim())
                    return Fail<LmsAssignmentDto>("Existing submission cannot be overwritten. Request an assignment return for revision.", 409);
                tx.Complete();
                return ApiResponse<LmsAssignmentDto>.SuccessResponse(MapAssignment(assignment, existing),
                    "Submission already recorded.");
            }
            var now = _clock.GetUtcNow().UtcDateTime;
            var submission = new AssignmentSubmission
            {
                TenantId = tenant, AssignmentId = assignment.Id, CourseEnrollmentId = enrollment.Id,
                SubmissionText = request.SubmissionText.Trim(), SubmittedAt = now,
                State = LearningSubmissionState.Submitted, CreatedAt = now, CreatedBy = _user.UserId
            };
            await _submissions.AddAsync(submission);
            await _uow.SaveChangesAsync(ct);
            tx.Complete();
            return ApiResponse<LmsAssignmentDto>.SuccessResponse(MapAssignment(assignment, submission),
                "Assignment submitted.");
        }
        catch (DbUpdateException) { return Fail<LmsAssignmentDto>("Submission conflicts with existing records.", 409); }
        catch (TransactionAbortedException) { return Fail<LmsAssignmentDto>("Concurrent submission rejected.", 409); }
    }

    public async Task<ApiResponse<bool>> ReviewSubmissionAsync(ReviewSubmissionDto request, CancellationToken ct = default)
    {
        if (!CanTeach()) return Fail<bool>("Assignment grading permission required.", 403);
        if (request == null || request.SubmissionReference == Guid.Empty || request.Mark < 0m ||
            !TryDecodeSubmission(request.SubmissionReference, _user.TenantId, out var submissionId))
            return Fail<bool>("Submission reference or mark is invalid.");
        var tenant = _user.TenantId;
        try
        {
            using var tx = SerializableScope();
            var submission = await _submissions.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == submissionId, ct);
            if (submission == null) return Fail<bool>("Submission not found.", 404);
            var assignment = await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == submission.AssignmentId, ct);
            if (assignment == null) return Fail<bool>("Assignment not found.", 404);
            var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == assignment.CourseId, ct);
            if (course == null || !await CanEditAsync(course, ct))
                return Fail<bool>("Submission belongs to another teacher.", 403);
            if (request.Mark > assignment.MaxMarks)
                return Fail<bool>("Mark exceeds the assignment maximum.");
            if (submission.State == LearningSubmissionState.Graded)
            {
                if (submission.Marks != request.Mark || submission.Feedback != Trim(request.Feedback))
                    return Fail<bool>("Graded submissions are immutable without an audited regrade workflow.", 409);
                tx.Complete();
                return ApiResponse<bool>.SuccessResponse(true, "Submission already graded.");
            }
            if (submission.State != LearningSubmissionState.Submitted)
                return Fail<bool>("Only submitted assignments may be graded.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            submission.Marks = request.Mark; submission.Feedback = Trim(request.Feedback);
            submission.State = LearningSubmissionState.Graded;
            submission.GradedByUserId = _user.UserId; submission.GradedAt = now;
            submission.UpdatedAt = now; submission.UpdatedBy = _user.UserId;
            await _uow.SaveChangesAsync(ct);
            tx.Complete();
            return ApiResponse<bool>.SuccessResponse(true, "Assignment graded.");
        }
        catch (DbUpdateException) { return Fail<bool>("Concurrent grading conflict.", 409); }
        catch (TransactionAbortedException) { return Fail<bool>("Concurrent grading transaction rejected.", 409); }
    }

    public async Task<ApiResponse<decimal>> CompleteLessonAsync(CompleteLessonDto request, CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated || !_user.IsInRole("Student") || _user.TenantId <= 0)
            return Fail<decimal>("Student lesson access required.", 403);
        if (request == null || request.LessonReference == Guid.Empty)
            return Fail<decimal>("Lesson reference required.");
        var tenant = _user.TenantId;
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
            x.UserId == _user.UserId && x.IsActive, ct);
        if (student == null) return Fail<decimal>("Student not found.", 403);
        try
        {
            using var tx = SerializableScope();
            var lesson = await _lessons.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == request.LessonReference && x.IsPublished, ct);
            if (lesson == null) return Fail<decimal>("Lesson not found.", 404);
            var enrollment = await _courseEnrollments.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.CourseId == lesson.CourseId && x.StudentId == student.Id &&
                (x.State == CourseEnrollmentState.Active || x.State == CourseEnrollmentState.Completed), ct);
            if (enrollment == null) return Fail<decimal>("Student is not enrolled.", 403);
            var progress = await _progress.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.CourseEnrollmentId == enrollment.Id && x.LessonId == lesson.Id, ct);
            var now = _clock.GetUtcNow().UtcDateTime;
            if (progress == null)
            {
                progress = new LessonProgress
                {
                    TenantId = tenant, CourseEnrollmentId = enrollment.Id, LessonId = lesson.Id,
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _progress.AddAsync(progress);
            }
            progress.IsCompleted = true; progress.CompletedAt = now;
            progress.LastAccessedAt = now; progress.ProgressPercent = 100m;
            progress.UpdatedAt = now; progress.UpdatedBy = _user.UserId;
            await _uow.SaveChangesAsync(ct);
            var total = await _lessons.GetQueryable().AsNoTracking().CountAsync(x => x.TenantId == tenant &&
                x.CourseId == lesson.CourseId && x.IsPublished, ct);
            var completed = await (from entry in _progress.GetQueryable().AsNoTracking()
                join courseLesson in _lessons.GetQueryable().AsNoTracking() on entry.LessonId equals courseLesson.Id
                where entry.TenantId == tenant && courseLesson.TenantId == tenant &&
                    entry.CourseEnrollmentId == enrollment.Id && entry.IsCompleted &&
                    courseLesson.CourseId == lesson.CourseId && courseLesson.IsPublished
                select entry.Id).CountAsync(ct);
            var percentage = total == 0 ? 0m : Math.Round(100m * completed / total, 2);
            if (percentage >= 100m && enrollment.State != CourseEnrollmentState.Completed)
            {
                enrollment.State = CourseEnrollmentState.Completed;
                enrollment.CompletedAt = now;
                enrollment.UpdatedAt = now; enrollment.UpdatedBy = _user.UserId;
                await _uow.SaveChangesAsync(ct);
            }
            tx.Complete();
            return ApiResponse<decimal>.SuccessResponse(percentage, "Lesson completion recorded.");
        }
        catch (DbUpdateException) { return Fail<decimal>("Lesson progress conflicted with another request.", 409); }
        catch (TransactionAbortedException) { return Fail<decimal>("Concurrent lesson progress rejected.", 409); }
    }

    private async Task<int> SynchronizeAsync(long courseId, long batchId, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var enrollments = await _academicEnrollments.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.AcademicBatchId == batchId && x.IsCurrent && x.IsActive && x.State == EnrollmentState.Active)
            .Select(x => new { x.Id, x.StudentId }).ToListAsync(ct);
        var studentIds = enrollments.Select(x => x.StudentId).ToArray();
        var activeStudents = await _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            studentIds.Contains(x.Id) && x.IsActive).Select(x => x.Id).ToArrayAsync(ct);
        var active = activeStudents.ToHashSet();
        var existing = await _courseEnrollments.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.CourseId == courseId && studentIds.Contains(x.StudentId))
            .Select(x => x.StudentId).ToArrayAsync(ct);
        var registered = existing.ToHashSet();
        var now = _clock.GetUtcNow().UtcDateTime;
        var count = 0;
        foreach (var row in enrollments)
        {
            if (!active.Contains(row.StudentId) || registered.Contains(row.StudentId)) continue;
            await _courseEnrollments.AddAsync(new CourseEnrollment
            {
                TenantId = tenant, ClientRequestId = CohortKey(courseId, row.Id),
                CourseId = courseId, StudentId = row.StudentId, StudentEnrollmentId = row.Id,
                State = CourseEnrollmentState.Active, EnrolledAt = now, CreatedAt = now, CreatedBy = _user.UserId
            });
            count++;
        }
        return count;
    }
    private async Task<List<long>> CohortBatchIdsAsync(long courseId, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        return await (from linked in _courseEnrollments.GetQueryable().AsNoTracking()
            join enrolled in _academicEnrollments.GetQueryable().AsNoTracking()
                on linked.StudentEnrollmentId equals enrolled.Id
            where linked.TenantId == tenant && enrolled.TenantId == tenant && linked.CourseId == courseId
            select enrolled.AcademicBatchId).Distinct().Take(2).ToListAsync(ct);
    }
    private async Task<Employee?> ResolveTeacherAsync(long? requested, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var q = _employees.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.CanTeach && x.State == EmployeeState.Active);
        if (!IsManager()) return await q.FirstOrDefaultAsync(x => x.UserId == _user.UserId &&
            (!requested.HasValue || x.Id == requested.Value), ct);
        if (requested.HasValue) return await q.FirstOrDefaultAsync(x => x.Id == requested.Value, ct);
        return await q.FirstOrDefaultAsync(x => x.UserId == _user.UserId, ct);
    }
    private async Task<Course?> EditableCourseAsync(Guid reference, CancellationToken ct)
    {
        if (!CanTeach()) return null;
        var course = await _courses.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == _user.TenantId &&
            x.PublicId == reference && x.IsActive, ct);
        return course != null && await CanEditAsync(course, ct) ? course : null;
    }
    private async Task<bool> CanEditAsync(Course course, CancellationToken ct)
    {
        if (IsManager()) return true;
        if (!_user.IsInRole("Teacher") || !course.PrimaryInstructorEmployeeId.HasValue) return false;
        return await _employees.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == _user.TenantId && x.Id == course.PrimaryInstructorEmployeeId &&
            x.UserId == _user.UserId && x.CanTeach && x.State == EmployeeState.Active, ct);
    }
    private async Task<LmsCourseDto> MapCourseAsync(Course course, long? studentId, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var teacher = course.PrimaryInstructorEmployeeId.HasValue
            ? await _employees.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == course.PrimaryInstructorEmployeeId.Value, ct) : null;
        var subjectName = course.SubjectId.HasValue
            ? await _subjects.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.Id == course.SubjectId.Value).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var anchor = await (from link in _courseEnrollments.GetQueryable().AsNoTracking()
            join academic in _academicEnrollments.GetQueryable().AsNoTracking()
                on link.StudentEnrollmentId equals academic.Id
            where link.TenantId == tenant && academic.TenantId == tenant &&
                link.CourseId == course.Id &&
                (!studentId.HasValue || link.StudentId == studentId.Value)
            orderby link.Id
            select new { academic.AcademicYearId, academic.AcademicLevelId, academic.AcademicBatchId })
            .FirstOrDefaultAsync(ct);
        decimal completion = 0m;
        if (studentId.HasValue)
        {
            var courseEnrollment = await _courseEnrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.CourseId == course.Id && x.StudentId == studentId.Value, ct);
            if (courseEnrollment != null)
            {
                var count = await _lessons.GetQueryable().AsNoTracking().CountAsync(x => x.TenantId == tenant &&
                    x.CourseId == course.Id && x.IsPublished, ct);
                if (count > 0)
                {
                    var done = await _progress.GetQueryable().AsNoTracking().CountAsync(x => x.TenantId == tenant &&
                        x.CourseEnrollmentId == courseEnrollment.Id && x.IsCompleted, ct);
                    completion = Math.Min(100m, Math.Round(done * 100m / count, 2));
                }
            }
        }
        return new LmsCourseDto
        {
            Reference = course.PublicId, Title = course.Title,
            AcademicYearId = anchor?.AcademicYearId ?? 0, ClassId = anchor?.AcademicLevelId ?? 0,
            SectionId = anchor?.AcademicBatchId, SubjectId = course.SubjectId ?? 0,
            SubjectName = subjectName ?? "", TeacherId = teacher?.Id ?? 0,
            TeacherName = teacher?.FullName ?? "", ProgressPercentage = completion,
            RowVersion = Convert.ToBase64String(course.RowVersion)
        };
    }
    private static LmsLessonDto MapLesson(Lesson row, bool complete, string? attachment) => new()
    {
        Reference = row.PublicId, Title = row.Title, Content = row.Content,
        VideoUrl = row.ContentUrl, AttachmentUrl = attachment,
        OrderNo = row.DisplayOrder, Duration = 0, IsCompleted = complete,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };
    private LmsAssignmentDto MapAssignment(Assignment row, AssignmentSubmission? submission) => new()
    {
        Reference = row.PublicId, Title = row.Title, Description = row.Instructions,
        TotalMark = (int)row.MaxMarks, DueDate = row.DueAt ?? DateTime.MaxValue,
        SubmissionReference = submission == null ? null : EncodeSubmission(submission.Id, _user.TenantId),
        SubmissionStatus = submission?.State.ToString(), Mark = submission?.Marks,
        Feedback = submission?.Feedback, RowVersion = Convert.ToBase64String(row.RowVersion)
    };
    private static Guid EncodeSubmission(long id, long tenant)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(id).CopyTo(bytes, 0);
        BitConverter.GetBytes(tenant).CopyTo(bytes, 8);
        return new Guid(bytes);
    }
    private static bool TryDecodeSubmission(Guid reference, long tenant, out long id)
    {
        var bytes = reference.ToByteArray();
        id = BitConverter.ToInt64(bytes, 0);
        return id > 0 && BitConverter.ToInt64(bytes, 8) == tenant;
    }
    private static Guid CohortKey(long courseId, long enrollmentId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(courseId + "|" + enrollmentId));
        return new Guid(bytes.AsSpan(0, 16));
    }
    private static bool MatchesVersion(byte[] actual, string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return false;
        try { return actual.AsSpan().SequenceEqual(Convert.FromBase64String(base64)); }
        catch (FormatException) { return false; }
    }
    private bool IsManager() => _user.IsTenantAdmin || _user.IsInRole("Principal");
    private bool CanTeach() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (IsManager() || _user.IsInRole("Teacher"));
    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static TransactionScope SerializableScope() =>
        new(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable },
            TransactionScopeAsyncFlowOption.Enabled);
    private static ApiResponse<T> Fail<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);
}
