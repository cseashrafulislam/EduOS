using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Attendance;

public sealed class StudentAttendanceService : IStudentAttendanceService
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase) { "Present", "Absent", "Late", "Leave" };

    private readonly IGenericRepository<StudentAttendance> _attendances;
    private readonly IGenericRepository<Enrollment> _enrollments;
    private readonly IGenericRepository<AcademicYear> _academicYears;
    private readonly IGenericRepository<Class> _classes;
    private readonly IGenericRepository<Section> _sections;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<StudentAttendanceService> _logger;

    public StudentAttendanceService(
        IGenericRepository<StudentAttendance> attendances,
        IGenericRepository<Enrollment> enrollments,
        IGenericRepository<AcademicYear> academicYears,
        IGenericRepository<Class> classes,
        IGenericRepository<Section> sections,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        TimeProvider clock,
        ILogger<StudentAttendanceService> logger)
    {
        _attendances = attendances;
        _enrollments = enrollments;
        _academicYears = academicYears;
        _classes = classes;
        _sections = sections;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<StudentAttendanceRosterDto>> GetRosterAsync(StudentAttendanceRosterQueryDto query, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied();
        var validation = await ValidateContextAsync(query, cancellationToken);
        if (validation != null) return ApiResponse<StudentAttendanceRosterDto>.ErrorResponse(validation);

        try
        {
            var roster = await BuildRosterAsync(query, cancellationToken);
            return ApiResponse<StudentAttendanceRosterDto>.SuccessResponse(roster);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Attendance roster failed for tenant {TenantId}, class {ClassId}, section {SectionId}, date {Date}",
                _currentUser.TenantId, query.ClassId, query.SectionId, query.Date.Date);
            return ApiResponse<StudentAttendanceRosterDto>.ErrorResponse("Attendance roster could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<StudentAttendanceRosterDto>> SaveAsync(SaveStudentAttendanceDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied();
        var validation = await ValidateContextAsync(request, cancellationToken);
        if (validation != null) return ApiResponse<StudentAttendanceRosterDto>.ErrorResponse(validation);
        if (request.Items == null || request.Items.Count == 0)
            return ApiResponse<StudentAttendanceRosterDto>.ErrorResponse("At least one attendance entry is required.");
        if (request.Items.GroupBy(x => x.StudentId).Any(g => g.Count() > 1))
            return ApiResponse<StudentAttendanceRosterDto>.ErrorResponse("A student can only appear once in the attendance request.");
        if (request.Items.Any(x => x.StudentId <= 0 || !AllowedStatuses.Contains(x.Status?.Trim() ?? string.Empty)))
            return ApiResponse<StudentAttendanceRosterDto>.ErrorResponse("One or more attendance entries are invalid.");
        if (_currentUser.UserId <= 0 || _currentUser.UserId > int.MaxValue)
            return ApiResponse<StudentAttendanceRosterDto>.ErrorResponse("The current user cannot mark attendance.", 403);

        var tenantId = _currentUser.TenantId;
        var date = request.Date.Date;
        var nextDate = date.AddDays(1);

        try
        {
            var allowedStudentIds = await _enrollments.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.IsActive
                            && x.AcademicYearId == request.AcademicYearId
                            && x.ClassId == request.ClassId
                            && x.SectionId == request.SectionId
                            && x.Student != null && x.Student.IsActive)
                .Select(x => x.StudentId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var requestedStudentIds = request.Items.Select(x => x.StudentId).ToHashSet();
            if (requestedStudentIds.Except(allowedStudentIds).Any())
                return ApiResponse<StudentAttendanceRosterDto>.ErrorResponse("One or more students are not active in the selected class and section.", 409);

            var existingRows = await _attendances.GetQueryable()
                .Where(x => x.TenantId == tenantId && x.ClassId == request.ClassId && x.SectionId == request.SectionId
                            && x.Date >= date && x.Date < nextDate && requestedStudentIds.Contains(x.StudentId))
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);
            var existingByStudent = existingRows.GroupBy(x => x.StudentId).ToDictionary(g => g.Key, g => g.First());
            var now = _clock.GetUtcNow().UtcDateTime;

            foreach (var item in request.Items)
            {
                var status = NormalizeStatus(item.Status);
                var remarks = TrimToNull(item.Remarks);
                if (remarks?.Length > 500)
                    return ApiResponse<StudentAttendanceRosterDto>.ErrorResponse("Attendance remarks cannot exceed 500 characters.");
                if (item.OutTime.HasValue && item.InTime.HasValue && item.OutTime.Value < item.InTime.Value)
                    return ApiResponse<StudentAttendanceRosterDto>.ErrorResponse("Out time cannot be earlier than in time.");

                if (existingByStudent.TryGetValue(item.StudentId, out var attendance))
                {
                    attendance.Status = status;
                    attendance.InTime = item.InTime;
                    attendance.OutTime = item.OutTime;
                    attendance.Remarks = remarks;
                    attendance.MarkedBy = (int)_currentUser.UserId;
                    attendance.UpdatedAt = now;
                    attendance.UpdatedBy = _currentUser.UserId;
                }
                else
                {
                    await _attendances.AddAsync(new StudentAttendance
                    {
                        TenantId = tenantId,
                        StudentId = item.StudentId,
                        ClassId = request.ClassId,
                        SectionId = request.SectionId,
                        Date = date,
                        Status = status,
                        InTime = item.InTime,
                        OutTime = item.OutTime,
                        Remarks = remarks,
                        MarkedBy = (int)_currentUser.UserId,
                        CreatedAt = now,
                        CreatedBy = _currentUser.UserId
                    });
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var roster = await BuildRosterAsync(request, cancellationToken);
            return ApiResponse<StudentAttendanceRosterDto>.SuccessResponse(roster, "Attendance saved successfully.");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent attendance update for tenant {TenantId}, class {ClassId}, section {SectionId}, date {Date}",
                tenantId, request.ClassId, request.SectionId, date);
            return ApiResponse<StudentAttendanceRosterDto>.ErrorResponse("Attendance changed by another user. Reload and try again.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Attendance save failed for tenant {TenantId}, class {ClassId}, section {SectionId}, date {Date}",
                tenantId, request.ClassId, request.SectionId, date);
            return ApiResponse<StudentAttendanceRosterDto>.ErrorResponse("Attendance could not be saved.", 500);
        }
    }

    private async Task<StudentAttendanceRosterDto> BuildRosterAsync(StudentAttendanceRosterQueryDto query, CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        var date = query.Date.Date;
        var nextDate = date.AddDays(1);

        var enrollments = await _enrollments.GetQueryable().AsNoTracking()
            .Include(x => x.Student)
            .Where(x => x.TenantId == tenantId && x.IsActive
                        && x.AcademicYearId == query.AcademicYearId
                        && x.ClassId == query.ClassId
                        && x.SectionId == query.SectionId
                        && x.Student != null && x.Student.IsActive)
            .OrderBy(x => x.Roll)
            .ThenBy(x => x.Student!.FullName)
            .ToListAsync(cancellationToken);

        var studentIds = enrollments.Select(x => x.StudentId).Distinct().ToArray();
        var attendanceRows = studentIds.Length == 0
            ? new List<StudentAttendance>()
            : await _attendances.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.ClassId == query.ClassId && x.SectionId == query.SectionId
                            && x.Date >= date && x.Date < nextDate && studentIds.Contains(x.StudentId))
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);
        var attendanceByStudent = attendanceRows.GroupBy(x => x.StudentId).ToDictionary(g => g.Key, g => g.First());

        var students = enrollments.GroupBy(x => x.StudentId).Select(g => g.First()).Select(x =>
        {
            attendanceByStudent.TryGetValue(x.StudentId, out var a);
            return new StudentAttendanceRosterItemDto
            {
                StudentId = x.StudentId,
                StudentReference = x.Student?.PublicId ?? Guid.Empty,
                StudentCode = x.Student?.StudentCode ?? string.Empty,
                Roll = x.Roll,
                StudentName = x.Student?.FullName ?? string.Empty,
                Status = a?.Status,
                InTime = a?.InTime,
                OutTime = a?.OutTime,
                Remarks = a?.Remarks
            };
        }).ToList();

        return new StudentAttendanceRosterDto
        {
            Date = date,
            AcademicYearId = query.AcademicYearId,
            ClassId = query.ClassId,
            SectionId = query.SectionId,
            Students = students,
            Summary = BuildSummary(students)
        };
    }

    private async Task<string?> ValidateContextAsync(StudentAttendanceRosterQueryDto query, CancellationToken cancellationToken)
    {
        if (query.AcademicYearId <= 0 || query.ClassId <= 0 || query.SectionId <= 0) return "Academic year, class and section are required.";
        var date = query.Date.Date;
        var today = _clock.GetLocalNow().Date;
        if (date < new DateTime(2000, 1, 1) || date > today.AddDays(1)) return "Attendance date is invalid.";

        var tenantId = _currentUser.TenantId;
        var year = await _academicYears.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == query.AcademicYearId && x.IsActive, cancellationToken);
        if (year == null) return "Academic year is unavailable.";
        if (date < year.StartDate.Date || date > year.EndDate.Date) return "Attendance date is outside the selected academic year.";
        if (!await _classes.AnyAsync(x => x.TenantId == tenantId && x.Id == query.ClassId && x.IsActive)) return "Class is unavailable.";
        if (!await _sections.AnyAsync(x => x.TenantId == tenantId && x.Id == query.SectionId && x.ClassId == query.ClassId && x.IsActive)) return "Section is unavailable for the selected class.";
        return null;
    }

    private bool CanManage() => _currentUser.TenantId > 0 && (_currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal")
                                || _currentUser.IsInRole("VicePrincipal") || _currentUser.IsInRole("Teacher"));

    private static ApiResponse<StudentAttendanceRosterDto> Denied() => ApiResponse<StudentAttendanceRosterDto>.ErrorResponse("Attendance access is required.", 403);

    private static StudentAttendanceSummaryDto BuildSummary(IEnumerable<StudentAttendanceRosterItemDto> students)
    {
        var rows = students.ToList();
        return new StudentAttendanceSummaryDto
        {
            TotalStudents = rows.Count,
            Marked = rows.Count(x => !string.IsNullOrWhiteSpace(x.Status)),
            Present = rows.Count(x => string.Equals(x.Status, "Present", StringComparison.OrdinalIgnoreCase)),
            Absent = rows.Count(x => string.Equals(x.Status, "Absent", StringComparison.OrdinalIgnoreCase)),
            Late = rows.Count(x => string.Equals(x.Status, "Late", StringComparison.OrdinalIgnoreCase)),
            Leave = rows.Count(x => string.Equals(x.Status, "Leave", StringComparison.OrdinalIgnoreCase)),
            Unmarked = rows.Count(x => string.IsNullOrWhiteSpace(x.Status))
        };
    }

    private static string NormalizeStatus(string value) => AllowedStatuses.First(x => string.Equals(x, value.Trim(), StringComparison.OrdinalIgnoreCase));
    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
