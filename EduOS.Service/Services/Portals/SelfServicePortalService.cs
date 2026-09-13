using EduOS.Core.Common;
using EduOS.Core.DTOs.Portals;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Exams;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Students;
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
    private readonly IGenericRepository<StudentAttendance> _attendance;
    private readonly IGenericRepository<ExamResult> _results;
    private readonly IGenericRepository<StudentInvoice> _invoices;
    private readonly IGenericRepository<Payment> _payments;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<SelfServicePortalService> _logger;

    public SelfServicePortalService(IGenericRepository<Student> students, IGenericRepository<Guardian> guardians,
        IGenericRepository<StudentAttendance> attendance, IGenericRepository<ExamResult> results,
        IGenericRepository<StudentInvoice> invoices, IGenericRepository<Payment> payments,
        ICurrentUserService currentUser, ILogger<SelfServicePortalService> logger)
    {
        _students = students; _guardians = guardians; _attendance = attendance; _results = results;
        _invoices = invoices; _payments = payments; _currentUser = currentUser; _logger = logger;
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
        IReadOnlyList<PortalResultDto> rows = await _results.GetQueryable().AsNoTracking().Include(x => x.Exam)
            .Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id && x.IsPublished)
            .OrderByDescending(x => x.PublishedAtUtc)
            .Select(x => new PortalResultDto { ExamId = x.ExamId, ExamName = x.Exam != null ? x.Exam.Name : string.Empty, TotalMark = x.TotalMark, TotalFullMark = x.TotalFullMark, Percentage = x.Percentage, GPA = x.TotalGPA, Grade = x.FinalGrade, Position = x.Position, IsPassed = x.IsPassed, PublishedAtUtc = x.PublishedAtUtc })
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
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("The requested student is not linked to this account.", 403);
}
