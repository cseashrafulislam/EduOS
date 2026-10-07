using EduOS.Core.Common;
using EduOS.Core.DTOs.Portals;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Portals;

public sealed class SelfServicePortalService : ISelfServicePortalService
{
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Guardian> _guardians;
    private readonly IGenericRepository<StudentEnrollment> _studentEnrollments;
    private readonly IGenericRepository<RoutineEntry> _classRoutines;
    private readonly IGenericRepository<StudentAttendance> _attendance;
    private readonly IGenericRepository<StudentResultSummary> _results;
    private readonly IGenericRepository<StudentInvoice> _invoices;
    private readonly IGenericRepository<StudentPayment> _payments;
    private readonly IGenericRepository<StudentTransport> _transport;
    private readonly IGenericRepository<Course> _courses;
    private readonly IGenericRepository<Subject> _subjects;
    private readonly IGenericRepository<Assignment> _assignments;
    private readonly IGenericRepository<CourseEnrollment> _enrollments;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<SelfServicePortalService> _logger;

    public SelfServicePortalService(IGenericRepository<Student> students, IGenericRepository<Guardian> guardians,
        IGenericRepository<StudentEnrollment> studentEnrollments, IGenericRepository<RoutineEntry> classRoutines,
        IGenericRepository<StudentAttendance> attendance, IGenericRepository<StudentResultSummary> results,
        IGenericRepository<StudentInvoice> invoices, IGenericRepository<StudentPayment> payments,
        IGenericRepository<StudentTransport> transport, IGenericRepository<Course> courses,
        IGenericRepository<Subject> subjects, IGenericRepository<Assignment> assignments,
        IGenericRepository<CourseEnrollment> enrollments,
        ICurrentUserService currentUser, ILogger<SelfServicePortalService> logger)
    {
        _students = students; _guardians = guardians; _studentEnrollments = studentEnrollments; _classRoutines = classRoutines; _attendance = attendance; _results = results;
        _invoices = invoices; _payments = payments; _transport = transport; _courses = courses; _subjects = subjects;
        _assignments = assignments; _enrollments = enrollments; _currentUser = currentUser; _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<PortalTimetableEntryDto>>> GetTimetableAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        var student = await GetAuthorizedStudentAsync(studentReference, cancellationToken);
        if (student == null) return Denied<IReadOnlyList<PortalTimetableEntryDto>>();

        var placement = await _studentEnrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id && x.IsActive)
            .OrderByDescending(x => x.EnrollmentDate).ThenByDescending(x => x.Id)
            .Select(x => new { x.AcademicYearId, x.ClassId, x.SectionId })
            .FirstOrDefaultAsync(cancellationToken);
        var academicYearId = placement?.AcademicYearId ?? student.AcademicYearId;
        var classId = placement?.ClassId ?? student.ClassId;
        var sectionId = placement?.SectionId ?? student.SectionId;

