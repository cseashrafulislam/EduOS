using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;

namespace EduOS.Service.Services.LMS;

/// <summary>Tenant-scoped live lessons. Course/subject-offering and instructor assignments are the authority.</summary>
public sealed class LmsLiveClassService : ILmsLiveClassService
{
    private readonly IGenericRepository<LiveClass> _classes;
    private readonly IGenericRepository<Course> _courses;
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<EmployeeCampusAssignment> _campusAssignments;
    private readonly IGenericRepository<InstructorAssignment> _assignments;
    private readonly IGenericRepository<SubjectOffering> _offerings;
    private readonly IGenericRepository<CurriculumSubject> _curriculumSubjects;
    private readonly IGenericRepository<AcademicCurriculum> _curricula;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicTerm> _terms;
    private readonly IGenericRepository<CourseEnrollment> _enrollments;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<StudentEnrollment> _studentEnrollments;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<LmsLiveClassService> _logger;

    public LmsLiveClassService(IGenericRepository<LiveClass> classes, IGenericRepository<Course> courses,
        IGenericRepository<Employee> employees, IGenericRepository<EmployeeCampusAssignment> campusAssignments,
        IGenericRepository<InstructorAssignment> assignments, IGenericRepository<SubjectOffering> offerings,
        IGenericRepository<CurriculumSubject> curriculumSubjects, IGenericRepository<AcademicCurriculum> curricula,
        IGenericRepository<AcademicBatch> batches, IGenericRepository<AcademicYear> years,
        IGenericRepository<AcademicTerm> terms, IGenericRepository<CourseEnrollment> enrollments,
        IGenericRepository<Student> students, IGenericRepository<StudentEnrollment> studentEnrollments,
        IUnitOfWork uow, ICurrentUserService user, TimeProvider clock, ILogger<LmsLiveClassService> logger)
    {
        _classes = classes; _courses = courses; _employees = employees;
        _campusAssignments = campusAssignments; _assignments = assignments; _offerings = offerings;
        _curriculumSubjects = curriculumSubjects; _curricula = curricula; _batches = batches;
        _years = years; _terms = terms; _enrollments = enrollments; _students = students;
        _studentEnrollments = studentEnrollments; _uow = uow; _user = user; _clock = clock; _logger = logger;
    }

