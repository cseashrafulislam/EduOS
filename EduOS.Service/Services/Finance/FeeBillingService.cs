using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Finance;

public sealed class FeeBillingService : IFeeBillingService
{
    private readonly IGenericRepository<FeeStructure> _structures;
    private readonly IGenericRepository<FeeHead> _heads;
    private readonly IGenericRepository<StudentDiscount> _studentDiscounts;
    private readonly IGenericRepository<StudentInvoice> _invoices;
    private readonly IGenericRepository<Payment> _payments;
    private readonly IGenericRepository<BankAccount> _bankAccounts;
    private readonly IGenericRepository<Enrollment> _enrollments;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<Class> _classes;
    private readonly IGenericRepository<Section> _sections;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<FeeBillingService> _logger;

    public FeeBillingService(IGenericRepository<FeeStructure> structures, IGenericRepository<FeeHead> heads,
        IGenericRepository<StudentDiscount> studentDiscounts, IGenericRepository<StudentInvoice> invoices,
        IGenericRepository<Payment> payments, IGenericRepository<BankAccount> bankAccounts,
        IGenericRepository<Enrollment> enrollments, IGenericRepository<Student> students,
        IGenericRepository<AcademicYear> years, IGenericRepository<Class> classes, IGenericRepository<Section> sections,
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, TimeProvider clock, ILogger<FeeBillingService> logger)
    {
        _structures = structures; _heads = heads; _studentDiscounts = studentDiscounts; _invoices = invoices;
        _payments = payments; _bankAccounts = bankAccounts; _enrollments = enrollments; _students = students;
        _years = years; _classes = classes; _sections = sections; _unitOfWork = unitOfWork; _currentUser = currentUser;
        _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<bool>> SaveFeeStructureAsync(SaveFeeStructureDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<bool>.ErrorResponse("Finance access is required.", 403);
        var tenantId = _currentUser.TenantId;
        if (!await _years.AnyAsync(x => x.TenantId == tenantId && x.Id == request.AcademicYearId && x.IsActive)
            || !await _classes.AnyAsync(x => x.TenantId == tenantId && x.Id == request.ClassId && x.IsActive)
            || !await _heads.AnyAsync(x => x.TenantId == tenantId && x.Id == request.FeeHeadId && x.IsActive))
            return ApiResponse<bool>.ErrorResponse("Fee structure references are unavailable.", 409);
        var row = await _structures.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.AcademicYearId == request.AcademicYearId && x.ClassId == request.ClassId && x.FeeHeadId == request.FeeHeadId, cancellationToken);
        if (row == null) { row = new FeeStructure { TenantId = tenantId, AcademicYearId = request.AcademicYearId, ClassId = request.ClassId, FeeHeadId = request.FeeHeadId, CreatedBy = _currentUser.UserId }; await _structures.AddAsync(row); }
        row.Amount = request.Amount; row.UpdatedAt = _clock.GetUtcNow().UtcDateTime; row.UpdatedBy = _currentUser.UserId;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ApiResponse<bool>.SuccessResponse(true, "Fee structure saved.");
    }