        var rows = await _classRoutines.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.AcademicYearId == academicYearId && x.ClassId == classId && x.SectionId == sectionId)
            .Select(x => new PortalTimetableEntryDto
            {
                RoutineId = x.Id,
                DayOfWeek = x.DayOfWeek,
                StartTime = x.StartTime,
                EndTime = x.EndTime,
                SubjectId = x.SubjectId,
                SubjectName = x.Subject != null ? x.Subject.Name : string.Empty,
                TeacherName = x.Teacher != null ? x.Teacher.FullName : string.Empty,
                RoomNo = x.RoomNo
            })
            .ToListAsync(cancellationToken);

        IReadOnlyList<PortalTimetableEntryDto> ordered = rows
            .OrderBy(x => DayOrder(x.DayOfWeek)).ThenBy(x => x.StartTime).ThenBy(x => x.RoutineId)
            .ToList();
        return ApiResponse<IReadOnlyList<PortalTimetableEntryDto>>.SuccessResponse(ordered);
    }

    public async Task<ApiResponse<IReadOnlyList<PortalStudentDto>>> GetLinkedStudentsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUsePortal()) return Denied<IReadOnlyList<PortalStudentDto>>();
        try
        {
            var ids = await GetAuthorizedStudentIdsAsync(cancellationToken);
            IReadOnlyList<PortalStudentDto> data = await _students.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == _currentUser.TenantId && ids.Contains(x.Id) && x.IsActive)
                .OrderBy(x => x.FullName)
                .Select(x => new PortalStudentDto { Reference = x.PublicId, StudentCode = x.StudentCode, Roll = x.Roll, Name = x.FullName, AcademicYearId = x.AcademicYearId, ClassId = x.ClassId, SectionId = x.SectionId })
                .ToListAsync(cancellationToken);
            return ApiResponse<IReadOnlyList<PortalStudentDto>>.SuccessResponse(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Portal linked students failed for user {UserId}", _currentUser.UserId);
            return ApiResponse<IReadOnlyList<PortalStudentDto>>.ErrorResponse("Portal data could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<PortalAttendanceDto>>> GetAttendanceAsync(Guid studentReference, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var student = await GetAuthorizedStudentAsync(studentReference, cancellationToken);
        if (student == null) return Denied<IReadOnlyList<PortalAttendanceDto>>();
        if (fromDate.Date > toDate.Date || (toDate.Date - fromDate.Date).TotalDays > 370) return ApiResponse<IReadOnlyList<PortalAttendanceDto>>.ErrorResponse("Attendance date range is invalid.");
        IReadOnlyList<PortalAttendanceDto> rows = await _attendance.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id && x.Date >= fromDate.Date && x.Date < toDate.Date.AddDays(1))
            .OrderByDescending(x => x.Date)
            .Select(x => new PortalAttendanceDto { Date = x.Date, Status = x.Status, InTime = x.InTime, OutTime = x.OutTime, Remarks = x.Remarks })
            .ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<PortalAttendanceDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<PortalResultDto>>> GetResultsAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        var student = await GetAuthorizedStudentAsync(studentReference, cancellationToken);
        if (student == null) return Denied<IReadOnlyList<PortalResultDto>>();
        IReadOnlyList<PortalResultDto> rows = await _results.GetQueryable().AsNoTracking().Include(x => x.Assessment)
            .Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id && x.IsPublished)
            .OrderByDescending(x => x.PublishedAtUtc)
            .Select(x => new PortalResultDto { ExamId = x.ExamId, ExamName = x.Assessment != null ? x.Assessment.Name : string.Empty, TotalMark = x.TotalMark, TotalFullMark = x.TotalFullMark, Percentage = x.Percentage, GPA = x.TotalGPA, Grade = x.FinalGrade, Position = x.Position, IsPassed = x.IsPassed, PublishedAtUtc = x.PublishedAtUtc })
            .ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<PortalResultDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<PortalFeeLedgerDto>> GetFeesAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        var student = await GetAuthorizedStudentAsync(studentReference, cancellationToken);
        if (student == null) return Denied<PortalFeeLedgerDto>();
        var invoices = await _invoices.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id).OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToListAsync(cancellationToken);
        var payments = await _payments.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id).OrderByDescending(x => x.PaymentDate).ToListAsync(cancellationToken);
        return ApiResponse<PortalFeeLedgerDto>.SuccessResponse(new PortalFeeLedgerDto
        {
            TotalBilled = invoices.Sum(x => x.TotalAmount - (x.DiscountAmount ?? 0m) + (x.FineAmount ?? 0m)), TotalPaid = invoices.Sum(x => x.PaidAmount), TotalDue = invoices.Sum(x => x.DueAmount),
            Invoices = invoices.Select(x => new PortalInvoiceDto { Reference = x.PublicId, InvoiceNo = x.InvoiceNo, Month = x.Month, Year = x.Year, BilledAmount = x.TotalAmount - (x.DiscountAmount ?? 0m) + (x.FineAmount ?? 0m), PaidAmount = x.PaidAmount, DueAmount = x.DueAmount, Status = x.Status, DueDate = x.DueDate }).ToList(),
            Payments = payments.Select(x => new PortalPaymentDto { Reference = x.PublicId, ReceiptNo = x.ReceiptNo, Amount = x.Amount, PaymentMethod = x.PaymentMethod, PaymentDate = x.PaymentDate }).ToList()
        });
    }

    public async Task<ApiResponse<IReadOnlyList<PortalTransportDto>>> GetTransportAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        var student = await GetAuthorizedStudentAsync(studentReference, cancellationToken);
        if (student == null) return Denied<IReadOnlyList<PortalTransportDto>>();
        IReadOnlyList<PortalTransportDto> rows = await _transport.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id)
            .OrderByDescending(x => x.IsActive).ThenByDescending(x => x.StartDate)
            .Select(x => new PortalTransportDto { Reference = x.PublicId, RouteName = x.Route != null ? x.Route.Name : string.Empty, VehicleNo = x.Vehicle != null ? x.Vehicle.VehicleNo : string.Empty, PickupPoint = x.PickupPoint, DriverName = x.Vehicle != null ? x.Vehicle.DriverName : null, DriverPhone = x.Vehicle != null ? x.Vehicle.DriverPhone : null, StartDate = x.StartDate, EndDate = x.EndDate, MonthlyFare = x.MonthlyFare, IsActive = x.IsActive })
            .ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<PortalTransportDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<PortalHomeworkDto>>> GetHomeworkAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        var student = await GetAuthorizedStudentAsync(studentReference, cancellationToken);
        if (student == null) return Denied<IReadOnlyList<PortalHomeworkDto>>();

        var courseIds = await _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id && x.State == CourseEnrollmentState.Active)
            .Select(x => x.CourseId).Distinct().Take(200).ToListAsync(cancellationToken);
        if (courseIds.Count == 0) return ApiResponse<IReadOnlyList<PortalHomeworkDto>>.SuccessResponse(Array.Empty<PortalHomeworkDto>());

        var assignments = await _assignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && courseIds.Contains(x.CourseId) && x.Type == LearningTaskType.Homework && x.IsPublished)
            .OrderByDescending(x => x.OpensAt ?? x.CreatedAt).ThenByDescending(x => x.Id)
            .Take(200).ToListAsync(cancellationToken);
        var usedCourseIds = assignments.Select(x => x.CourseId).Distinct().ToArray();
        var courses = await _courses.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && usedCourseIds.Contains(x.Id) && x.IsActive)
            .Select(x => new { x.Id, x.SubjectId, x.Title }).ToListAsync(cancellationToken);
        var subjectIds = courses.Where(x => x.SubjectId.HasValue).Select(x => x.SubjectId!.Value).Distinct().ToArray();
        var subjects = await _subjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && subjectIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var courseMap = courses.ToDictionary(x => x.Id);

        IReadOnlyList<PortalHomeworkDto> rows = assignments.Where(x => courseMap.ContainsKey(x.CourseId)).Select(x =>
        {
            var course = courseMap[x.CourseId];
            var subjectId = course.SubjectId ?? 0;
            return new PortalHomeworkDto
            {
                HomeworkId = x.Id,
                SubjectId = subjectId,
                SubjectName = subjectId > 0 && subjects.TryGetValue(subjectId, out var subjectName) ? subjectName : course.Title,
                Title = x.Title,
                Description = x.Instructions,
                AssignedDate = x.OpensAt ?? x.CreatedAt,
                DueDate = x.DueAt ?? x.OpensAt ?? x.CreatedAt,
                AttachmentUrl = null
            };
        }).ToList();
        return ApiResponse<IReadOnlyList<PortalHomeworkDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<PortalAssignmentDto>>> GetAssignmentsAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        var student = await GetAuthorizedStudentAsync(studentReference, cancellationToken);
        if (student == null) return Denied<IReadOnlyList<PortalAssignmentDto>>();

        var courseIds = await _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id && x.State == CourseEnrollmentState.Active)
            .Select(x => x.CourseId).Distinct().Take(200).ToListAsync(cancellationToken);
        if (courseIds.Count == 0) return ApiResponse<IReadOnlyList<PortalAssignmentDto>>.SuccessResponse(Array.Empty<PortalAssignmentDto>());

        var assignments = await _assignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && courseIds.Contains(x.CourseId) && x.Type != LearningTaskType.Homework && x.IsPublished)
            .OrderBy(x => x.DueAt ?? DateTime.MaxValue).ThenBy(x => x.Id)
            .Take(200).ToListAsync(cancellationToken);
        var usedCourseIds = assignments.Select(x => x.CourseId).Distinct().ToArray();
        var courseTitles = await _courses.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && usedCourseIds.Contains(x.Id) && x.IsActive)
            .Select(x => new { x.Id, x.Title }).ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken);

        IReadOnlyList<PortalAssignmentDto> rows = assignments.Where(x => courseTitles.ContainsKey(x.CourseId))
            .Select(x => new PortalAssignmentDto
            {
                Reference = x.PublicId,
                CourseId = x.CourseId,
                CourseTitle = courseTitles[x.CourseId],
                Title = x.Title,
                Description = x.Instructions,
                TotalMark = decimal.ToInt32(decimal.Round(x.MaxMarks, 0, MidpointRounding.AwayFromZero)),
                DueDate = x.DueAt ?? x.OpensAt ?? x.CreatedAt,
                AttachmentUrl = null
            }).ToList();
        return ApiResponse<IReadOnlyList<PortalAssignmentDto>>.SuccessResponse(rows);
    }

    private async Task<Student?> GetAuthorizedStudentAsync(Guid reference, CancellationToken cancellationToken)
    {
        if (!CanUsePortal() || reference == Guid.Empty) return null;
        var ids = await GetAuthorizedStudentIdsAsync(cancellationToken);
        return await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.PublicId == reference && ids.Contains(x.Id) && x.IsActive, cancellationToken);
    }

    private async Task<List<long>> GetAuthorizedStudentIdsAsync(CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId; var userId = _currentUser.UserId;
        var direct = await _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && x.UserId == userId).Select(x => x.Id).ToListAsync(cancellationToken);
        var guarded = await _guardians.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && x.UserId == userId).Select(x => x.StudentId).ToListAsync(cancellationToken);
        return direct.Concat(guarded).Distinct().ToList();
    }

    private bool CanUsePortal() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (_currentUser.IsInRole("Student") || _currentUser.IsInRole("Guardian") || _currentUser.IsInRole("Parent"));
    private static int DayOrder(string? day) => day?.Trim().ToLowerInvariant() switch
    {
        "saturday" => 0,
        "sunday" => 1,
        "monday" => 2,
        "tuesday" => 3,
        "wednesday" => 4,
        "thursday" => 5,
        "friday" => 6,
        _ => 7
    };
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("The requested student is not linked to this account.", 403);
}
