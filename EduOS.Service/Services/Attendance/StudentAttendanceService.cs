using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.HR;
using EduOS.Persistence.Context;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Attendance;

public sealed class StudentAttendanceService : IStudentAttendanceService
{
    private readonly IGenericRepository<StudentAttendance> _attendances;
    private readonly IGenericRepository<StudentAttendanceAdjustment> _adjustments;
    private readonly IGenericRepository<StudentLeaveApplication> _leaves;
    private readonly IGenericRepository<AttendanceSession> _sessions;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<StudentSubjectRegistration> _registrations;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<InstructorAssignment> _instructors;
    private readonly IGenericRepository<SubjectOffering> _offerings;
    private readonly IUnitOfWork _uow;
    private readonly EduOSDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<StudentAttendanceService> _logger;

    public StudentAttendanceService(IGenericRepository<StudentAttendance> attendances,
        IGenericRepository<StudentAttendanceAdjustment> adjustments,
        IGenericRepository<StudentLeaveApplication> leaves,
        IGenericRepository<AttendanceSession> sessions, IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<StudentSubjectRegistration> registrations,
        IGenericRepository<Student> students, IGenericRepository<AcademicBatch> batches,
        IGenericRepository<AcademicYear> years, IGenericRepository<Employee> employees,
        IGenericRepository<InstructorAssignment> instructors, IGenericRepository<SubjectOffering> offerings,
        EduOSDbContext db, IUnitOfWork uow, ICurrentUserService user, TimeProvider clock,
        ILogger<StudentAttendanceService> logger)
    {
        _attendances = attendances;
        _adjustments = adjustments;
        _leaves = leaves;
        _sessions = sessions;
        _enrollments = enrollments;
        _registrations = registrations;
        _students = students;
        _batches = batches;
        _years = years;
        _employees = employees;
        _instructors = instructors;
        _offerings = offerings;
        _uow = uow;
        _db = db;
        _user = user;
        _clock = clock;
        _logger = logger;
    }