    public async Task<ApiResponse<InvoiceBatchResultDto>> GenerateInvoicesAsync(GenerateStudentInvoicesDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<InvoiceBatchResultDto>.ErrorResponse("Finance access is required.", 403);
        if (request.ClientRequestId == Guid.Empty) return ApiResponse<InvoiceBatchResultDto>.ErrorResponse("Client request reference is required.");
        var tenantId = _currentUser.TenantId;
        var contextError = await ValidateScopeAsync(request, cancellationToken);
        if (contextError != null) return ApiResponse<InvoiceBatchResultDto>.ErrorResponse(contextError, 409);
        var month = request.Month.ToString("D2");
        var structures = await _structures.GetQueryable().AsNoTracking().Include(x => x.FeeHead).Where(x => x.TenantId == tenantId && x.AcademicYearId == request.AcademicYearId && x.ClassId == request.ClassId && x.Amount >= 0 && x.FeeHead != null && x.FeeHead.IsActive).OrderBy(x => x.FeeHead!.Name).ToListAsync(cancellationToken);
        if (structures.Count == 0) return ApiResponse<InvoiceBatchResultDto>.ErrorResponse("No active fee structure exists for the selected year and class.", 409);
        var enrollments = await _enrollments.GetQueryable().AsNoTracking().Include(x => x.Student).Where(x => x.TenantId == tenantId && x.IsActive && x.AcademicYearId == request.AcademicYearId && x.ClassId == request.ClassId && x.SectionId == request.SectionId && x.Student != null && x.Student.IsActive).OrderBy(x => x.Roll).ToListAsync(cancellationToken);
        var uniqueEnrollments = enrollments.GroupBy(x => x.StudentId).Select(x => x.First()).ToList();
        if (uniqueEnrollments.Count == 0) return ApiResponse<InvoiceBatchResultDto>.ErrorResponse("No active students were found.", 409);
        var ids = uniqueEnrollments.Select(x => x.StudentId).ToArray();
        var previousRequest = await _invoices.GetQueryable().AsNoTracking().Include(x => x.Student).Where(x => x.TenantId == tenantId && x.GenerationRequestId == request.ClientRequestId && ids.Contains(x.StudentId)).OrderBy(x => x.Student!.Roll).ToListAsync(cancellationToken);
        if (previousRequest.Count > 0)
        {
            if (previousRequest.Any(x => x.AcademicYearId != request.AcademicYearId || x.ClassId != request.ClassId || x.SectionId != request.SectionId || x.Month != month || x.Year != request.Year)) return ApiResponse<InvoiceBatchResultDto>.ErrorResponse("Client request reference was already used for a different billing scope.", 409);
            return ApiResponse<InvoiceBatchResultDto>.SuccessResponse(new InvoiceBatchResultDto { ClientRequestId = request.ClientRequestId, Existing = previousRequest.Count, Invoices = previousRequest.Select(MapInvoice).ToList() }, "Billing request was already processed.");
        }
        try
        {
            var existing = await _invoices.GetQueryable().Include(x => x.Student).Where(x => x.TenantId == tenantId && ids.Contains(x.StudentId) && x.Year == request.Year && x.Month == month).ToListAsync(cancellationToken);
            var existingByStudent = existing.GroupBy(x => x.StudentId).ToDictionary(x => x.Key, x => x.First());
            var discounts = await _studentDiscounts.GetQueryable().AsNoTracking().Include(x => x.Discount).Where(x => x.TenantId == tenantId && ids.Contains(x.StudentId) && x.StartDate.Date <= request.DueDate.Date && (!x.EndDate.HasValue || x.EndDate.Value.Date >= request.DueDate.Date) && x.Discount != null && x.Discount.IsActive).ToListAsync(cancellationToken);
            var discountsByStudent = discounts.GroupBy(x => x.StudentId).ToDictionary(x => x.Key, x => x.ToList());
            var result = new InvoiceBatchResultDto { ClientRequestId = request.ClientRequestId };
            var now = _clock.GetUtcNow().UtcDateTime;
            foreach (var enrollment in uniqueEnrollments)
            {
                if (existingByStudent.TryGetValue(enrollment.StudentId, out var old)) { result.Existing++; result.Invoices.Add(MapInvoice(old)); continue; }
                var publicId = Guid.NewGuid();
                var total = structures.Sum(x => x.Amount);
                discountsByStudent.TryGetValue(enrollment.StudentId, out var assigned);
                var discountAmount = CalculateDiscount(structures, assigned ?? new List<StudentDiscount>());
                var invoice = new StudentInvoice
                {
                    TenantId = tenantId, PublicId = publicId, GenerationRequestId = request.ClientRequestId,
                    BillingKey = $"{request.Year:D4}-{request.Month:D2}-{enrollment.StudentId}", StudentId = enrollment.StudentId,
                    Student = enrollment.Student, AcademicYearId = request.AcademicYearId, ClassId = request.ClassId, SectionId = request.SectionId,
                    InvoiceNo = $"INV-{request.Year:D4}{request.Month:D2}-{publicId:N}"[..21].ToUpperInvariant(), Month = month, Year = request.Year,
                    TotalAmount = total, DiscountAmount = discountAmount, FineAmount = 0m, PaidAmount = 0m,
                    DueAmount = Math.Max(0m, total - discountAmount), Status = total - discountAmount <= 0 ? "Paid" : "Unpaid",
                    DueDate = request.DueDate.Date, CreatedDate = now, CreatedAt = now, CreatedBy = _currentUser.UserId
                };
                foreach (var s in structures) invoice.Items.Add(new InvoiceItem { FeeHeadId = s.FeeHeadId, FeeHead = s.FeeHead, Description = s.FeeHead?.Name ?? "Fee", Amount = s.Amount, CreatedAt = now, CreatedBy = _currentUser.UserId });
                await _invoices.AddAsync(invoice); result.Generated++; result.Invoices.Add(MapInvoice(invoice));
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var refs = result.Invoices.Select(x => x.Reference).ToArray();
            var saved = await _invoices.GetQueryable().AsNoTracking().Include(x => x.Student).Where(x => x.TenantId == tenantId && refs.Contains(x.PublicId)).ToListAsync(cancellationToken);
            result.Invoices = saved.OrderBy(x => x.Student!.Roll).Select(MapInvoice).ToList();
            return ApiResponse<InvoiceBatchResultDto>.SuccessResponse(result, "Student invoices generated.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Invoice generation conflict for tenant {TenantId}, request {RequestId}", tenantId, request.ClientRequestId);
            return ApiResponse<InvoiceBatchResultDto>.ErrorResponse("Billing conflicts with an existing invoice. Reload and try again.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice generation failed for tenant {TenantId}", tenantId);
            return ApiResponse<InvoiceBatchResultDto>.ErrorResponse("Invoices could not be generated.", 500);
        }
    }

    public async Task<ApiResponse<StudentPaymentDto>> CollectPaymentAsync(CollectStudentPaymentDto request, CancellationToken cancellationToken = default)
    {
        if (!CanCollect()) return ApiResponse<StudentPaymentDto>.ErrorResponse("Fee collection access is required.", 403);
        if (request.ClientRequestId == Guid.Empty || request.InvoiceReference == Guid.Empty || !TryVersion(request.InvoiceRowVersion, out var version)) return ApiResponse<StudentPaymentDto>.ErrorResponse("Payment request is invalid.");
        var method = request.PaymentMethod.Trim();
        if (!string.Equals(method, "Cash", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(request.TransactionId)) return ApiResponse<StudentPaymentDto>.ErrorResponse("Transaction reference is required for non-cash payment.");
        var tenantId = _currentUser.TenantId;
        var existingPayment = await _payments.GetQueryable().AsNoTracking().Include(x => x.Invoice).ThenInclude(x => x!.Student).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
        if (existingPayment != null)
        {
            if (existingPayment.Invoice?.PublicId != request.InvoiceReference || existingPayment.Amount != request.Amount) return ApiResponse<StudentPaymentDto>.ErrorResponse("Client request reference was already used for a different payment.", 409);
            return ApiResponse<StudentPaymentDto>.SuccessResponse(MapPayment(existingPayment), "Payment was already received.");
        }
        try
        {
            await _unitOfWork.BeginTransactionAsync();
            var invoice = await _invoices.GetQueryable().Include(x => x.Student).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == request.InvoiceReference, cancellationToken);
            if (invoice == null) return await RollbackError("Invoice not found.", 404);
            if (!VersionsMatch(invoice.RowVersion, version)) return await RollbackError("Invoice changed by another user. Reload and try again.", 409);
            if (request.Amount <= 0 || request.Amount > invoice.DueAmount) return await RollbackError("Payment amount exceeds the outstanding due.", 409);
            BankAccount? account = null;
            if (request.BankAccountId.HasValue)
            {
                account = await _bankAccounts.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.BankAccountId.Value && x.IsActive, cancellationToken);
                if (account == null) return await RollbackError("Bank or cash account is unavailable.", 409);
            }
            var publicId = Guid.NewGuid(); var now = _clock.GetUtcNow().UtcDateTime;
            var payment = new Payment { TenantId = tenantId, PublicId = publicId, ClientRequestId = request.ClientRequestId, InvoiceId = invoice.Id, Invoice = invoice, StudentId = invoice.StudentId, Student = invoice.Student, ReceiptNo = $"RCP-{now:yyyyMMdd}-{publicId:N}"[..25].ToUpperInvariant(), Amount = request.Amount, PaymentMethod = method, PaymentDate = now, ReceivedBy = _currentUser.UserId, TransactionId = Trim(request.TransactionId), Note = Trim(request.Note), BankAccountId = account?.Id, BankAccount = account, CreatedAt = now, CreatedBy = _currentUser.UserId };
            invoice.PaidAmount += request.Amount; invoice.DueAmount = Math.Max(0m, invoice.TotalAmount - (invoice.DiscountAmount ?? 0m) + (invoice.FineAmount ?? 0m) - invoice.PaidAmount); invoice.Status = invoice.DueAmount <= 0 ? "Paid" : "Partial"; invoice.UpdatedAt = now; invoice.UpdatedBy = _currentUser.UserId;
            if (account != null) { account.CurrentBalance += request.Amount; account.UpdatedAt = now; account.UpdatedBy = _currentUser.UserId; }
            await _payments.AddAsync(payment); await _unitOfWork.SaveChangesAsync(cancellationToken); await _unitOfWork.CommitTransactionAsync();
            return ApiResponse<StudentPaymentDto>.SuccessResponse(MapPayment(payment), "Payment received successfully.");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await SafeRollbackAsync(); _logger.LogWarning(ex, "Concurrent payment collection for invoice {Reference}", request.InvoiceReference); return ApiResponse<StudentPaymentDto>.ErrorResponse("Invoice changed by another user. Reload and try again.", 409);
        }
        catch (DbUpdateException ex)
        {
            await SafeRollbackAsync(); _logger.LogWarning(ex, "Duplicate/conflicting payment request {RequestId}", request.ClientRequestId); return ApiResponse<StudentPaymentDto>.ErrorResponse("Payment request conflicts with an existing transaction.", 409);
        }
        catch (Exception ex)
        {
            await SafeRollbackAsync(); _logger.LogError(ex, "Payment collection failed for invoice {Reference}", request.InvoiceReference); return ApiResponse<StudentPaymentDto>.ErrorResponse("Payment could not be completed.", 500);
        }
    }

