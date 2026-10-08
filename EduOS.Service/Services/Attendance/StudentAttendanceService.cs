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
using System.Text;

namespace EduOS.Service.Services.Attendance;

public sealed class StudentAttendanceService : IStudentAttendanceService
{
    private readonly IGenericRepository<StudentAttendance> _attendances;
    private readonly IGenericRepository<AttendanceSession> _sessions;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<InstructorAssignment> _instructors;
    private readonly IGenericRepository<SubjectOffering> _offerings;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicLevel> _levels;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<StudentAttendanceService> _logger;

    public StudentAttendanceService(IGenericRepository<StudentAttendance> attendances,
        IGenericRepository<AttendanceSession> sessions, IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<Employee> employees, IGenericRepository<InstructorAssignment> instructors,
        IGenericRepository<SubjectOffering> offerings,
        IGenericRepository<Student> students, IGenericRepository<AcademicYear> years,
        IGenericRepository<AcademicLevel> levels, IGenericRepository<AcademicBatch> batches,
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, TimeProvider clock,
        ILogger<StudentAttendanceService> logger)
    {
        _attendances = attendances;
        _sessions = sessions;
        _enrollments = enrollments;
        _employees = employees;
        _instructors = instructors;
        _offerings = offerings;
        _students = students;
        _years = years;
        _levels = levels;
        _batches = batches;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<StudentAttendanceRosterDto>> GetRosterAsync(StudentAttendanceRosterQueryDto query, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied();
        if (query == null) return Error("Attendance query is required.");
        var error = await ValidateContextAsync(query, cancellationToken);
        if (error != null) return Error(error);
        try { return ApiResponse<StudentAttendanceRosterDto>.SuccessResponse(await BuildRosterAsync(query, cancellationToken)); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Attendance roster failed for tenant {TenantId} and batch {BatchId}", _currentUser.TenantId, query.SectionId);
            return Error("Attendance roster could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<StudentAttendanceRosterDto>> SaveAsync(SaveStudentAttendanceDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied();
        if (request == null) return Error("Attendance request is required.");
        var error = await ValidateContextAsync(request, cancellationToken);
        if (error != null) return Error(error);
        if (request.Items == null || request.Items.Count == 0 || request.Items.Any(x => x == null || x.StudentReference == Guid.Empty ||
            !new[] { "Present", "Absent", "Late", "Leave" }.Contains(x.Status?.Trim(), StringComparer.OrdinalIgnoreCase)))
            return Error("One or more attendance entries are invalid.");
        if (request.Items.Select(x => x.StudentReference).Distinct().Count() != request.Items.Count)
            return Error("Each student must appear only once.");
        if (request.Items.Any(x => x.Remarks?.Length > 500 || x.InTime < TimeSpan.Zero || x.InTime >= TimeSpan.FromDays(1) || x.OutTime < TimeSpan.Zero || x.OutTime >= TimeSpan.FromDays(1) ||
            (x.InTime.HasValue && x.OutTime.HasValue && x.OutTime < x.InTime)))
            return Error("Attendance times or remarks are invalid.");

        var tenantId = _currentUser.TenantId;
        var attendanceDate = DateOnly.FromDateTime(request.Date);
        var sessionRequestId = DailySessionKey(tenantId, request.SectionId, attendanceDate);
        var transactionStarted = false;
        try
        {
            var enrolled = await ActiveEnrollments(request, cancellationToken)
                .Join(_students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive),
                    enrollment => enrollment.StudentId, student => student.Id,
                    (enrollment, student) => new { EnrollmentId = enrollment.Id, student.PublicId })
                .ToListAsync(cancellationToken);
            var enrollmentByReference = enrolled.ToDictionary(x => x.PublicId, x => x.EnrollmentId);
            if (request.Items.Any(x => !enrollmentByReference.ContainsKey(x.StudentReference)))
                return Error("One or more students are not actively enrolled in this batch.", 409);

            await _unitOfWork.BeginTransactionAsync();
            transactionStarted = true;
            var session = await _sessions.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId &&
                x.AcademicBatchId == request.SectionId && x.AttendanceDate == attendanceDate &&
                x.SubjectOfferingId == null, cancellationToken);
            if (session != null && session.IsFinalized)
            {
                await _unitOfWork.RollbackTransactionAsync();
                transactionStarted = false;
                return Error("Finalized attendance cannot be edited.", 409);
            }
            var now = _clock.GetUtcNow().UtcDateTime;
            if (session == null)
            {
                session = new AttendanceSession
                {
                    TenantId = tenantId, ClientRequestId = sessionRequestId,
                    AcademicBatchId = request.SectionId, AttendanceDate = attendanceDate,
                    CreatedAt = now, CreatedBy = _currentUser.UserId
                };
                await _sessions.AddAsync(session);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var enrollmentIds = request.Items.Select(x => enrollmentByReference[x.StudentReference]).ToArray();
            var existingRows = await _attendances.GetQueryable().Where(x => x.TenantId == tenantId &&
                x.AttendanceSessionId == session.Id && enrollmentIds.Contains(x.StudentEnrollmentId))
                .ToDictionaryAsync(x => x.StudentEnrollmentId, cancellationToken);

            foreach (var item in request.Items)
            {
                var enrollmentId = enrollmentByReference[item.StudentReference];
                var state = Enum.Parse<AttendanceState>(item.Status, true);
                if (!existingRows.TryGetValue(enrollmentId, out var attendance))
                {
                    attendance = new StudentAttendance
                    {
                        TenantId = tenantId, AttendanceSessionId = session.Id,
                        StudentEnrollmentId = enrollmentId,
                        CreatedAt = now, CreatedBy = _currentUser.UserId
                    };
                    await _attendances.AddAsync(attendance);
                }
                attendance.State = state;
                attendance.CheckInTime = item.InTime.HasValue ? TimeOnly.FromTimeSpan(item.InTime.Value) : null;
                attendance.CheckOutTime = item.OutTime.HasValue ? TimeOnly.FromTimeSpan(item.OutTime.Value) : null;
                attendance.Remarks = string.IsNullOrWhiteSpace(item.Remarks) ? null : item.Remarks.Trim();
                attendance.RecordedByUserId = _currentUser.UserId;
                attendance.RecordedAt = now;
                attendance.UpdatedAt = now;
                attendance.UpdatedBy = _currentUser.UserId;
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync();
            transactionStarted = false;
            return ApiResponse<StudentAttendanceRosterDto>.SuccessResponse(await BuildRosterAsync(request, cancellationToken), "Attendance saved.");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent attendance update rejected for tenant {TenantId}", tenantId);
            return Error("Attendance was changed by another user. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Duplicate or invalid attendance rejected for tenant {TenantId}", tenantId);
            return Error("Attendance conflicts with an existing session or record. Reload and retry.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Attendance save failed for tenant {TenantId}", tenantId);
            return Error("Attendance could not be saved.", 500);
        }
        finally
        {
            if (transactionStarted) await _unitOfWork.RollbackTransactionAsync();
        }
    }

    private async Task<StudentAttendanceRosterDto> BuildRosterAsync(StudentAttendanceRosterQueryDto query, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var date = DateOnly.FromDateTime(query.Date);
        var enrollments = await ActiveEnrollments(query, ct)
            .Join(_students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive),
                enrollment => enrollment.StudentId, student => student.Id,
                (enrollment, student) => new { EnrollmentId = enrollment.Id, student.PublicId, student.StudentCode, student.FullName, enrollment.RollNo })
            .OrderBy(x => x.RollNo).ThenBy(x => x.FullName).ToListAsync(ct);
        var session = await _sessions.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId &&
            x.AcademicBatchId == query.SectionId && x.AttendanceDate == date && x.SubjectOfferingId == null, ct);
        var ids = enrollments.Select(x => x.EnrollmentId).ToArray();
        var records = session == null || ids.Length == 0 ? new List<StudentAttendance>() :
            await _attendances.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId &&
                x.AttendanceSessionId == session.Id && ids.Contains(x.StudentEnrollmentId)).ToListAsync(ct);
        var byEnrollment = records.ToDictionary(x => x.StudentEnrollmentId);
        var students = enrollments.Select(x =>
        {
            byEnrollment.TryGetValue(x.EnrollmentId, out var attendance);
            return new StudentAttendanceRosterItemDto
            {
                StudentReference = x.PublicId, StudentCode = x.StudentCode,
                Roll = x.RollNo, StudentName = x.FullName,
                Status = attendance?.State.ToString(),
                InTime = attendance?.CheckInTime?.ToTimeSpan(),
                OutTime = attendance?.CheckOutTime?.ToTimeSpan(),
                Remarks = attendance?.Remarks
            };
        }).ToList();
        return new StudentAttendanceRosterDto
        {
            Date = query.Date.Date, AcademicYearId = query.AcademicYearId,
            ClassId = query.ClassId, SectionId = query.SectionId,
            Students = students,
            Summary = new StudentAttendanceSummaryDto
            {
                TotalStudents = students.Count,
                Marked = students.Count(x => x.Status != null),
                Present = students.Count(x => x.Status == nameof(AttendanceState.Present)),
                Absent = students.Count(x => x.Status == nameof(AttendanceState.Absent)),
                Late = students.Count(x => x.Status == nameof(AttendanceState.Late)),
                Leave = students.Count(x => x.Status == nameof(AttendanceState.Leave)),
                Unmarked = students.Count(x => x.Status == null)
            }
        };
    }

