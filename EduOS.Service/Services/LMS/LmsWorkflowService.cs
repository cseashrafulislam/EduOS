using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
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

namespace EduOS.Service.Services.LMS;

public sealed class LmsWorkflowService : ILmsWorkflowService
{
    private readonly IGenericRepository<Course> _courses;
    private readonly IGenericRepository<CourseEnrollment> _enrollments;
    private readonly IGenericRepository<Lesson> _lessons;
    private readonly IGenericRepository<LessonProgress> _progress;
    private readonly IGenericRepository<Assignment> _assignments;
    private readonly IGenericRepository<AssignmentSubmission> _submissions;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<StudentEnrollment> _academicEnrollments;
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<AcademicProgram> _programs;
    private readonly IGenericRepository<Subject> _subjects;
    private readonly IGenericRepository<SubjectOffering> _offerings;
    private readonly IGenericRepository<CurriculumSubject> _curriculumSubjects;
    private readonly IGenericRepository<FileAsset> _files;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<LmsWorkflowService> _logger;

    public LmsWorkflowService(IGenericRepository<Course> courses,
        IGenericRepository<CourseEnrollment> enrollments, IGenericRepository<Lesson> lessons,
        IGenericRepository<LessonProgress> progress, IGenericRepository<Assignment> assignments,
        IGenericRepository<AssignmentSubmission> submissions, IGenericRepository<Student> students,
        IGenericRepository<StudentEnrollment> academicEnrollments, IGenericRepository<Employee> employees,
        IGenericRepository<AcademicProgram> programs, IGenericRepository<Subject> subjects,
        IGenericRepository<SubjectOffering> offerings, IGenericRepository<CurriculumSubject> curriculumSubjects,
        IGenericRepository<FileAsset> files, IUnitOfWork uow, ICurrentUserService user,
        TimeProvider clock, ILogger<LmsWorkflowService> logger)
    {
        _courses = courses; _enrollments = enrollments; _lessons = lessons; _progress = progress;
        _assignments = assignments; _submissions = submissions; _students = students;
        _academicEnrollments = academicEnrollments; _employees = employees; _programs = programs;
        _subjects = subjects; _offerings = offerings; _curriculumSubjects = curriculumSubjects;
        _files = files; _uow = uow; _user = user; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<CourseDto>> SaveCourseAsync(Guid? courseReference, SaveCourseRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanTeach()) return Error<CourseDto>("LMS authoring permission required.", 403);
        if (request == null || string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 50 ||
            request.Description?.Length > 4000 || request.ThumbnailUrl?.Length > 500 ||
            request.AcademicProgramId is <= 0 || request.SubjectId is <= 0 ||
            (courseReference.HasValue && courseReference.Value == Guid.Empty))
            return Error<CourseDto>("Invalid course title, code, program, subject or reference.");
        var tenant = _user.TenantId;
        Employee? teacher = null;
        if (request.PrimaryInstructorReference.HasValue)
        {
            teacher = await _employees.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.PrimaryInstructorReference.Value &&
                x.CanTeach && x.State == EmployeeState.Active && !x.IsDeleted, ct);
            if (teacher == null) return Error<CourseDto>("Active instructor not found.", 404);
        }
        if (!IsManager())
        {
            var self = await OwnTeacherIdAsync(ct);
            if (self == 0 || (teacher != null && teacher.Id != self))
                return Error<CourseDto>("Instructor may only manage their own courses.", 403);
            teacher ??= await _employees.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == self, ct);
        }
        if (request.AcademicProgramId.HasValue && !await _programs.GetQueryable().AsNoTracking()
            .AnyAsync(x => x.TenantId == tenant && x.Id == request.AcademicProgramId && x.IsActive && !x.IsDeleted, ct))
            return Error<CourseDto>("Academic program not found.", 404);
        if (request.SubjectId.HasValue && !await _subjects.GetQueryable().AsNoTracking()
            .AnyAsync(x => x.TenantId == tenant && x.Id == request.SubjectId && x.IsActive && !x.IsDeleted, ct))
            return Error<CourseDto>("Subject not found.", 404);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var row = courseReference.HasValue ? await _courses.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.PublicId == courseReference.Value && !x.IsDeleted, token) : null;
                if (courseReference.HasValue && row == null) return Error<CourseDto>("Course not found.", 404);
                if (row != null && (!await CanEditAsync(row, token) || !Matches(row.RowVersion, request.RowVersion)))
                    return Error<CourseDto>("Course access denied or row version changed.", 409);
                var code = request.Code.Trim();
                if (await _courses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Code == code && !x.IsDeleted && (row == null || x.Id != row.Id), token))
                    return Error<CourseDto>("Course code already exists.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                if (row == null)
                {
                    row = new Course
                    {
                        TenantId = tenant, PublicId = Guid.NewGuid(), CreatedAt = now, CreatedBy = _user.UserId
                    };
                    await _courses.AddAsync(row);
                }
                else
                {
                    row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
                    _courses.Update(row);
                }
                row.Title = request.Title.Trim(); row.Code = code;
                row.Description = Trim(request.Description); row.ThumbnailUrl = Trim(request.ThumbnailUrl);
                row.AcademicProgramId = request.AcademicProgramId; row.SubjectId = request.SubjectId;
                row.PrimaryInstructorEmployeeId = teacher?.Id;
                row.IsSelfPaced = request.IsSelfPaced; row.IsActive = request.IsActive;
                await _uow.SaveChangesAsync(token);
                return ApiResponse<CourseDto>.SuccessResponse(await MapCourseAsync(row, token), "Course saved.");
            }, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<CourseDto>("Course changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Course constraint violation for tenant {TenantId}", tenant);
            return Error<CourseDto>("Course conflicts with another request.", 409);
        }
    }

    public async Task<ApiResponse<LessonDto>> SaveLessonAsync(Guid? lessonReference, SaveLessonRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanTeach()) return Error<LessonDto>("LMS authoring permission required.", 403);
        if (request == null || request.CourseReference == Guid.Empty || string.IsNullOrWhiteSpace(request.Title) ||
            request.Title.Trim().Length > 200 || request.Content?.Length > 4000 ||
            request.ContentUrl?.Length > 500 || request.DisplayOrder < 0)
            return Error<LessonDto>("Invalid lesson fields.");
        var course = await EditableCourseAsync(request.CourseReference, ct);
        if (course == null) return Error<LessonDto>("Course not found or instructor access denied.", 403);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var row = lessonReference.HasValue ? await _lessons.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == _user.TenantId && x.PublicId == lessonReference.Value &&
                    x.CourseId == course.Id && !x.IsDeleted, token) : null;
                if (lessonReference.HasValue && row == null) return Error<LessonDto>("Lesson not found.", 404);
                if (row != null && !Matches(row.RowVersion, request.RowVersion))
                    return Error<LessonDto>("Lesson changed. Reload and retry.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                if (row == null)
                {
                    row = new Lesson
                    {
                        TenantId = _user.TenantId, PublicId = Guid.NewGuid(), CourseId = course.Id,
                        CreatedAt = now, CreatedBy = _user.UserId
                    };
                    await _lessons.AddAsync(row);
                }
                else
                {
                    row.UpdatedAt = now; row.UpdatedBy = _user.UserId; _lessons.Update(row);
                }
                row.Title = request.Title.Trim(); row.Content = Trim(request.Content);
                row.ContentUrl = Trim(request.ContentUrl); row.DisplayOrder = request.DisplayOrder;
                row.IsPublished = request.IsPublished;
                await _uow.SaveChangesAsync(token);
                return ApiResponse<LessonDto>.SuccessResponse(MapLesson(row, course.PublicId), "Lesson saved.");
            }, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<LessonDto>("Lesson changed concurrently.", 409); }
        catch (DbUpdateException) { return Error<LessonDto>("Lesson update conflicts with existing data.", 409); }
    }

    public async Task<ApiResponse<AssignmentDto>> SaveAssignmentAsync(Guid? assignmentReference,
        SaveAssignmentRequestDto request, CancellationToken ct = default)
    {
        if (!CanTeach()) return Error<AssignmentDto>("LMS authoring permission required.", 403);
        if (request == null || request.CourseReference == Guid.Empty || string.IsNullOrWhiteSpace(request.Title) ||
            request.Title.Trim().Length > 200 || request.Instructions?.Length > 4000 ||
            request.MaxMarks < 0 || request.MaxMarks > 100000m || !Enum.IsDefined(request.Type) ||
            (request.OpensAt.HasValue && request.DueAt.HasValue && request.OpensAt >= request.DueAt))
            return Error<AssignmentDto>("Invalid assignment fields or date range.");
        var course = await EditableCourseAsync(request.CourseReference, ct);
        if (course == null) return Error<AssignmentDto>("Course not found or instructor access denied.", 403);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var row = assignmentReference.HasValue ? await _assignments.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == _user.TenantId && x.PublicId == assignmentReference.Value &&
                    x.CourseId == course.Id && !x.IsDeleted, token) : null;
                if (assignmentReference.HasValue && row == null) return Error<AssignmentDto>("Assignment not found.", 404);
                if (row != null && !Matches(row.RowVersion, request.RowVersion))
                    return Error<AssignmentDto>("Assignment changed. Reload and retry.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                if (row == null)
                {
                    row = new Assignment
                    {
                        TenantId = _user.TenantId, CourseId = course.Id, PublicId = Guid.NewGuid(),
                        CreatedAt = now, CreatedBy = _user.UserId
                    };
                    await _assignments.AddAsync(row);
                }
                else
                {
                    row.UpdatedAt = now; row.UpdatedBy = _user.UserId; _assignments.Update(row);
                }
                row.Type = request.Type; row.Title = request.Title.Trim();
                row.Instructions = Trim(request.Instructions); row.OpensAt = request.OpensAt;
                row.DueAt = request.DueAt; row.MaxMarks = request.MaxMarks;
                row.IsPublished = request.IsPublished;
                await _uow.SaveChangesAsync(token);
                return ApiResponse<AssignmentDto>.SuccessResponse(MapAssignment(row, course.PublicId), "Assignment saved.");
            }, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<AssignmentDto>("Assignment changed concurrently.", 409); }
        catch (DbUpdateException) { return Error<AssignmentDto>("Assignment conflicts with existing data.", 409); }
    }

    public async Task<ApiResponse<CourseEnrollmentDto>> EnrollStudentAsync(EnrollCourseRequestDto request,
        CancellationToken ct = default)
    {
        if (!Authenticated()) return Error<CourseEnrollmentDto>("Authentication required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.CourseReference == Guid.Empty || request.StudentReference == Guid.Empty)
            return Error<CourseEnrollmentDto>("Course, student and request reference are required.");
        var tenant = _user.TenantId;
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == request.StudentReference &&
            x.StatusCode == "Active" && !x.IsDeleted, ct);
        if (student == null) return Error<CourseEnrollmentDto>("Student not found.", 404);
        if (!IsManager() && student.UserId != _user.UserId)
            return Error<CourseEnrollmentDto>("Cannot enroll another student.", 403);
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == request.CourseReference && x.IsActive && !x.IsDeleted, ct);
        if (course == null) return Error<CourseEnrollmentDto>("Course not found.", 404);
        StudentEnrollment? academic = null;
        if (request.StudentEnrollmentReference.HasValue)
        {
            academic = await _academicEnrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.StudentEnrollmentReference.Value &&
                x.StudentId == student.Id && x.State == EnrollmentState.Active && x.IsCurrent && !x.IsDeleted, ct);
            if (academic == null) return Error<CourseEnrollmentDto>("Active academic enrollment not found.", 409);
        }
        else if (course.AcademicProgramId.HasValue || course.SubjectId.HasValue)
        {
            academic = await _academicEnrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.StudentId == student.Id && x.State == EnrollmentState.Active &&
                x.IsCurrent && !x.IsDeleted, ct);
            if (academic == null) return Error<CourseEnrollmentDto>("An active academic enrollment is required.", 409);
        }
        if (academic != null)
        {
            if (course.AcademicProgramId.HasValue && academic.AcademicProgramId != course.AcademicProgramId)
                return Error<CourseEnrollmentDto>("Course does not belong to the student's academic program.", 409);
            if (course.SubjectId.HasValue)
            {
                var subjectAllowed = await (from offering in _offerings.GetQueryable().AsNoTracking()
                    join item in _curriculumSubjects.GetQueryable().AsNoTracking()
                        on offering.CurriculumSubjectId equals item.Id
                    where offering.TenantId == tenant && item.TenantId == tenant && offering.IsActive &&
                        item.IsActive && offering.AcademicBatchId == academic.AcademicBatchId &&
                        offering.AcademicYearId == academic.AcademicYearId &&
                        item.AcademicCurriculumId == academic.AcademicCurriculumId &&
                        item.SubjectId == course.SubjectId.Value
                    select offering.Id).AnyAsync(ct);
                if (!subjectAllowed) return Error<CourseEnrollmentDto>("Course subject is unavailable for this academic batch.", 409);
            }
        }
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var replay = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, token);
                if (replay != null)
                {
                    if (replay.CourseId != course.Id || replay.StudentId != student.Id ||
                        replay.StudentEnrollmentId != academic?.Id)
                        return Error<CourseEnrollmentDto>("Request reference was reused for another enrollment.", 409);
                    return ApiResponse<CourseEnrollmentDto>.SuccessResponse(
                        MapEnrollment(replay, course, student, academic), "Enrollment already exists.");
                }
                var duplicate = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.CourseId == course.Id && x.StudentId == student.Id &&
                    x.StudentEnrollmentId == (academic == null ? (long?)null : academic.Id) && !x.IsDeleted, token);
                if (duplicate != null)
                    return ApiResponse<CourseEnrollmentDto>.SuccessResponse(
                        MapEnrollment(duplicate, course, student, academic), "Student is already enrolled.");
                var now = _clock.GetUtcNow().UtcDateTime;
                var row = new CourseEnrollment
                {
                    TenantId = tenant, ClientRequestId = request.ClientRequestId,
                    CourseId = course.Id, StudentId = student.Id, StudentEnrollmentId = academic?.Id,
                    State = CourseEnrollmentState.Active, EnrolledAt = now,
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _enrollments.AddAsync(row);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<CourseEnrollmentDto>.SuccessResponse(
                    MapEnrollment(row, course, student, academic), "Student enrolled.");
            }, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Course enrollment conflict for tenant {TenantId}", tenant);
            return Error<CourseEnrollmentDto>("Course enrollment conflicts with another request.", 409);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<AcademicInstructorChoiceDto>>> SearchInstructorsAsync(
        string search, int take = 20, CancellationToken ct = default)
    {
        if (!IsManager() || !Authenticated())
            return Error<IReadOnlyList<AcademicInstructorChoiceDto>>("LMS management permission required.", 403);
        if (search?.Length > 100) return Error<IReadOnlyList<AcademicInstructorChoiceDto>>("Search is too long.");
        var q = _employees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.CanTeach && x.State == EmployeeState.Active && !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(x => x.FullName.StartsWith(term) || x.EmployeeCode.StartsWith(term));
        }
        IReadOnlyList<AcademicInstructorChoiceDto> rows = await q.OrderBy(x => x.EmployeeCode)
            .ThenBy(x => x.Id).Take(Math.Clamp(take, 1, 100))
            .Select(x => new AcademicInstructorChoiceDto
            {
                EmployeeReference = x.PublicId, EmployeeCode = x.EmployeeCode, Name = x.FullName
            }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<AcademicInstructorChoiceDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<PagedResult<CourseDto>>> GetMyCoursesAsync(int page, int pageSize,
        CancellationToken ct = default)
    {
        if (!Authenticated()) return Error<PagedResult<CourseDto>>("Authentication required.", 403);
        if (page < 1 || pageSize is < 1 or > 100)
            return Error<PagedResult<CourseDto>>("Invalid page or page size.");
        var q = _courses.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.IsActive && !x.IsDeleted);
        if (!IsManager())
        {
            if (_user.IsInRole("Student"))
            {
                var studentId = await OwnStudentIdAsync(ct);
                if (studentId == 0) return ApiResponse<PagedResult<CourseDto>>.SuccessResponse(EmptyPage<CourseDto>(page, pageSize));
                var ids = _enrollments.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == _user.TenantId && x.StudentId == studentId &&
                    (x.State == CourseEnrollmentState.Active || x.State == CourseEnrollmentState.Completed))
                    .Select(x => x.CourseId);
                q = q.Where(x => ids.Contains(x.Id));
            }
            else if (_user.IsInRole("Teacher"))
            {
                var teacherId = await OwnTeacherIdAsync(ct);
                if (teacherId == 0) return ApiResponse<PagedResult<CourseDto>>.SuccessResponse(EmptyPage<CourseDto>(page, pageSize));
                q = q.Where(x => x.PrimaryInstructorEmployeeId == teacherId);
            }
            else return Error<PagedResult<CourseDto>>("LMS permission required.", 403);
        }
        var total = await q.CountAsync(ct);
        var skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue) return Error<PagedResult<CourseDto>>("Page is outside the allowed range.");
        var courses = await q.OrderBy(x => x.Title).ThenBy(x => x.Id)
            .Skip((int)skip).Take(pageSize).ToListAsync(ct);
        var dtos = new List<CourseDto>(courses.Count);
        foreach (var row in courses) dtos.Add(await MapCourseAsync(row, ct));
        return ApiResponse<PagedResult<CourseDto>>.SuccessResponse(new PagedResult<CourseDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total, Items = dtos
        });
    }

    public async Task<ApiResponse<CourseDetailsDto>> GetCourseDetailsAsync(Guid courseReference,
        CancellationToken ct = default)
    {
        if (!Authenticated() || courseReference == Guid.Empty)
            return Error<CourseDetailsDto>("Course not found.", 404);
        var row = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == courseReference && !x.IsDeleted, ct);
        if (row == null) return Error<CourseDetailsDto>("Course not found.", 404);
        if (!await CanViewAsync(row, ct)) return Error<CourseDetailsDto>("Course not found.", 404);
        var canEdit = await CanEditAsync(row, ct);
        var lessons = await _lessons.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.CourseId == row.Id && !x.IsDeleted &&
            (canEdit || x.IsPublished)).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id)
            .Take(500).ToListAsync(ct);
        var assignments = await _assignments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.CourseId == row.Id && !x.IsDeleted &&
            (canEdit || x.IsPublished)).OrderBy(x => x.DueAt).ThenBy(x => x.Id)
            .Take(500).ToListAsync(ct);
        return ApiResponse<CourseDetailsDto>.SuccessResponse(new CourseDetailsDto
        {
            Course = await MapCourseAsync(row, ct),
            Lessons = lessons.Select(x => MapLesson(x, row.PublicId)).ToList(),
            Assignments = assignments.Select(x => MapAssignment(x, row.PublicId)).ToList()
        });
    }

    public async Task<ApiResponse<AssignmentSubmissionDto>> SubmitAssignmentAsync(SubmitAssignmentRequestDto request,
        CancellationToken ct = default)
    {
        if (!Authenticated() || !_user.IsInRole("Student"))
            return Error<AssignmentSubmissionDto>("Student permission required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.AssignmentReference == Guid.Empty ||
            request.CourseEnrollmentId <= 0 || request.SubmissionText?.Length > 4000 ||
            (string.IsNullOrWhiteSpace(request.SubmissionText) && !request.FileAssetId.HasValue))
            return Error<AssignmentSubmissionDto>("Invalid submission.");
        var tenant = _user.TenantId;
        var assignment = await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == request.AssignmentReference && x.IsPublished && !x.IsDeleted, ct);
        if (assignment == null) return Error<AssignmentSubmissionDto>("Assignment not found.", 404);
        var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == request.CourseEnrollmentId && x.CourseId == assignment.CourseId &&
            x.State == CourseEnrollmentState.Active && !x.IsDeleted, ct);
        if (enrollment == null) return Error<AssignmentSubmissionDto>("Active course enrollment not found.", 404);
        var owns = await _students.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == enrollment.StudentId && x.UserId == _user.UserId &&
            x.StatusCode == "Active" && !x.IsDeleted, ct);
        if (!owns) return Error<AssignmentSubmissionDto>("Submission belongs to another student.", 403);
        var now = _clock.GetUtcNow().UtcDateTime;
        if (assignment.OpensAt.HasValue && assignment.OpensAt > now ||
            assignment.DueAt.HasValue && assignment.DueAt < now)
            return Error<AssignmentSubmissionDto>("Assignment submission window is closed.", 409);
        if (request.FileAssetId.HasValue && !await _files.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == request.FileAssetId.Value && x.IsVerifiedSafe && !x.IsDeleted, ct))
            return Error<AssignmentSubmissionDto>("Safe uploaded file not found.", 409);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var existing = await _submissions.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.AssignmentId == assignment.Id &&
                    x.CourseEnrollmentId == enrollment.Id && !x.IsDeleted, token);
                if (existing != null)
                {
                    if (existing.State == LearningSubmissionState.Submitted &&
                        existing.SubmissionText == Trim(request.SubmissionText) &&
                        existing.FileAssetId == request.FileAssetId)
                        return ApiResponse<AssignmentSubmissionDto>.SuccessResponse(
                            MapSubmission(existing, assignment.PublicId), "Submission already received.");
                    return Error<AssignmentSubmissionDto>("Assignment was already submitted.", 409);
                }
                var row = new AssignmentSubmission
                {
                    TenantId = tenant, AssignmentId = assignment.Id, CourseEnrollmentId = enrollment.Id,
                    SubmissionText = Trim(request.SubmissionText), FileAssetId = request.FileAssetId,
                    State = LearningSubmissionState.Submitted, SubmittedAt = now,
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _submissions.AddAsync(row);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<AssignmentSubmissionDto>.SuccessResponse(MapSubmission(row, assignment.PublicId),
                    "Assignment submitted.");
            }, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "LMS submission conflict tenant {TenantId}", tenant);
            return Error<AssignmentSubmissionDto>("Assignment submission conflicts with existing data.", 409);
        }
    }

    public async Task<ApiResponse<AssignmentSubmissionDto>> GradeSubmissionAsync(long submissionId,
        GradeAssignmentSubmissionRequestDto request, CancellationToken ct = default)
    {
        if (!CanTeach()) return Error<AssignmentSubmissionDto>("LMS grading permission required.", 403);
        if (submissionId <= 0 || request == null || !TryVersion(request.RowVersion, out _) ||
            request.Marks < 0 || request.Marks > 100000m || request.Feedback?.Length > 2000)
            return Error<AssignmentSubmissionDto>("Invalid marks, feedback or row version.");
        var tenant = _user.TenantId;
        var row = await _submissions.GetQueryable().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == submissionId && !x.IsDeleted, ct);
        if (row == null) return Error<AssignmentSubmissionDto>("Submission not found.", 404);
        var assignment = await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == row.AssignmentId && !x.IsDeleted, ct);
        if (assignment == null) return Error<AssignmentSubmissionDto>("Assignment not found.", 404);
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == assignment.CourseId && !x.IsDeleted, ct);
        if (course == null || !await CanEditAsync(course, ct))
            return Error<AssignmentSubmissionDto>("Instructor may not grade this course.", 403);
        if (!Matches(row.RowVersion, request.RowVersion))
            return Error<AssignmentSubmissionDto>("Submission changed. Reload and retry.", 409);
        if (request.Marks > assignment.MaxMarks)
            return Error<AssignmentSubmissionDto>("Marks cannot exceed assignment maximum.", 409);
        if (row.State != LearningSubmissionState.Submitted && row.State != LearningSubmissionState.Returned)
            return Error<AssignmentSubmissionDto>("Only submitted assignments may be graded.", 409);
        row.Marks = request.Marks; row.Feedback = Trim(request.Feedback);
        row.State = LearningSubmissionState.Graded;
        row.GradedAt = _clock.GetUtcNow().UtcDateTime; row.GradedByUserId = _user.UserId;
        row.UpdatedAt = row.GradedAt; row.UpdatedBy = _user.UserId;
        _submissions.Update(row);
        try
        {
            await _uow.SaveChangesAsync(ct);
            return ApiResponse<AssignmentSubmissionDto>.SuccessResponse(
                MapSubmission(row, assignment.PublicId), "Assignment graded.");
        }
        catch (DbUpdateConcurrencyException)
        { return Error<AssignmentSubmissionDto>("Submission changed concurrently.", 409); }
    }

    public async Task<ApiResponse<LessonProgressDto>> UpdateLessonProgressAsync(
        UpdateLessonProgressRequestDto request, CancellationToken ct = default)
    {
        if (!Authenticated() || !_user.IsInRole("Student"))
            return Error<LessonProgressDto>("Student permission required.", 403);
        if (request == null || request.LessonReference == Guid.Empty ||
            request.ProgressPercent is < 0 or > 100)
            return Error<LessonProgressDto>("Invalid lesson progress.");
        var tenant = _user.TenantId;
        var lesson = await _lessons.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == request.LessonReference &&
            x.IsPublished && !x.IsDeleted, ct);
        if (lesson == null) return Error<LessonProgressDto>("Lesson not found.", 404);
        var studentId = await OwnStudentIdAsync(ct);
        if (studentId == 0) return Error<LessonProgressDto>("Student not found.", 404);
        var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.CourseId == lesson.CourseId && x.StudentId == studentId &&
            x.State == CourseEnrollmentState.Active && !x.IsDeleted, ct);
        if (enrollment == null) return Error<LessonProgressDto>("Active course enrollment not found.", 409);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var row = await _progress.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.CourseEnrollmentId == enrollment.Id &&
                    x.LessonId == lesson.Id && !x.IsDeleted, token);
                var now = _clock.GetUtcNow().UtcDateTime;
                if (row == null)
                {
                    row = new LessonProgress
                    {
                        TenantId = tenant, CourseEnrollmentId = enrollment.Id, LessonId = lesson.Id,
                        CreatedAt = now, CreatedBy = _user.UserId
                    };
                    await _progress.AddAsync(row);
                }
                else
                {
                    row.UpdatedAt = now; row.UpdatedBy = _user.UserId; _progress.Update(row);
                }
                row.IsCompleted = request.IsCompleted || row.IsCompleted;
                row.ProgressPercent = row.IsCompleted ? 100m : Math.Max(row.ProgressPercent, request.ProgressPercent);
                row.CompletedAt = row.IsCompleted ? row.CompletedAt ?? now : null;
                row.LastAccessedAt = now;
                await _uow.SaveChangesAsync(token);
                return ApiResponse<LessonProgressDto>.SuccessResponse(new LessonProgressDto
                {
                    Id = row.Id, CourseEnrollmentId = row.CourseEnrollmentId,
                    LessonReference = lesson.PublicId, LessonTitle = lesson.Title,
                    IsCompleted = row.IsCompleted, CompletedAt = row.CompletedAt,
                    ProgressPercent = row.ProgressPercent
                }, "Lesson progress saved.");
            }, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<LessonProgressDto>("Lesson progress changed concurrently.", 409); }
        catch (DbUpdateException) { return Error<LessonProgressDto>("Lesson progress conflicts with existing data.", 409); }
    }

    private async Task<Course?> EditableCourseAsync(Guid reference, CancellationToken ct)
    {
        var row = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
        return row != null && await CanEditAsync(row, ct) ? row : null;
    }

    private async Task<bool> CanEditAsync(Course course, CancellationToken ct)
    {
        if (!CanTeach()) return false;
        if (IsManager()) return true;
        var own = await OwnTeacherIdAsync(ct);
        return own > 0 && course.PrimaryInstructorEmployeeId == own;
    }

    private async Task<bool> CanViewAsync(Course course, CancellationToken ct)
    {
        if (await CanEditAsync(course, ct)) return true;
        if (!_user.IsInRole("Student") || !course.IsActive) return false;
        var id = await OwnStudentIdAsync(ct);
        return id > 0 && await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == _user.TenantId && x.CourseId == course.Id && x.StudentId == id &&
            (x.State == CourseEnrollmentState.Active || x.State == CourseEnrollmentState.Completed), ct);
    }

    private async Task<long> OwnTeacherIdAsync(CancellationToken ct) =>
        await _employees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.UserId == _user.UserId && x.CanTeach &&
            x.State == EmployeeState.Active && !x.IsDeleted).Select(x => x.Id).FirstOrDefaultAsync(ct);

    private async Task<long> OwnStudentIdAsync(CancellationToken ct) =>
        await _students.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.UserId == _user.UserId && x.StatusCode == "Active" &&
            !x.IsDeleted).Select(x => x.Id).FirstOrDefaultAsync(ct);

    private async Task<CourseDto> MapCourseAsync(Course row, CancellationToken ct)
    {
        var subjectName = row.SubjectId.HasValue ? await _subjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == row.TenantId && x.Id == row.SubjectId.Value)
            .Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var programName = row.AcademicProgramId.HasValue ? await _programs.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == row.TenantId && x.Id == row.AcademicProgramId.Value)
            .Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var instructor = row.PrimaryInstructorEmployeeId.HasValue ? await _employees.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == row.TenantId && x.Id == row.PrimaryInstructorEmployeeId.Value)
            .Select(x => new { x.PublicId, x.FullName }).FirstOrDefaultAsync(ct) : null;
        return new CourseDto
        {
            Id = row.Id, Reference = row.PublicId, AcademicProgramId = row.AcademicProgramId,
            AcademicProgramName = programName, SubjectId = row.SubjectId, SubjectName = subjectName,
            PrimaryInstructorReference = instructor?.PublicId, PrimaryInstructorName = instructor?.FullName,
            Title = row.Title, Code = row.Code, Description = row.Description, ThumbnailUrl = row.ThumbnailUrl,
            IsSelfPaced = row.IsSelfPaced, IsActive = row.IsActive,
            RowVersion = Convert.ToBase64String(row.RowVersion)
        };
    }

    private static LessonDto MapLesson(Lesson row, Guid courseReference) => new()
    {
        Id = row.Id, Reference = row.PublicId, CourseReference = courseReference,
        Title = row.Title, Content = row.Content, ContentUrl = row.ContentUrl,
        DisplayOrder = row.DisplayOrder, IsPublished = row.IsPublished,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    private static AssignmentDto MapAssignment(Assignment row, Guid courseReference) => new()
    {
        Id = row.Id, Reference = row.PublicId, CourseReference = courseReference, Type = row.Type,
        Title = row.Title, Instructions = row.Instructions, OpensAt = row.OpensAt, DueAt = row.DueAt,
        MaxMarks = row.MaxMarks, IsPublished = row.IsPublished,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    private static AssignmentSubmissionDto MapSubmission(AssignmentSubmission row, Guid reference) => new()
    {
        Id = row.Id, AssignmentReference = reference, CourseEnrollmentId = row.CourseEnrollmentId,
        SubmissionText = row.SubmissionText, FileAssetId = row.FileAssetId, State = row.State,
        SubmittedAt = row.SubmittedAt, Marks = row.Marks, Feedback = row.Feedback,
        GradedByUserId = row.GradedByUserId, GradedAt = row.GradedAt,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    private static CourseEnrollmentDto MapEnrollment(CourseEnrollment row, Course course, Student student,
        StudentEnrollment? academic) => new()
    {
        Id = row.Id, CourseReference = course.PublicId, CourseTitle = course.Title,
        StudentReference = student.PublicId, StudentName = student.FullName,
        StudentEnrollmentReference = academic?.PublicId, State = row.State, EnrolledAt = row.EnrolledAt,
        CompletedAt = row.CompletedAt, ProgressPercent = 0m,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    private static PagedResult<T> EmptyPage<T>(int page, int size) => new()
    {
        Page = page, PageSize = size, TotalCount = 0, Items = new List<T>()
    };

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryVersion(string? encoded, out byte[] decoded)
    {
        decoded = [];
        if (string.IsNullOrWhiteSpace(encoded)) return false;
        try { decoded = Convert.FromBase64String(encoded); return decoded.Length > 0; }
        catch (FormatException) { return false; }
    }

    private static bool Matches(byte[] actual, string? encoded) =>
        TryVersion(encoded, out var expected) && actual != null && expected.Length > 0 &&
        actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);

    private bool Authenticated() => _user.IsAuthenticated && _user.TenantId > 0;
    private bool IsManager() => _user.IsTenantAdmin || _user.IsInRole("Principal");
    private bool CanTeach() => Authenticated() && (IsManager() || _user.IsInRole("Teacher"));
    private static ApiResponse<T> Error<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
}