    public async Task<ApiResponse<StudentInvoiceDto>> SetFineAsync(SetInvoiceFineDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage() || !TryVersion(request.InvoiceRowVersion, out var version)) return ApiResponse<StudentInvoiceDto>.ErrorResponse("Invoice update request is invalid.", 400);
        var invoice = await _invoices.GetQueryable().Include(x => x.Student).FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.PublicId == request.InvoiceReference, cancellationToken);
        if (invoice == null) return ApiResponse<StudentInvoiceDto>.ErrorResponse("Invoice not found.", 404);
        if (!VersionsMatch(invoice.RowVersion, version)) return ApiResponse<StudentInvoiceDto>.ErrorResponse("Invoice changed by another user. Reload and try again.", 409);
        invoice.FineAmount = request.FineAmount; invoice.DueAmount = Math.Max(0m, invoice.TotalAmount - (invoice.DiscountAmount ?? 0m) + request.FineAmount - invoice.PaidAmount); invoice.Status = invoice.DueAmount <= 0 ? "Paid" : invoice.PaidAmount > 0 ? "Partial" : "Unpaid"; invoice.UpdatedAt = _clock.GetUtcNow().UtcDateTime; invoice.UpdatedBy = _currentUser.UserId;
        await _unitOfWork.SaveChangesAsync(cancellationToken); return ApiResponse<StudentInvoiceDto>.SuccessResponse(MapInvoice(invoice), "Invoice fine updated.");
    }

    public async Task<ApiResponse<StudentLedgerDto>> GetStudentLedgerAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<StudentLedgerDto>.ErrorResponse("Finance access is required.", 403);
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.PublicId == studentReference, cancellationToken);
        if (student == null) return ApiResponse<StudentLedgerDto>.ErrorResponse("Student not found.", 404);
        var invoices = await _invoices.GetQueryable().AsNoTracking().Include(x => x.Student).Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id).OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToListAsync(cancellationToken);
        var payments = await _payments.GetQueryable().AsNoTracking().Include(x => x.Invoice).ThenInclude(x => x!.Student).Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id).OrderByDescending(x => x.PaymentDate).ToListAsync(cancellationToken);
        return ApiResponse<StudentLedgerDto>.SuccessResponse(new StudentLedgerDto { StudentReference = student.PublicId, StudentCode = student.StudentCode, StudentName = student.FullName, TotalBilled = invoices.Sum(x => x.TotalAmount - (x.DiscountAmount ?? 0m) + (x.FineAmount ?? 0m)), TotalPaid = invoices.Sum(x => x.PaidAmount), TotalDue = invoices.Sum(x => x.DueAmount), Invoices = invoices.Select(MapInvoice).ToList(), Payments = payments.Select(MapPayment).ToList() });
    }

    private async Task<string?> ValidateScopeAsync(GenerateStudentInvoicesDto request, CancellationToken cancellationToken)
    {
        var t = _currentUser.TenantId;
        if (!await _years.AnyAsync(x => x.TenantId == t && x.Id == request.AcademicYearId && x.IsActive)) return "Academic year is unavailable.";
        if (!await _classes.AnyAsync(x => x.TenantId == t && x.Id == request.ClassId && x.IsActive)) return "Class is unavailable.";
        if (!await _sections.AnyAsync(x => x.TenantId == t && x.Id == request.SectionId && x.ClassId == request.ClassId && x.IsActive)) return "Section is unavailable.";
        if (request.DueDate.Date < new DateTime(request.Year, request.Month, 1)) return "Due date cannot be before the billing month.";
        return null;
    }

    private static decimal CalculateDiscount(List<FeeStructure> structures, List<StudentDiscount> assigned)
    {
        var total = structures.Sum(x => x.Amount); decimal discount = 0m;
        foreach (var row in assigned)
        {
            var d = row.Discount; if (d == null || d.Value <= 0) continue;
            var basis = d.FeeHeadId.HasValue ? structures.Where(x => x.FeeHeadId == d.FeeHeadId.Value).Sum(x => x.Amount) : total;
            if (basis <= 0) continue;
            var amount = string.Equals(d.Type, "Percentage", StringComparison.OrdinalIgnoreCase) ? basis * Math.Min(d.Value, 100m) / 100m : Math.Min(d.Value, basis);
            discount += Math.Max(0m, amount);
        }
        return Math.Min(total, Math.Round(discount, 2));
    }

    private static StudentInvoiceDto MapInvoice(StudentInvoice x) => new() { Reference = x.PublicId, InvoiceNo = x.InvoiceNo, StudentId = x.StudentId, StudentReference = x.Student?.PublicId ?? Guid.Empty, StudentName = x.Student?.FullName ?? string.Empty, Roll = x.Student?.Roll ?? string.Empty, Month = x.Month, Year = x.Year, TotalAmount = x.TotalAmount, DiscountAmount = x.DiscountAmount ?? 0m, FineAmount = x.FineAmount ?? 0m, PaidAmount = x.PaidAmount, DueAmount = x.DueAmount, Status = x.Status, DueDate = x.DueDate, RowVersion = Convert.ToBase64String(x.RowVersion) };
    private static StudentPaymentDto MapPayment(Payment x) => new() { Reference = x.PublicId, InvoiceReference = x.Invoice?.PublicId ?? Guid.Empty, ReceiptNo = x.ReceiptNo, Amount = x.Amount, PaymentMethod = x.PaymentMethod, PaymentDate = x.PaymentDate, TransactionId = x.TransactionId, Invoice = x.Invoice == null ? new StudentInvoiceDto() : MapInvoice(x.Invoice) };
    private bool CanManage() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (_currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal") || _currentUser.IsInRole("Accountant"));
    private bool CanCollect() => CanManage() || _currentUser.IsInRole("Cashier");
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool TryVersion(string? value, out byte[] version) { try { version = Convert.FromBase64String(value ?? string.Empty); return version.Length > 0; } catch (FormatException) { version = []; return false; } }
    private static bool VersionsMatch(byte[] a, byte[] b) => a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    private async Task<ApiResponse<StudentPaymentDto>> RollbackError(string message, int code) { await SafeRollbackAsync(); return ApiResponse<StudentPaymentDto>.ErrorResponse(message, code); }
    private async Task SafeRollbackAsync() { try { await _unitOfWork.RollbackTransactionAsync(); } catch (Exception ex) { _logger.LogError(ex, "Finance rollback failed."); } }
}