    /// <summary>
    /// SQL-paged attendance batch picker. Managers may select historical batches for
    /// finalized reports; active register selection additionally validates the date.
    /// Teacher choices are restricted to effective subject assignments for that date.
    /// </summary>
    public async Task<ApiResponse<PagedResult<StudentAttendanceBatchChoiceDto>>> GetBatchChoicesAsync(
        DateOnly? attendanceDate, string? search, int page = 1, int pageSize = 25,
        CancellationToken ct = default)
    {
        if (!CanWrite())
            return Error<PagedResult<StudentAttendanceBatchChoiceDto>>("Attendance permission is required.", 403);
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            search?.Length > 100 || (attendanceDate.HasValue &&
            (attendanceDate.Value < new DateOnly(2000, 1, 1) ||
             attendanceDate.Value > DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime.AddDays(1)))))
            return Error<PagedResult<StudentAttendanceBatchChoiceDto>>("Invalid batch search, date or page.");
        if (!_user.IsTenantAdmin && !_user.IsInRole("Principal") &&
            !_user.IsInRole("VicePrincipal") && !attendanceDate.HasValue)
            return Error<PagedResult<StudentAttendanceBatchChoiceDto>>("Attendance date is required.");

        var tenant = _user.TenantId;
        var batches = _batches.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted);
        if (attendanceDate.HasValue)
        {
            var date = attendanceDate.Value;
            batches = batches.Where(x => x.IsActive &&
                (!x.StartDate.HasValue || x.StartDate.Value <= date) &&
                (!x.EndDate.HasValue || x.EndDate.Value >= date));
        }
        if (!_user.IsTenantAdmin)
        {
            if (_user.IsInRole("Principal") || _user.IsInRole("VicePrincipal"))
            {
                var campuses = AccessibleManagerCampusIds();
                batches = batches.Where(x => campuses.Contains(x.CampusId));
            }
            else
            {
                var date = attendanceDate!.Value;
                var assignedBatchIds = from employee in _employees.GetQueryable().AsNoTracking()
                    join assignment in _instructors.GetQueryable().AsNoTracking()
                        on employee.Id equals assignment.EmployeeId
                    join offering in _offerings.GetQueryable().AsNoTracking()
                        on assignment.SubjectOfferingId equals offering.Id
                    where employee.TenantId == tenant && assignment.TenantId == tenant &&
                        offering.TenantId == tenant && !employee.IsDeleted &&
                        !assignment.IsDeleted && !offering.IsDeleted &&
                        employee.UserId == _user.UserId && employee.State == EmployeeState.Active &&
                        assignment.IsActive && offering.IsActive &&
                        assignment.EffectiveFrom <= date &&
                        (!assignment.EffectiveTo.HasValue || assignment.EffectiveTo.Value >= date)
                    select offering.AcademicBatchId;
                batches = batches.Where(x => assignedBatchIds.Contains(x.Id));
            }
        }

        var query = from batch in batches
            join year in _years.GetQueryable().AsNoTracking() on batch.AcademicYearId equals year.Id
            join level in _db.AcademicLevels.AsNoTracking() on batch.AcademicLevelId equals level.Id
            where year.TenantId == tenant && level.TenantId == tenant &&
                !year.IsDeleted && !level.IsDeleted &&
                (!attendanceDate.HasValue || (year.IsActive &&
                    year.StartDate <= attendanceDate.Value && year.EndDate >= attendanceDate.Value))
            select new StudentAttendanceBatchChoiceDto
            {
                Id = batch.Id, Name = batch.Name, Code = batch.Code,
                AcademicYearName = year.Name, AcademicLevelName = level.Name
            };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Name.Contains(term) || x.Code.Contains(term) ||
                x.AcademicYearName.Contains(term) || x.AcademicLevelName.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.AcademicYearName).ThenBy(x => x.AcademicLevelName)
            .ThenBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<StudentAttendanceBatchChoiceDto>>.SuccessResponse(
            new PagedResult<StudentAttendanceBatchChoiceDto>
            {
                Items = items, TotalCount = total, Page = page, PageSize = pageSize
            });
    }


    /// <summary>SQL-paged active offerings with live campus and instructor assignment scope.</summary>
    public async Task<ApiResponse<PagedResult<StudentAttendanceReportSubjectDto>>> GetSubjectChoicesAsync(
        long academicBatchId, DateOnly attendanceDate, string? search,
        int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (!CanWrite())
            return Error<PagedResult<StudentAttendanceReportSubjectDto>>("Attendance permission is required.", 403);
        if (academicBatchId <= 0 || attendanceDate == default || page < 1 ||
            pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            search?.Length > 100)
            return Error<PagedResult<StudentAttendanceReportSubjectDto>>("Invalid attendance subject search.");
        if (await ValidateScopeAsync(academicBatchId, attendanceDate, null, ct) != null)
            return Error<PagedResult<StudentAttendanceReportSubjectDto>>("Academic batch is unavailable.", 404);
        var tenant = _user.TenantId;
        var query = from offering in _offerings.GetQueryable().AsNoTracking()
            join mapping in _db.CurriculumSubjects.AsNoTracking()
                on offering.CurriculumSubjectId equals mapping.Id
            join subject in _db.Subjects.AsNoTracking() on mapping.SubjectId equals subject.Id
            where offering.TenantId == tenant && mapping.TenantId == tenant &&
                subject.TenantId == tenant && offering.AcademicBatchId == academicBatchId &&
                offering.IsActive && !offering.IsDeleted && mapping.IsActive &&
                !mapping.IsDeleted && subject.IsActive && !subject.IsDeleted
            select new StudentAttendanceReportSubjectDto
            {
                Id = offering.Id, Code = offering.Code,
                SubjectCode = subject.Code, SubjectName = subject.Name
            };
        if (!_user.IsTenantAdmin && !_user.IsInRole("Principal") && !_user.IsInRole("VicePrincipal"))
        {
            var assignedIds = from assignment in _instructors.GetQueryable().AsNoTracking()
                join employee in _employees.GetQueryable().AsNoTracking()
                    on assignment.EmployeeId equals employee.Id
                where assignment.TenantId == tenant && employee.TenantId == tenant &&
                    !assignment.IsDeleted && !employee.IsDeleted && assignment.IsActive &&
                    employee.State == EmployeeState.Active && employee.UserId == _user.UserId &&
                    assignment.EffectiveFrom <= attendanceDate &&
                    (!assignment.EffectiveTo.HasValue || assignment.EffectiveTo.Value >= attendanceDate)
                select assignment.SubjectOfferingId;
            query = query.Where(x => assignedIds.Contains(x.Id));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Code.StartsWith(term) ||
                x.SubjectCode.StartsWith(term) || x.SubjectName.StartsWith(term));
        }
        var count = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.SubjectName).ThenBy(x => x.Code)
            .ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<StudentAttendanceReportSubjectDto>>.SuccessResponse(
            new PagedResult<StudentAttendanceReportSubjectDto>
            {
                Items = items, TotalCount = count, Page = page, PageSize = pageSize
            });
    }

    private IQueryable<long> AccessibleManagerCampusIds() =>
        from access in _db.UserCampusAccesses.AsNoTracking()
        join campus in _db.Campuses.AsNoTracking() on access.CampusId equals campus.Id
        where access.TenantId == _user.TenantId && campus.TenantId == _user.TenantId &&
            access.UserId == _user.UserId && access.IsActive && !access.IsDeleted &&
            campus.IsActive && !campus.IsDeleted
        select access.CampusId;

    private async Task<bool> CanAccessBatchAsync(long batchId, CancellationToken ct)
    {
        if (_user.IsTenantAdmin) return true;
        if (!_user.IsInRole("Principal") && !_user.IsInRole("VicePrincipal")) return false;
        return await _batches.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == _user.TenantId && x.Id == batchId && !x.IsDeleted &&
            AccessibleManagerCampusIds().Contains(x.CampusId), ct);
    }

    public async Task<ApiResponse<StudentAttendanceRosterDto>> GetRosterAsync(StudentAttendanceRosterQueryDto query,
        CancellationToken ct = default)
    {
        if (!CanWrite()) return Error<StudentAttendanceRosterDto>("Attendance permission is required.", 403);
        if (query == null || query.AcademicBatchId <= 0 ||
            query.AttendanceDate == default || query.SubjectOfferingId is <= 0)
            return Error<StudentAttendanceRosterDto>("Valid batch, date and optional subject offering are required.");
        var check = await ValidateScopeAsync(query.AcademicBatchId, query.AttendanceDate, query.SubjectOfferingId, ct);
        if (check != null) return Error<StudentAttendanceRosterDto>(check, 409);
        return ApiResponse<StudentAttendanceRosterDto>.SuccessResponse(
            await ReadRosterAsync(query.AcademicBatchId, query.AttendanceDate, query.SubjectOfferingId, ct));
    }


    public async Task<ApiResponse<StudentAttendanceRosterDto>> GetRosterPageAsync(
        StudentAttendanceRosterQueryDto query, int page = 1, int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!CanWrite()) return Error<StudentAttendanceRosterDto>("Attendance permission is required.", 403);
        if (query == null || query.AcademicBatchId <= 0 || query.AttendanceDate == default ||
            query.SubjectOfferingId is <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Error<StudentAttendanceRosterDto>("Invalid attendance roster page.");
        var error = await ValidateScopeAsync(query.AcademicBatchId, query.AttendanceDate, query.SubjectOfferingId, ct);
        if (error != null) return Error<StudentAttendanceRosterDto>(error, 409);
        return ApiResponse<StudentAttendanceRosterDto>.SuccessResponse(
            await ReadRosterAsync(query.AcademicBatchId, query.AttendanceDate, query.SubjectOfferingId, ct, page, pageSize));
    }

    public async Task<ApiResponse<StudentAttendanceRosterDto>> EnsureSessionAsync(
        EnsureStudentAttendanceSessionDto request, CancellationToken ct = default)
    {
        if (!CanWrite()) return Error<StudentAttendanceRosterDto>("Attendance permission is required.", 403);
        if (request == null || request.AcademicBatchId <= 0 || request.AttendanceDate == default ||
            request.SubjectOfferingId is <= 0)
            return Error<StudentAttendanceRosterDto>("Valid batch, date and optional subject are required.");
        var scopeError = await ValidateScopeAsync(request.AcademicBatchId, request.AttendanceDate, request.SubjectOfferingId, ct);
        if (scopeError != null) return Error<StudentAttendanceRosterDto>(scopeError, 409);
        var tenant = _user.TenantId;
        // The existing unique (TenantId, ClientRequestId) index prevents two writers from
        // creating different sessions for the same scope without a shared schema migration.
        var scopeKey = FormattableString.Invariant(
            $"student-attendance:v1:{tenant}:{request.AcademicBatchId}:{request.AttendanceDate:yyyy-MM-dd}:{request.SubjectOfferingId?.ToString() ?? "all"}");
        var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(scopeKey));
        var scopeRequestId = new Guid(digest.AsSpan(0, 16));
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var existing = await _sessions.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.AcademicBatchId == request.AcademicBatchId &&
                    x.AttendanceDate == request.AttendanceDate && x.SubjectOfferingId == request.SubjectOfferingId &&
                    !x.IsDeleted).OrderBy(x => x.Id).Take(2).ToListAsync(token);
                if (existing.Count > 1)
                    return Error<StudentAttendanceRosterDto>("Multiple attendance sessions exist for this scope. Resolve before writing.", 409);
                if (existing.Count == 0)
                {
                    var now = _clock.GetUtcNow().UtcDateTime;
                    await _sessions.AddAsync(new AttendanceSession
                    {
                        TenantId = tenant, ClientRequestId = scopeRequestId,
                        AcademicBatchId = request.AcademicBatchId, AttendanceDate = request.AttendanceDate,
                        SubjectOfferingId = request.SubjectOfferingId, CreatedAt = now, CreatedBy = _user.UserId
                    });
                    await _uow.SaveChangesAsync(token);
                }
                var roster = await ReadRosterAsync(request.AcademicBatchId, request.AttendanceDate,
                    request.SubjectOfferingId, token);
                return ApiResponse<StudentAttendanceRosterDto>.SuccessResponse(roster,
                    existing.Count == 0 ? "Attendance session opened." : "Attendance session already exists.");
            }, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Attendance session collision for tenant {TenantId}", tenant);
            return Error<StudentAttendanceRosterDto>("Attendance session was opened concurrently. Reload and retry.", 409);
        }
    }

    public async Task<ApiResponse<StudentAttendanceRosterDto>> SaveAsync(SaveAttendanceRegisterRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanWrite()) return Error<StudentAttendanceRosterDto>("Attendance permission is required.", 403);
        if (request == null || request.AttendanceSessionId <= 0 ||
            !TryVersion(request.SessionRowVersion, out var sessionVersion) ||
            request.Students == null || request.Students.Count == 0 ||
            request.Students.Count > 2000 || request.Students.Any(x =>
                x == null || x.StudentEnrollmentReference == Guid.Empty || !Enum.IsDefined(x.State) ||
                x.Remarks?.Length > 500 || (x.RowVersion != null && !TryVersion(x.RowVersion, out _))) ||
            request.Students.Select(x => x.StudentEnrollmentReference).Distinct().Count() != request.Students.Count)
            return Error<StudentAttendanceRosterDto>("Invalid attendance session, row version or roster entries.");
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var session = await _sessions.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == _user.TenantId && x.Id == request.AttendanceSessionId && !x.IsDeleted, token);
                if (session == null) return Error<StudentAttendanceRosterDto>("Attendance session not found.", 404);
                if (session.IsFinalized)
                    return Error<StudentAttendanceRosterDto>("Finalized attendance requires an audited correction.", 409);
                if (!Matches(session.RowVersion, sessionVersion))
                    return Error<StudentAttendanceRosterDto>("Attendance session was changed. Reload and retry.", 409);
                var scopeError = await ValidateScopeAsync(session.AcademicBatchId, session.AttendanceDate,
                    session.SubjectOfferingId, token);
                if (scopeError != null) return Error<StudentAttendanceRosterDto>(scopeError, 409);

                var requested = request.Students.Select(x => x.StudentEnrollmentReference).ToArray();
                var enrollments = await (from enrollment in _enrollments.GetQueryable().AsNoTracking()
                    join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
                    join batch in _batches.GetQueryable().AsNoTracking() on enrollment.AcademicBatchId equals batch.Id
                    where enrollment.TenantId == _user.TenantId && student.TenantId == _user.TenantId &&
                        enrollment.AcademicBatchId == session.AcademicBatchId &&
                        enrollment.State == EnrollmentState.Active && enrollment.IsCurrent &&
                        student.StatusCode == "Active" && batch.TenantId == _user.TenantId &&
                        enrollment.CampusId == batch.CampusId && requested.Contains(enrollment.PublicId)
                    select new { enrollment.Id, enrollment.PublicId }).ToListAsync(token);
                if (enrollments.Count != requested.Length)
                    return Error<StudentAttendanceRosterDto>("One or more enrollments are not active in this batch.", 409);
                var enrollmentIds = enrollments.Select(x => x.Id).ToArray();
                if (enrollments.Select(x => x.PublicId).Distinct().Count() != requested.Length)
                    return Error<StudentAttendanceRosterDto>("Ambiguous enrollment references. Resolve before saving.", 409);
                if (session.SubjectOfferingId.HasValue)
                {
                    var permitted = await _registrations.GetQueryable().AsNoTracking().Where(x =>
                        x.TenantId == _user.TenantId && x.SubjectOfferingId == session.SubjectOfferingId.Value &&
                        x.State == SubjectRegistrationState.Approved && enrollmentIds.Contains(x.StudentEnrollmentId))
                        .Select(x => x.StudentEnrollmentId).Distinct().CountAsync(token);
                    if (permitted != enrollmentIds.Length)
                        return Error<StudentAttendanceRosterDto>("A student is not registered for this subject.", 409);
                }
                var existing = await _attendances.GetQueryable().Where(x =>
                    x.TenantId == _user.TenantId && x.AttendanceSessionId == session.Id &&
                    enrollmentIds.Contains(x.StudentEnrollmentId) && !x.IsDeleted)
                    .ToDictionaryAsync(x => x.StudentEnrollmentId, token);
                // Validate the entire page before tracking ANY changes. A stale row at the
                // end of a batch must not leave earlier rows modified in this DbContext.
                // The session rowversion remains the cross-page optimistic concurrency gate.
                var enrollmentByReference = enrollments.ToDictionary(x => x.PublicId, x => x.Id);
                foreach (var item in request.Students)
                {
                    var id = enrollmentByReference[item.StudentEnrollmentReference];
                    if (existing.TryGetValue(id, out var row))
                    {
                        if (!TryVersion(item.RowVersion, out var version) || !Matches(row.RowVersion, version))
                            return Error<StudentAttendanceRosterDto>("Student attendance changed. Reload and retry.", 409);
                    }
                    else if (!string.IsNullOrWhiteSpace(item.RowVersion))
                        return Error<StudentAttendanceRosterDto>("An attendance row was removed. Reload and retry.", 409);
                }
                var now = _clock.GetUtcNow().UtcDateTime;
                foreach (var item in request.Students)
                {
                    var id = enrollmentByReference[item.StudentEnrollmentReference];
                    if (existing.TryGetValue(id, out var row))
                    {
                        if (row.State == item.State && row.CheckInTime == item.CheckInTime &&
                            row.Remarks == Normalize(item.Remarks))
                            continue;
                        row.State = item.State;
                        row.CheckInTime = item.CheckInTime;
                        row.Remarks = Normalize(item.Remarks);
                        row.RecordedAt = now;
                        row.RecordedByUserId = _user.UserId;
                        row.UpdatedAt = now;
                        row.UpdatedBy = _user.UserId;
                        _attendances.Update(row);
                    }
                    else
                    {
                        await _attendances.AddAsync(new StudentAttendance
                        {
                            TenantId = _user.TenantId, AttendanceSessionId = session.Id, StudentEnrollmentId = id,
                            State = item.State, CheckInTime = item.CheckInTime, Remarks = Normalize(item.Remarks),
                            RecordedAt = now, RecordedByUserId = _user.UserId,
                            CreatedAt = now, CreatedBy = _user.UserId
                        });
                    }
                }
                // Touch the session to make competing roster submissions detect the shared rowversion.
                session.UpdatedAt = now;
                session.UpdatedBy = _user.UserId;
                _sessions.Update(session);
                await _uow.SaveChangesAsync(token);
                var roster = await ReadRosterAsync(session.AcademicBatchId, session.AttendanceDate,
                    session.SubjectOfferingId, token);
                return ApiResponse<StudentAttendanceRosterDto>.SuccessResponse(roster, "Attendance saved.");
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        { return Error<StudentAttendanceRosterDto>("Attendance changed concurrently. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Attendance collision for tenant {TenantId}", _user.TenantId);
            return Error<StudentAttendanceRosterDto>("Attendance conflicts with an existing record.", 409);
        }
    }

    public async Task<ApiResponse<StudentAttendanceRosterDto>> FinalizeSessionAsync(
        long sessionId, string rowVersion, CancellationToken ct = default)
    {
        if (!CanCorrect()) return Error<StudentAttendanceRosterDto>("Attendance finalization permission denied.", 403);
        if (sessionId <= 0 || !TryVersion(rowVersion, out var expected))
            return Error<StudentAttendanceRosterDto>("Attendance session and row version are required.");
        var tenant = _user.TenantId;
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var session = await _sessions.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == sessionId && !x.IsDeleted, token);
                if (session == null) return Error<StudentAttendanceRosterDto>("Attendance session not found.", 404);
                if (!await CanAccessBatchAsync(session.AcademicBatchId, token))
                    return Error<StudentAttendanceRosterDto>("Attendance session not found.", 404);
                // Finalization is idempotent: a retry after a lost HTTP response
                // must return the posted register even if SQL rowversion advanced.
                if (session.IsFinalized)
                    return ApiResponse<StudentAttendanceRosterDto>.SuccessResponse(
                        await ReadRosterAsync(session.AcademicBatchId, session.AttendanceDate,
                            session.SubjectOfferingId, token), "Attendance already finalized.");
                if (!Matches(session.RowVersion, expected))
                    return Error<StudentAttendanceRosterDto>("Attendance session changed. Reload and retry.", 409);
                var scopeError = await ValidateScopeAsync(session.AcademicBatchId, session.AttendanceDate,
                    session.SubjectOfferingId, token);
                if (scopeError != null) return Error<StudentAttendanceRosterDto>(scopeError, 409);
                var roster = await ReadRosterAsync(session.AcademicBatchId, session.AttendanceDate,
                    session.SubjectOfferingId, token);
                if (roster.Summary.TotalStudents == 0 || roster.Summary.Unmarked != 0 ||
                    roster.Summary.Marked != roster.Summary.TotalStudents)
                    return Error<StudentAttendanceRosterDto>(
                        "The complete attendance roster must be marked before finalization.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                session.IsFinalized = true; session.FinalizedAt = now;
                session.FinalizedByUserId = _user.UserId;
                session.UpdatedAt = now; session.UpdatedBy = _user.UserId;
                _sessions.Update(session);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<StudentAttendanceRosterDto>.SuccessResponse(
                    await ReadRosterAsync(session.AcademicBatchId, session.AttendanceDate,
                        session.SubjectOfferingId, token), "Attendance finalized.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        { return Error<StudentAttendanceRosterDto>("Attendance changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Attendance finalization conflict for tenant {TenantId}", tenant);
            return Error<StudentAttendanceRosterDto>("Attendance finalization conflicts with another update.", 409);
        }
    }

    public async Task<ApiResponse<StudentAttendanceDto>> CorrectAttendanceAsync(long attendanceId,
        AttendanceCorrectionRequestDto request, CancellationToken ct = default)
    {
        if (!CanCorrect()) return Error<StudentAttendanceDto>("Attendance correction permission is required.", 403);
        if (attendanceId <= 0 || request == null || !Enum.IsDefined(request.NewState) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000 ||
            !TryVersion(request.RowVersion, out var expected))
            return Error<StudentAttendanceDto>("Valid correction, reason and row version are required.");
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var row = await _attendances.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == _user.TenantId && x.Id == attendanceId && !x.IsDeleted, token);
                if (row == null) return Error<StudentAttendanceDto>("Attendance record not found.", 404);
                var session = await _sessions.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == _user.TenantId && x.Id == row.AttendanceSessionId && !x.IsDeleted, token);
                if (session == null || !await CanAccessBatchAsync(session.AcademicBatchId, token))
                    return Error<StudentAttendanceDto>("Attendance record not found.", 404);
                if (!Matches(row.RowVersion, expected))
                    return Error<StudentAttendanceDto>("Attendance record changed. Reload and retry.", 409);
                if (!session.IsFinalized)
                    return Error<StudentAttendanceDto>("Only finalized attendance can be corrected. Edit the draft register instead.", 409);
                var ownsEnrollment = await (from enrollment in _enrollments.GetQueryable().AsNoTracking()
                    join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
                    join batch in _batches.GetQueryable().AsNoTracking() on enrollment.AcademicBatchId equals batch.Id
                    where enrollment.TenantId == _user.TenantId && student.TenantId == _user.TenantId &&
                        enrollment.Id == row.StudentEnrollmentId && !enrollment.IsDeleted && !student.IsDeleted &&
                        enrollment.AcademicBatchId == session.AcademicBatchId &&
                        batch.TenantId == _user.TenantId && enrollment.CampusId == batch.CampusId
                    select enrollment.Id).AnyAsync(token);
                if (!ownsEnrollment)
                    return Error<StudentAttendanceDto>("Historical enrollment ownership mismatch.", 409);
                if (row.State == request.NewState)
                    return Error<StudentAttendanceDto>("Attendance state is already the requested value.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                await _adjustments.AddAsync(new StudentAttendanceAdjustment
                {
                    TenantId = _user.TenantId, StudentAttendanceId = row.Id,
                    PreviousState = row.State, NewState = request.NewState,
                    Reason = request.Reason.Trim(), ChangedByUserId = _user.UserId,
                    ChangedAt = now, CreatedAt = now, CreatedBy = _user.UserId
                });
                row.State = request.NewState;
                row.UpdatedAt = now;
                row.UpdatedBy = _user.UserId;
                _attendances.Update(row);
                await _uow.SaveChangesAsync(token);
                var dto = await LoadAttendanceAsync(row.Id, token);
                return dto == null
                    ? Error<StudentAttendanceDto>("Updated attendance could not be reloaded.", 500)
                    : ApiResponse<StudentAttendanceDto>.SuccessResponse(dto, "Attendance corrected with audit history.");
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        { return Error<StudentAttendanceDto>("Attendance changed concurrently. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Attendance correction conflict for tenant {TenantId}", _user.TenantId);
            return Error<StudentAttendanceDto>("Attendance correction conflicts with an existing record.", 409);
        }
    }


    /// <summary>
    /// Paginated readback of immutable correction events. This query uses the attendance's
    /// historical enrollment identity; it never substitutes the student's current enrollment.
    /// Only attendance managers can read correction reasons and actor identifiers.
    /// </summary>
    public async Task<ApiResponse<PagedResult<StudentAttendanceAdjustmentDto>>> GetCorrectionsAsync(
        long attendanceId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!CanCorrect())
            return Error<PagedResult<StudentAttendanceAdjustmentDto>>("Attendance history permission denied.", 403);
        if (attendanceId <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<StudentAttendanceAdjustmentDto>>("Valid attendance, page and page size are required.");

        var tenant = _user.TenantId;
        try
        {
            // Check the complete ownership chain, not merely a guessed attendance ID.
            var exists = await (from attendance in _attendances.GetQueryable().AsNoTracking()
                join enrollment in _enrollments.GetQueryable().AsNoTracking()
                    on attendance.StudentEnrollmentId equals enrollment.Id
                join student in _students.GetQueryable().AsNoTracking()
                    on enrollment.StudentId equals student.Id
                join session in _sessions.GetQueryable().AsNoTracking()
                    on attendance.AttendanceSessionId equals session.Id
                join batch in _batches.GetQueryable().AsNoTracking()
                    on session.AcademicBatchId equals batch.Id
                where attendance.TenantId == tenant && enrollment.TenantId == tenant &&
                    student.TenantId == tenant && session.TenantId == tenant && batch.TenantId == tenant &&
                    attendance.Id == attendanceId && !attendance.IsDeleted &&
                    !enrollment.IsDeleted && !student.IsDeleted && !session.IsDeleted &&
                    enrollment.AcademicBatchId == session.AcademicBatchId &&
                    enrollment.CampusId == batch.CampusId
                select session.AcademicBatchId).FirstOrDefaultAsync(ct);
            if (exists <= 0 || !await CanAccessBatchAsync(exists, ct))
                return Error<PagedResult<StudentAttendanceAdjustmentDto>>("Attendance record not found.", 404);

            var query = _adjustments.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.StudentAttendanceId == attendanceId && !x.IsDeleted);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(x => x.ChangedAt).ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new StudentAttendanceAdjustmentDto
                {
                    Id = x.Id, StudentAttendanceId = x.StudentAttendanceId,
                    PreviousState = x.PreviousState, NewState = x.NewState,
                    Reason = x.Reason, ChangedByUserId = x.ChangedByUserId, ChangedAt = x.ChangedAt
                }).ToListAsync(ct);
            return ApiResponse<PagedResult<StudentAttendanceAdjustmentDto>>.SuccessResponse(
                new PagedResult<StudentAttendanceAdjustmentDto>
                {
                    Items = items, TotalCount = total, Page = page, PageSize = pageSize
                });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Attendance correction history read failed for tenant {TenantId}", tenant);
            return Error<PagedResult<StudentAttendanceAdjustmentDto>>("Correction history could not be loaded.", 500);
        }
    }


    /// <summary>
    /// Manager-only, SQL-paged audit across finalized sessions in a historical batch.
    /// Retains the original enrollment and student identity, even after promotion.
    /// The campus grant is part of the SQL query, not merely a UI filter.
    /// </summary>
    public async Task<ApiResponse<PagedResult<StudentAttendanceCorrectionReportRowDto>>> GetCorrectionReportAsync(
        StudentAttendanceCorrectionReportQueryDto query, int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!CanCorrect())
            return Error<PagedResult<StudentAttendanceCorrectionReportRowDto>>("Attendance audit permission denied.", 403);
        if (query == null || query.AcademicBatchId <= 0 ||
            query.FromDate < new DateOnly(2000, 1, 1) ||
            query.ToDate < query.FromDate ||
            query.ToDate > DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime.AddDays(1)) ||
            query.ToDate.DayNumber - query.FromDate.DayNumber > 30 ||
            query.SubjectOfferingId is <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<StudentAttendanceCorrectionReportRowDto>>(
                "Valid batch, attendance dates (up to 31 days), subject and page are required.");

        var tenant = _user.TenantId;
        var isAdmin = _user.IsTenantAdmin;
        var campuses = AccessibleManagerCampusIds();
        try
        {
            var batchExists = await _batches.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == query.AcademicBatchId && !x.IsDeleted &&
                (isAdmin || campuses.Contains(x.CampusId)), ct);
            if (!batchExists)
                return Error<PagedResult<StudentAttendanceCorrectionReportRowDto>>(
                    "Academic batch not found.", 404);

            if (query.SubjectOfferingId.HasValue)
            {
                var offeringExists = await _offerings.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == query.SubjectOfferingId.Value &&
                    x.AcademicBatchId == query.AcademicBatchId && !x.IsDeleted, ct);
                if (!offeringExists)
                    return Error<PagedResult<StudentAttendanceCorrectionReportRowDto>>(
                        "Subject offering not found in academic batch.", 404);
            }

            var rows = from adjustment in _adjustments.GetQueryable().AsNoTracking()
                join attendance in _attendances.GetQueryable().AsNoTracking()
                    on adjustment.StudentAttendanceId equals attendance.Id
                join session in _sessions.GetQueryable().AsNoTracking()
                    on attendance.AttendanceSessionId equals session.Id
                join enrollment in _enrollments.GetQueryable().AsNoTracking()
                    on attendance.StudentEnrollmentId equals enrollment.Id
                join student in _students.GetQueryable().AsNoTracking()
                    on enrollment.StudentId equals student.Id
                join batch in _batches.GetQueryable().AsNoTracking()
                    on session.AcademicBatchId equals batch.Id
                where adjustment.TenantId == tenant && attendance.TenantId == tenant &&
                    session.TenantId == tenant && enrollment.TenantId == tenant &&
                    student.TenantId == tenant && batch.TenantId == tenant &&
                    !adjustment.IsDeleted && !attendance.IsDeleted && !session.IsDeleted &&
                    !enrollment.IsDeleted && !student.IsDeleted && !batch.IsDeleted &&
                    session.IsFinalized && session.AcademicBatchId == query.AcademicBatchId &&
                    session.AttendanceDate >= query.FromDate && session.AttendanceDate <= query.ToDate &&
                    enrollment.AcademicBatchId == session.AcademicBatchId &&
                    enrollment.CampusId == batch.CampusId &&
                    (isAdmin || campuses.Contains(batch.CampusId))
                select new StudentAttendanceCorrectionReportRowDto
                {
                    AdjustmentId = adjustment.Id, StudentAttendanceId = attendance.Id,
                    AttendanceDate = session.AttendanceDate, RollNo = enrollment.RollNo,
                    StudentCode = student.StudentCode, StudentName = student.FullName,
                    SubjectOfferingId = session.SubjectOfferingId,
                    PreviousState = adjustment.PreviousState, NewState = adjustment.NewState,
                    Reason = adjustment.Reason, ChangedByUserId = adjustment.ChangedByUserId,
                    ChangedAt = adjustment.ChangedAt
                };
            if (query.SubjectOfferingId.HasValue)
            {
                var offeringId = query.SubjectOfferingId.Value;
                rows = rows.Where(x => x.SubjectOfferingId == offeringId);
            }
            var total = await rows.CountAsync(ct);
            var items = await rows.OrderByDescending(x => x.ChangedAt)
                .ThenByDescending(x => x.AdjustmentId)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return ApiResponse<PagedResult<StudentAttendanceCorrectionReportRowDto>>.SuccessResponse(
                new PagedResult<StudentAttendanceCorrectionReportRowDto>
                {
                    Items = items, TotalCount = total, Page = page, PageSize = pageSize
                });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Attendance correction report failed for tenant {TenantId}", tenant);
            return Error<PagedResult<StudentAttendanceCorrectionReportRowDto>>(
                "Attendance correction report could not be loaded.", 500);
        }
    }

    private async Task<StudentAttendanceRosterDto> ReadRosterAsync(
        long batchId, DateOnly date, long? offeringId, CancellationToken ct,
        int page = 1, int pageSize = 2000)
    {
        var tenant = _user.TenantId;
        var session = await _sessions.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.AcademicBatchId == batchId && x.AttendanceDate == date &&
            x.SubjectOfferingId == offeringId && !x.IsDeleted, ct);
        var eligible = from enrollment in _enrollments.GetQueryable().AsNoTracking()
            join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
            join batch in _batches.GetQueryable().AsNoTracking() on enrollment.AcademicBatchId equals batch.Id
            where enrollment.TenantId == tenant && student.TenantId == tenant &&
                batch.TenantId == tenant && enrollment.AcademicBatchId == batchId &&
                enrollment.State == EnrollmentState.Active && enrollment.IsCurrent &&
                student.StatusCode == "Active" && !enrollment.IsDeleted &&
                !student.IsDeleted && !batch.IsDeleted && enrollment.CampusId == batch.CampusId
            select new { enrollment.Id, enrollment.PublicId, enrollment.RollNo,
                StudentReference = student.PublicId, student.StudentCode, student.FullName };
        if (offeringId.HasValue)
        {
            var subjectId = offeringId.Value;
            eligible = eligible.Where(x => _registrations.GetQueryable().AsNoTracking().Any(r =>
                r.TenantId == tenant && r.SubjectOfferingId == subjectId &&
                r.StudentEnrollmentId == x.Id && r.State == SubjectRegistrationState.Approved &&
                !r.IsDeleted));
        }
        // Eligibility is evaluated in SQL before paging; the aggregate covers ALL eligible students.
        var total = await eligible.CountAsync(ct);
        var students = await eligible.OrderBy(x => x.RollNo).ThenBy(x => x.FullName).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = students.Select(x => x.Id).ToArray();
        var sessionId = session?.Id ?? 0;
        var eligibleIds = eligible.Select(x => x.Id);
        var counts = await _attendances.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AttendanceSessionId == sessionId &&
            sessionId > 0 && !x.IsDeleted && eligibleIds.Contains(x.StudentEnrollmentId))
            .GroupBy(x => x.State)
            .Select(g => new { State = g.Key, Count = g.Count() }).ToListAsync(ct);
        var marked = counts.Sum(x => x.Count);
        var rows = sessionId == 0 || ids.Length == 0 ? new List<StudentAttendance>() :
            await _attendances.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AttendanceSessionId == sessionId &&
                ids.Contains(x.StudentEnrollmentId) && !x.IsDeleted).ToListAsync(ct);
        var approved = ids.Length == 0 ? new HashSet<long>() :
            (await _leaves.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && ids.Contains(x.StudentEnrollmentId) &&
                x.State == LeaveState.Approved && !x.IsDeleted &&
                x.FromDate <= date && x.ToDate >= date)
                .Select(x => x.StudentEnrollmentId).Distinct().ToListAsync(ct)).ToHashSet();
        var byId = rows.ToDictionary(x => x.StudentEnrollmentId);
        var dtos = students.Select(x =>
        {
            byId.TryGetValue(x.Id, out var row);
            return new StudentAttendanceDto
            {
                Id = row?.Id ?? 0, AttendanceSessionId = sessionId,
                HasApprovedLeave = approved.Contains(x.Id),
                StudentEnrollmentReference = x.PublicId, StudentReference = x.StudentReference,
                StudentCode = x.StudentCode, StudentName = x.FullName, RollNo = x.RollNo,
                State = row?.State ?? default, CheckInTime = row?.CheckInTime, CheckOutTime = row?.CheckOutTime,
                Remarks = row?.Remarks, RowVersion = row == null ? string.Empty : Convert.ToBase64String(row.RowVersion)
            };
        }).ToList();
        int countState(AttendanceState state) => counts.Where(x => x.State == state).Sum(x => x.Count);
        return new StudentAttendanceRosterDto
        {
            Page = page, PageSize = pageSize,
            TotalPages = total == 0 ? 0 : (total - 1) / pageSize + 1,
            AttendanceSessionId = sessionId, IsFinalized = session?.IsFinalized ?? false,
            SessionRowVersion = session == null ? string.Empty : Convert.ToBase64String(session.RowVersion),
            AcademicBatchId = batchId, AttendanceDate = date,
            Students = dtos, Summary = new StudentAttendanceSummaryDto
            {
                TotalStudents = total, Marked = marked,
                Present = countState(AttendanceState.Present),
                Absent = countState(AttendanceState.Absent),
                Late = countState(AttendanceState.Late),
                Leave = countState(AttendanceState.Leave),
                Excused = countState(AttendanceState.Excused),
                Unmarked = Math.Max(0, total - marked)
            }
        };
    }

    private async Task<StudentAttendanceDto?> LoadAttendanceAsync(long id, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var result = await (from attendance in _attendances.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking() on attendance.StudentEnrollmentId equals enrollment.Id
            join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
            where attendance.TenantId == tenant && enrollment.TenantId == tenant && student.TenantId == tenant &&
                attendance.Id == id && !attendance.IsDeleted
            select new { attendance, enrollment, student }).FirstOrDefaultAsync(ct);
        if (result == null) return null;
        return new StudentAttendanceDto
        {
            Id = result.attendance.Id, AttendanceSessionId = result.attendance.AttendanceSessionId,
            StudentEnrollmentReference = result.enrollment.PublicId, StudentReference = result.student.PublicId,
            StudentCode = result.student.StudentCode, StudentName = result.student.FullName,
            RollNo = result.enrollment.RollNo, State = result.attendance.State,
            CheckInTime = result.attendance.CheckInTime, CheckOutTime = result.attendance.CheckOutTime,
            Remarks = result.attendance.Remarks, RowVersion = Convert.ToBase64String(result.attendance.RowVersion)
        };
    }

    private async Task<string?> ValidateScopeAsync(long batchId, DateOnly date, long? offeringId, CancellationToken ct)
    {
        if (batchId <= 0 || date < new DateOnly(2000, 1, 1) ||
            date > DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime.AddDays(1)))
            return "Attendance date or batch is invalid.";
        var tenant = _user.TenantId;
        var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == batchId && x.IsActive && !x.IsDeleted, ct);
        if (batch == null || ((_user.IsInRole("Principal") || _user.IsInRole("VicePrincipal")) &&
            !await CanAccessBatchAsync(batchId, ct))) return "Academic batch is unavailable.";
        var year = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == batch.AcademicYearId && x.IsActive && !x.IsDeleted, ct);
        if (year == null || date < year.StartDate || date > year.EndDate ||
            (batch.StartDate.HasValue && date < batch.StartDate) ||
            (batch.EndDate.HasValue && date > batch.EndDate))
            return "Attendance date falls outside the academic session.";
        if (offeringId.HasValue && !await _offerings.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == offeringId && x.AcademicBatchId == batchId && x.IsActive && !x.IsDeleted, ct))
            return "Subject offering does not belong to this academic batch.";
        if (!_user.IsTenantAdmin && !_user.IsInRole("Principal") && !_user.IsInRole("VicePrincipal"))
        {
            if (!_user.IsInRole("Teacher")) return "Attendance permission is required.";
            var allowed = await (from employee in _employees.GetQueryable().AsNoTracking()
                join assigned in _instructors.GetQueryable().AsNoTracking() on employee.Id equals assigned.EmployeeId
                join offering in _offerings.GetQueryable().AsNoTracking() on assigned.SubjectOfferingId equals offering.Id
                where employee.TenantId == tenant && assigned.TenantId == tenant && offering.TenantId == tenant &&
                    employee.UserId == _user.UserId && employee.State == EmployeeState.Active &&
                    !employee.IsDeleted && !assigned.IsDeleted && !offering.IsDeleted &&
                    assigned.IsActive && offering.IsActive && offering.AcademicBatchId == batchId &&
                    (!offeringId.HasValue || offering.Id == offeringId.Value) &&
                    assigned.EffectiveFrom <= date && (!assigned.EffectiveTo.HasValue || assigned.EffectiveTo >= date)
                select assigned.Id).AnyAsync(ct);
            if (!allowed) return "Teacher is not assigned to the requested academic batch and subject.";
        }
        return null;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool TryVersion(string? text, out byte[] version)
    {
        version = [];
        if (string.IsNullOrWhiteSpace(text)) return false;
        try { version = Convert.FromBase64String(text); return version.Length > 0; }
        catch (FormatException) { return false; }
    }
    private static bool Matches(byte[] actual, byte[] expected) =>
        actual != null && actual.Length == expected.Length && actual.Length > 0 &&
        CryptographicOperations.FixedTimeEquals(actual, expected);
    private bool CanWrite() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("VicePrincipal") ||
         _user.IsInRole("Teacher"));
    private bool CanCorrect() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("VicePrincipal"));
    private static ApiResponse<T> Error<T>(string message, int status = 400) => ApiResponse<T>.ErrorResponse(message, status);
}
