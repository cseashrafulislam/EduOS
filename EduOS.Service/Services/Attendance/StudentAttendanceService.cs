using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.HR;
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
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<StudentAttendanceService> _logger;

    public StudentAttendanceService(IGenericRepository<StudentAttendance> attendances,
        IGenericRepository<StudentAttendanceAdjustment> adjustments,
        IGenericRepository<AttendanceSession> sessions, IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<StudentSubjectRegistration> registrations,
        IGenericRepository<Student> students, IGenericRepository<AcademicBatch> batches,
        IGenericRepository<AcademicYear> years, IGenericRepository<Employee> employees,
        IGenericRepository<InstructorAssignment> instructors, IGenericRepository<SubjectOffering> offerings,
        IUnitOfWork uow, ICurrentUserService user, TimeProvider clock,
        ILogger<StudentAttendanceService> logger)
    {
        _attendances = attendances;
        _adjustments = adjustments;
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
        _user = user;
        _clock = clock;
        _logger = logger;
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
                    where enrollment.TenantId == _user.TenantId && student.TenantId == _user.TenantId &&
                        enrollment.AcademicBatchId == session.AcademicBatchId &&
                        enrollment.State == EnrollmentState.Active && enrollment.IsCurrent &&
                        student.StatusCode == "Active" && requested.Contains(enrollment.PublicId)
                    select new { enrollment.Id, enrollment.PublicId }).ToListAsync(token);
                if (enrollments.Count != requested.Length)
                    return Error<StudentAttendanceRosterDto>("One or more enrollments are not active in this batch.", 409);
                var enrollmentIds = enrollments.Select(x => x.Id).ToArray();
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
                var now = _clock.GetUtcNow().UtcDateTime;
                foreach (var item in request.Students)
                {
                    var id = enrollments.First(x => x.PublicId == item.StudentEnrollmentReference).Id;
                    if (existing.TryGetValue(id, out var row))
                    {
                        if (!TryVersion(item.RowVersion, out var version) || !Matches(row.RowVersion, version))
                            return Error<StudentAttendanceRosterDto>("Student attendance changed. Reload and retry.", 409);
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
                        if (!string.IsNullOrWhiteSpace(item.RowVersion))
                            return Error<StudentAttendanceRosterDto>("An attendance row was removed. Reload and retry.", 409);
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
                if (!Matches(row.RowVersion, expected))
                    return Error<StudentAttendanceDto>("Attendance record changed. Reload and retry.", 409);
                var session = await _sessions.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == _user.TenantId && x.Id == row.AttendanceSessionId && !x.IsDeleted, token);
                if (session == null) return Error<StudentAttendanceDto>("Attendance session not found.", 404);
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

    private async Task<StudentAttendanceRosterDto> ReadRosterAsync(long batchId, DateOnly date, long? offeringId, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var session = await _sessions.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.AcademicBatchId == batchId && x.AttendanceDate == date &&
            x.SubjectOfferingId == offeringId && !x.IsDeleted, ct);
        var students = await (from enrollment in _enrollments.GetQueryable().AsNoTracking()
            join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
            where enrollment.TenantId == tenant && student.TenantId == tenant &&
                enrollment.AcademicBatchId == batchId && enrollment.State == EnrollmentState.Active &&
                enrollment.IsCurrent && student.StatusCode == "Active" && !student.IsDeleted
            orderby enrollment.RollNo, student.FullName
            select new { enrollment.Id, enrollment.PublicId, enrollment.RollNo,
                StudentReference = student.PublicId, student.StudentCode, student.FullName }).Take(2000).ToListAsync(ct);
        if (offeringId.HasValue && students.Count > 0)
        {
            var eligible = students.Select(x => x.Id).ToArray();
            var permitted = await _registrations.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.SubjectOfferingId == offeringId.Value &&
                x.State == SubjectRegistrationState.Approved && eligible.Contains(x.StudentEnrollmentId))
                .Select(x => x.StudentEnrollmentId).ToListAsync(ct);
            var set = permitted.ToHashSet();
            students = students.Where(x => set.Contains(x.Id)).ToList();
        }
        var ids = students.Select(x => x.Id).ToArray();
        var rows = session == null || ids.Length == 0 ? new List<StudentAttendance>() :
            await _attendances.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AttendanceSessionId == session.Id &&
                ids.Contains(x.StudentEnrollmentId) && !x.IsDeleted).ToListAsync(ct);
        var byId = rows.ToDictionary(x => x.StudentEnrollmentId);
        var dtos = students.Select(x =>
        {
            byId.TryGetValue(x.Id, out var row);
            return new StudentAttendanceDto
            {
                Id = row?.Id ?? 0, AttendanceSessionId = session?.Id ?? 0,
                StudentEnrollmentReference = x.PublicId, StudentReference = x.StudentReference,
                StudentCode = x.StudentCode, StudentName = x.FullName, RollNo = x.RollNo,
                State = row?.State ?? default, CheckInTime = row?.CheckInTime, CheckOutTime = row?.CheckOutTime,
                Remarks = row?.Remarks, RowVersion = row == null ? string.Empty : Convert.ToBase64String(row.RowVersion)
            };
        }).ToList();
        return new StudentAttendanceRosterDto
        {
            AttendanceSessionId = session?.Id ?? 0,
            SessionRowVersion = session == null ? string.Empty : Convert.ToBase64String(session.RowVersion),
            AcademicBatchId = batchId, AttendanceDate = date,
            Students = dtos, Summary = new StudentAttendanceSummaryDto
            {
                TotalStudents = dtos.Count, Marked = rows.Count,
                Present = rows.Count(x => x.State == AttendanceState.Present),
                Absent = rows.Count(x => x.State == AttendanceState.Absent),
                Late = rows.Count(x => x.State == AttendanceState.Late),
                Leave = rows.Count(x => x.State == AttendanceState.Leave),
                Unmarked = Math.Max(0, dtos.Count - rows.Count)
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
        if (batch == null) return "Academic batch is unavailable.";
        var year = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == batch.AcademicYearId && x.IsActive && !x.IsDeleted, ct);
        if (year == null || date < year.StartDate || date > year.EndDate ||
            (batch.StartDate.HasValue && date < batch.StartDate) ||
            (batch.EndDate.HasValue && date > batch.EndDate))
            return "Attendance date falls outside the academic session.";
        if (offeringId.HasValue && !await _offerings.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == offeringId && x.AcademicBatchId == batchId && x.IsActive, ct))
            return "Subject offering does not belong to this academic batch.";
        if (!_user.IsTenantAdmin && !_user.IsInRole("Principal") && !_user.IsInRole("VicePrincipal"))
        {
            if (!_user.IsInRole("Teacher")) return "Attendance permission is required.";
            var allowed = await (from employee in _employees.GetQueryable().AsNoTracking()
                join assigned in _instructors.GetQueryable().AsNoTracking() on employee.Id equals assigned.EmployeeId
                join offering in _offerings.GetQueryable().AsNoTracking() on assigned.SubjectOfferingId equals offering.Id
                where employee.TenantId == tenant && assigned.TenantId == tenant && offering.TenantId == tenant &&
                    employee.UserId == _user.UserId && employee.State == EmployeeState.Active &&
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