    private IQueryable<StudentEnrollment> ActiveEnrollments(StudentAttendanceRosterQueryDto query, CancellationToken ct) =>
        _enrollments.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId &&
            x.IsActive && x.IsCurrent && x.AcademicYearId == query.AcademicYearId &&
            x.AcademicLevelId == query.ClassId && x.AcademicBatchId == query.SectionId);

    private async Task<string?> ValidateContextAsync(StudentAttendanceRosterQueryDto query, CancellationToken ct)
    {
        if (query.AcademicYearId <= 0 || query.ClassId <= 0 || query.SectionId <= 0) return "Year, level and batch are required.";
        var date = DateOnly.FromDateTime(query.Date);
        var now = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (date < new DateOnly(2000, 1, 1) || date > now) return "Attendance date is invalid.";
        var tenant = _currentUser.TenantId;
        var year = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
            x.Id == query.AcademicYearId && x.IsActive, ct);
        if (year == null) return "Academic year is unavailable.";
        if (date < year.StartDate || date > year.EndDate) return "Attendance date is outside the selected academic year.";
        var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
            x.Id == query.SectionId && x.IsActive && x.AcademicLevelId == query.ClassId &&
            x.AcademicYearId == query.AcademicYearId, ct);
        if (batch == null) return "The batch does not belong to the selected year and level.";
        if (!await _levels.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.Id == query.ClassId &&
            x.AcademicProgramId == batch.AcademicProgramId && x.IsActive, ct))
            return "Academic level is unavailable.";
        if (_currentUser.IsInRole("Teacher") && !_currentUser.IsTenantAdmin &&
            !_currentUser.IsInRole("Principal") && !_currentUser.IsInRole("VicePrincipal"))
        {
            var instructorAccess = await (from employee in _employees.GetQueryable().AsNoTracking()
                join assignment in _instructors.GetQueryable().AsNoTracking() on employee.Id equals assignment.EmployeeId
                join offering in _offerings.GetQueryable().AsNoTracking() on assignment.SubjectOfferingId equals offering.Id
                where employee.TenantId == tenant && assignment.TenantId == tenant && offering.TenantId == tenant &&
                    employee.UserId == _currentUser.UserId && employee.State == EmployeeState.Active &&
                    assignment.IsActive && offering.IsActive && offering.AcademicBatchId == batch.Id &&
                    assignment.EffectiveFrom <= date && (!assignment.EffectiveTo.HasValue || assignment.EffectiveTo >= date)
                select assignment.Id).AnyAsync(ct);
            if (!instructorAccess) return "Teacher is not assigned to the selected batch.";
        }
        return null;
    }

    private static Guid DailySessionKey(long tenantId, int batchId, DateOnly date)
    {
        var payload = Encoding.UTF8.GetBytes($"attendance-daily/v1/{tenantId}/{batchId}/{date:yyyy-MM-dd}");
        var hash = SHA256.HashData(payload);
        return new Guid(hash.AsSpan(0, 16));
    }

    private bool CanManage() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 &&
        (_currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal") || _currentUser.IsInRole("VicePrincipal") || _currentUser.IsInRole("Teacher"));
    private static ApiResponse<StudentAttendanceRosterDto> Denied() => Error("Attendance permission is required.", 403);
    private static ApiResponse<StudentAttendanceRosterDto> Error(string message, int code = 400) =>
        ApiResponse<StudentAttendanceRosterDto>.ErrorResponse(message, code);
}