    private bool Authenticated => _user.IsAuthenticated && _user.TenantId > 0;
    private bool Manager => Authenticated && (_user.IsTenantAdmin || _user.IsInRole("Principal"));
    private bool Author => Manager || (Authenticated && _user.IsInRole("Teacher"));
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    private static ApiResponse<T> Fail<T>(string message, int status = 400) => ApiResponse<T>.ErrorResponse(message, status);
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool ValidUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && !uri.IsLoopback &&
            string.IsNullOrEmpty(uri.UserInfo) && uri.Host.IndexOf(".local", StringComparison.OrdinalIgnoreCase) < 0;
    }
    private static bool VersionMatches(byte[] actual, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded) || actual.Length == 0) return false;
        try
        {
            var expected = Convert.FromBase64String(encoded);
            return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
    private static DateOnly DhakaDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka")));

    private async Task<long> OwnInstructorAsync(CancellationToken ct) =>
        await _employees.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
            x.UserId == _user.UserId && x.CanTeach && x.State == EmployeeState.Active && !x.IsDeleted)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);

    private async Task<bool> CanAuthorCourseAsync(Course course, CancellationToken ct) =>
        Author && (Manager || course.PrimaryInstructorEmployeeId == await OwnInstructorAsync(ct));

    /// <summary>Only assigned, active offerings in valid academic/curriculum/term windows may host live lessons.</summary>
    private async Task<long[]> EligibleBatchesAsync(Course course, long instructorId,
        DateOnly startDate, DateOnly endDate, CancellationToken ct, bool historical = false)
    {
        if (!course.AcademicProgramId.HasValue || !course.SubjectId.HasValue) return [];
        var tenant = _user.TenantId;
        var query =
            from offering in _offerings.GetQueryable().AsNoTracking()
            join item in _curriculumSubjects.GetQueryable().AsNoTracking() on offering.CurriculumSubjectId equals item.Id
            join curriculum in _curricula.GetQueryable().AsNoTracking() on item.AcademicCurriculumId equals curriculum.Id
            join batch in _batches.GetQueryable().AsNoTracking() on offering.AcademicBatchId equals batch.Id
            join year in _years.GetQueryable().AsNoTracking() on offering.AcademicYearId equals year.Id
            join assignment in _assignments.GetQueryable().AsNoTracking() on offering.Id equals assignment.SubjectOfferingId
            join campus in _campusAssignments.GetQueryable().AsNoTracking() on assignment.EmployeeId equals campus.EmployeeId
            where offering.TenantId == tenant && item.TenantId == tenant && curriculum.TenantId == tenant &&
                batch.TenantId == tenant && year.TenantId == tenant && assignment.TenantId == tenant &&
                campus.TenantId == tenant &&
                !offering.IsDeleted && !item.IsDeleted && !curriculum.IsDeleted && !batch.IsDeleted &&
                !year.IsDeleted && !assignment.IsDeleted && !campus.IsDeleted &&
                (historical || (offering.IsActive && item.IsActive && curriculum.IsActive &&
                batch.IsActive && year.IsActive && assignment.IsActive && campus.IsCurrent)) &&
                curriculum.AcademicProgramId == course.AcademicProgramId &&
                batch.AcademicProgramId == course.AcademicProgramId && item.SubjectId == course.SubjectId &&
                batch.AcademicLevelId == item.AcademicLevelId &&
                batch.AcademicYearId == offering.AcademicYearId &&
                assignment.EmployeeId == instructorId && campus.CampusId == batch.CampusId &&
                assignment.EffectiveFrom <= startDate && (!assignment.EffectiveTo.HasValue || assignment.EffectiveTo >= endDate) &&
                campus.EffectiveFrom <= startDate && (!campus.EffectiveTo.HasValue || campus.EffectiveTo >= endDate) &&
                year.StartDate <= startDate && year.EndDate >= endDate &&
                (!year.CampusId.HasValue || year.CampusId == batch.CampusId) &&
                curriculum.EffectiveFrom <= startDate &&
                (!curriculum.EffectiveTo.HasValue || curriculum.EffectiveTo >= endDate) &&
                (!batch.StartDate.HasValue || batch.StartDate <= startDate) &&
                (!batch.EndDate.HasValue || batch.EndDate >= endDate) &&
                (!offering.AcademicTermId.HasValue || _terms.GetQueryable().Any(term =>
                    term.TenantId == tenant && term.Id == offering.AcademicTermId &&
                    term.AcademicYearId == year.Id && (historical || term.IsActive) && !term.IsDeleted &&
                    term.StartDate <= startDate && term.EndDate >= endDate))
            select batch.Id;
        return await query.Distinct().ToArrayAsync(ct);
    }

    /// <summary>Historical recordings require the completed course's original academic enrollment,
    /// its campus/curriculum offering and an instructor assignment valid on the session date.
    /// A current enrollment in another batch must never grant access to historical recordings.</summary>
    private async Task<bool> CanReadAsync(Course course, LiveClass row, CancellationToken ct)
    {
        if (Manager) return true;
        if (_user.IsInRole("Teacher")) return row.InstructorEmployeeId == await OwnInstructorAsync(ct);
        if (!Authenticated || !_user.IsInRole("Student")) return false;
        var tenant = _user.TenantId;
        var studentId = await _students.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.UserId == _user.UserId &&
            x.StatusCode == "Active" && !x.IsDeleted).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (studentId <= 0) return false;
        var from = DhakaDate(row.StartsAt);
        var to = DhakaDate(row.EndsAt);
        var isRecording = row.EndsAt <= Now && !string.IsNullOrWhiteSpace(row.RecordingUrl);
        var currentBatches = course.IsActive
            ? await EligibleBatchesAsync(course, row.InstructorEmployeeId, from, to, ct) : Array.Empty<long>();
        var historicBatches = isRecording
            ? await EligibleBatchesAsync(course, row.InstructorEmployeeId, from, to, ct, historical: true) : Array.Empty<long>();
        if (currentBatches.Length == 0 && historicBatches.Length == 0) return false;
        var academic = _studentEnrollments.GetQueryable().AsNoTracking().Where(a =>
            a.TenantId == tenant && a.StudentId == studentId && !a.IsDeleted);
        return await _enrollments.GetQueryable().AsNoTracking().AnyAsync(en =>
            en.TenantId == tenant && en.CourseId == course.Id && en.StudentId == studentId &&
            !en.IsDeleted && en.EnrolledAt <= row.EndsAt &&
            ((en.State == CourseEnrollmentState.Active && course.IsActive &&
              academic.Any(a => a.IsCurrent && a.State == EnrollmentState.Active &&
                  currentBatches.Contains(a.AcademicBatchId) &&
                  (!en.StudentEnrollmentId.HasValue || en.StudentEnrollmentId == a.Id))) ||
             (en.State == CourseEnrollmentState.Completed && isRecording &&
              en.CompletedAt.HasValue && en.CompletedAt.Value >= row.EndsAt &&
              en.StudentEnrollmentId.HasValue &&
              academic.Any(a => a.Id == en.StudentEnrollmentId.Value &&
                  (a.State == EnrollmentState.Completed || a.State == EnrollmentState.Promoted ||
                   a.State == EnrollmentState.Active) &&
                  a.AcademicProgramId == course.AcademicProgramId &&
                  a.EnrollmentDate <= from && (!a.EndDate.HasValue || a.EndDate.Value >= from) &&
                  historicBatches.Contains(a.AcademicBatchId) &&
                  _batches.GetQueryable().Any(batch => batch.TenantId == tenant &&
                      batch.Id == a.AcademicBatchId && !batch.IsDeleted &&
                      batch.CampusId == a.CampusId && batch.AcademicYearId == a.AcademicYearId) &&
                  _offerings.GetQueryable().Any(offering => offering.TenantId == tenant &&
                      !offering.IsDeleted && offering.AcademicBatchId == a.AcademicBatchId &&
                      offering.AcademicYearId == a.AcademicYearId &&
                      _curriculumSubjects.GetQueryable().Any(item => item.TenantId == tenant &&
                          !item.IsDeleted && item.Id == offering.CurriculumSubjectId &&
                          item.AcademicCurriculumId == a.AcademicCurriculumId &&
                          item.SubjectId == course.SubjectId))))), ct);
    }

    private async Task<LiveClassDto> MapAsync(LiveClass row, Course course, CancellationToken ct)
    {
        var teacher = await _employees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == row.TenantId && x.Id == row.InstructorEmployeeId)
            .Select(x => new { x.PublicId, x.FullName }).FirstOrDefaultAsync(ct);
        return new LiveClassDto
        {
            Id = row.Id, Reference = row.PublicId, CourseReference = course.PublicId,
            InstructorReference = teacher?.PublicId ?? Guid.Empty,
            InstructorName = teacher?.FullName ?? "Instructor", Title = row.Title,
            StartsAt = row.StartsAt, EndsAt = row.EndsAt,
            JoinUrl = _user.IsInRole("Student") && row.EndsAt <= Now ? null : row.JoinUrl,
            RecordingUrl = row.RecordingUrl, RowVersion = Convert.ToBase64String(row.RowVersion)
        };
    }

    public async Task<ApiResponse<LiveClassDto>> SaveLiveClassAsync(Guid? liveClassReference,
        SaveLiveClassRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!Author) return Fail<LiveClassDto>("Instructor authorization required.", 403);
        if (request == null || request.CourseReference == Guid.Empty || string.IsNullOrWhiteSpace(request.Title) ||
            request.Title.Trim().Length > 200 || request.JoinUrl?.Length > 1000 ||
            request.RecordingUrl?.Length > 1000 || !ValidUrl(request.JoinUrl) || !ValidUrl(request.RecordingUrl) ||
            request.StartsAt.Kind != DateTimeKind.Utc || request.EndsAt.Kind != DateTimeKind.Utc ||
            request.EndsAt <= request.StartsAt || request.EndsAt - request.StartsAt > TimeSpan.FromHours(4) ||
            request.StartsAt < Now.AddMinutes(5) || request.StartsAt > Now.AddDays(365))
            return Fail<LiveClassDto>("Provide a future UTC session (up to 4 hours), title and HTTPS links.");
        var tenant = _user.TenantId;
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == request.CourseReference && x.IsActive && !x.IsDeleted, cancellationToken);
        if (course == null) return Fail<LiveClassDto>("Active course not found.", 404);
        if (!await CanAuthorCourseAsync(course, cancellationToken))
            return Fail<LiveClassDto>("Course instructor access denied.", 403);
        var instructorId = Manager
            ? await _employees.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.PublicId == request.InstructorReference && x.CanTeach &&
                x.State == EmployeeState.Active && !x.IsDeleted).Select(x => x.Id).FirstOrDefaultAsync(cancellationToken)
            : await OwnInstructorAsync(cancellationToken);
        if (instructorId <= 0 || (!Manager && course.PrimaryInstructorEmployeeId != instructorId))
            return Fail<LiveClassDto>("Active course instructor not found.", 403);
        var from = DhakaDate(request.StartsAt); var to = DhakaDate(request.EndsAt);
        if ((await EligibleBatchesAsync(course, instructorId, from, to, cancellationToken)).Length == 0)
            return Fail<LiveClassDto>("No active instructor assignment and curriculum offering for this campus/date.", 409);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var row = liveClassReference.HasValue
                    ? await _classes.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                        x.PublicId == liveClassReference.Value && !x.IsDeleted, token) : null;
                if (liveClassReference.HasValue && (row == null || row.CourseId != course.Id))
                    return Fail<LiveClassDto>("Live class not found.", 404);
                if (row != null && (!VersionMatches(row.RowVersion, request.RowVersion) ||
                    row.StartsAt <= Now || (!Manager && row.InstructorEmployeeId != instructorId)))
                    return Fail<LiveClassDto>("Class changed, started, or instructor access denied.", 409);
                var same = await _classes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.InstructorEmployeeId == instructorId &&
                    x.StartsAt < request.EndsAt && x.EndsAt > request.StartsAt &&
                    (row == null || x.Id != row.Id), token);
                if (same != null)
                {
                    // Exact repeat of a successful create is safe without introducing a new schema column.
                    if (row == null && same.CourseId == course.Id && same.StartsAt == request.StartsAt &&
                        same.EndsAt == request.EndsAt && same.Title == request.Title.Trim() &&
                        same.JoinUrl == Trim(request.JoinUrl) && same.RecordingUrl == Trim(request.RecordingUrl))
                        return ApiResponse<LiveClassDto>.SuccessResponse(await MapAsync(same, course, token),
                            "Live class already scheduled.");
                    return Fail<LiveClassDto>("Instructor has an overlapping live class.", 409);
                }
                // Completion evidence is immutable. An exact create replay above remains idempotent.
                if (await CourseCompletionPublicationGuard.IsLockedAsync(_enrollments, tenant, course.Id, token))
                    return Fail<LiveClassDto>(CourseCompletionPublicationGuard.Message, 409);
                var now = Now;
                if (row == null)
                {
                    row = new LiveClass { TenantId = tenant, PublicId = Guid.NewGuid(), CourseId = course.Id,
                        CreatedAt = now, CreatedBy = _user.UserId };
                    await _classes.AddAsync(row);
                }
                else { row.UpdatedAt = now; row.UpdatedBy = _user.UserId; _classes.Update(row); }
                row.InstructorEmployeeId = instructorId; row.Title = request.Title.Trim();
                row.StartsAt = request.StartsAt; row.EndsAt = request.EndsAt;
                row.JoinUrl = Trim(request.JoinUrl); row.RecordingUrl = Trim(request.RecordingUrl);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<LiveClassDto>.SuccessResponse(await MapAsync(row, course, token), "Live class saved.");
            }, IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return Fail<LiveClassDto>("Live class changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Live class scheduling conflict for tenant {TenantId}", tenant);
            return Fail<LiveClassDto>("Live class conflicts with another scheduling request.", 409);
        }
    }

    public async Task<ApiResponse<LiveClassDto>> GetLiveClassAsync(Guid liveClassReference,
        CancellationToken cancellationToken = default)
    {
        if (!Authenticated) return Fail<LiveClassDto>("Authentication required.", 403);
        var row = await _classes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == liveClassReference && !x.IsDeleted, cancellationToken);
        if (row == null) return Fail<LiveClassDto>("Live class not found.", 404);
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == row.CourseId && !x.IsDeleted, cancellationToken);
        if (course == null || !await CanReadAsync(course, row, cancellationToken))
            return Fail<LiveClassDto>("Live class not available.", 404);
        return ApiResponse<LiveClassDto>.SuccessResponse(await MapAsync(row, course, cancellationToken));
    }

    public async Task<ApiResponse<IReadOnlyList<LiveClassDto>>> GetCourseLiveClassesAsync(Guid courseReference,
        CancellationToken cancellationToken = default)
    {
        if (!Authenticated || courseReference == Guid.Empty)
            return Fail<IReadOnlyList<LiveClassDto>>("Valid course and authentication required.", 403);
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == courseReference && !x.IsDeleted, cancellationToken);
        if (course == null) return Fail<IReadOnlyList<LiveClassDto>>("Course not found.", 404);
        var rows = await _classes.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.CourseId == course.Id && !x.IsDeleted &&
            x.StartsAt >= Now.AddDays(-30) && x.StartsAt <= Now.AddDays(365))
            .OrderBy(x => x.StartsAt).ThenBy(x => x.Id).Take(200).ToListAsync(cancellationToken);
        var result = new List<LiveClassDto>();
        foreach (var row in rows)
            if (await CanReadAsync(course, row, cancellationToken))
                result.Add(await MapAsync(row, course, cancellationToken));
        return ApiResponse<IReadOnlyList<LiveClassDto>>.SuccessResponse(result);
    }

    /// <summary>Exact, tenant-scoped historical recording register. Authorization precedes
    /// pagination and counting, so unauthorized sessions never leak through page totals.</summary>
    public async Task<ApiResponse<PagedResult<LiveClassRecordingDto>>> GetCourseRecordingsAsync(
        Guid courseReference, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!Authenticated) return Fail<PagedResult<LiveClassRecordingDto>>("Authentication required.", 403);
        if (courseReference == Guid.Empty || page < 1 || page > 100000 || pageSize < 1 || pageSize > 50)
            return Fail<PagedResult<LiveClassRecordingDto>>("Invalid course or pagination parameters.");
        var tenant = _user.TenantId;
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == courseReference && !x.IsDeleted, cancellationToken);
        if (course == null) return Fail<PagedResult<LiveClassRecordingDto>>("Course not found.", 404);
        var now = Now;
        var query = _classes.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.CourseId == course.Id && !x.IsDeleted &&
            x.EndsAt <= now && x.RecordingUrl != null && x.RecordingUrl != "")
            .OrderByDescending(x => x.StartsAt).ThenByDescending(x => x.Id);
        var total = 0;
        var skip = (page - 1) * pageSize;
        var items = new List<LiveClassRecordingDto>(pageSize);
        // Chunked reads avoid materializing unbounded course history while preserving exact totals.
        for (var offset = 0; ; offset += 100)
        {
            var chunk = await query.Skip(offset).Take(100).ToListAsync(cancellationToken);
            foreach (var row in chunk)
            {
                if (!await CanReadAsync(course, row, cancellationToken)) continue;
                if (total >= skip && items.Count < pageSize)
                {
                    var mapped = await MapAsync(row, course, cancellationToken);
                    items.Add(new LiveClassRecordingDto
                    {
                        Reference = mapped.Reference, CourseReference = mapped.CourseReference,
                        Title = mapped.Title, InstructorName = mapped.InstructorName,
                        StartsAt = mapped.StartsAt, EndsAt = mapped.EndsAt,
                        RecordingUrl = mapped.RecordingUrl!
                    });
                }
                total++;
            }
            if (chunk.Count < 100) break;
        }
        return ApiResponse<PagedResult<LiveClassRecordingDto>>.SuccessResponse(
            new PagedResult<LiveClassRecordingDto>
            {
                Page = page, PageSize = pageSize, TotalCount = total, Items = items
            });
    }

    public async Task<ApiResponse<LiveClassDto>> CancelLiveClassAsync(Guid liveClassReference, string rowVersion,
        CancellationToken cancellationToken = default)
    {
        if (!Author) return Fail<LiveClassDto>("Instructor authorization required.", 403);
        var tenant = _user.TenantId;
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var row = await _classes.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.PublicId == liveClassReference, token);
                if (row == null) return Fail<LiveClassDto>("Live class not found.", 404);
                var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == row.CourseId && !x.IsDeleted, token);
                if (course == null || !await CanAuthorCourseAsync(course, token) ||
                    (!Manager && row.InstructorEmployeeId != await OwnInstructorAsync(token)))
                    return Fail<LiveClassDto>("Live class instructor access denied.", 403);
                if (row.IsDeleted)
                    return ApiResponse<LiveClassDto>.SuccessResponse(await MapAsync(row, course, token), "Already cancelled.");
                if (await CourseCompletionPublicationGuard.IsLockedAsync(_enrollments, tenant, course.Id, token))
                    return Fail<LiveClassDto>(CourseCompletionPublicationGuard.Message, 409);
                if (!VersionMatches(row.RowVersion, rowVersion) || row.StartsAt <= Now)
                    return Fail<LiveClassDto>("Class started or row version changed.", 409);
                row.IsDeleted = true; row.DeletedAt = Now; row.DeletedBy = _user.UserId;
                row.UpdatedAt = Now; row.UpdatedBy = _user.UserId;
                _classes.Update(row); await _uow.SaveChangesAsync(token);
                return ApiResponse<LiveClassDto>.SuccessResponse(await MapAsync(row, course, token), "Live class cancelled.");
            }, IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return Fail<LiveClassDto>("Live class changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Live class cancellation conflict for tenant {TenantId}", tenant);
            return Fail<LiveClassDto>("Live class cancellation conflicts with another request.", 409);
        }
    }
}
