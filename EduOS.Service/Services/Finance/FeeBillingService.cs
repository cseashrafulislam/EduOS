using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.SaaS;
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

namespace EduOS.Service.Services.Finance;

public sealed class FeeBillingService : IFeeBillingService
{
    private readonly IGenericRepository<FeeStructure> _structures;
    private readonly IGenericRepository<FeeStructureLine> _structureLines;
    private readonly IGenericRepository<FeeHead> _heads;
    private readonly IGenericRepository<StudentDiscount> _discounts;
    private readonly IGenericRepository<DiscountRule> _discountRules;
    private readonly IGenericRepository<StudentInvoice> _invoices;
    private readonly IGenericRepository<StudentInvoiceLine> _invoiceLines;
    private readonly IGenericRepository<StudentPayment> _payments;
    private readonly IGenericRepository<PaymentAllocation> _allocations;
    private readonly IGenericRepository<BankAccount> _bankAccounts;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicLevel> _levels;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<FeeBillingService> _logger;

    public FeeBillingService(IGenericRepository<FeeStructure> structures,
        IGenericRepository<FeeStructureLine> structureLines, IGenericRepository<FeeHead> heads,
        IGenericRepository<StudentDiscount> discounts, IGenericRepository<DiscountRule> discountRules,
        IGenericRepository<StudentInvoice> invoices, IGenericRepository<StudentInvoiceLine> invoiceLines,
        IGenericRepository<StudentPayment> payments, IGenericRepository<PaymentAllocation> allocations,
        IGenericRepository<BankAccount> bankAccounts, IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<Student> students, IGenericRepository<AcademicYear> years,
        IGenericRepository<AcademicLevel> levels, IGenericRepository<AcademicBatch> batches,
        IGenericRepository<Campus> campuses, IUnitOfWork unitOfWork,
        ICurrentUserService currentUser, TimeProvider clock, ILogger<FeeBillingService> logger)
    {
        _structures = structures; _structureLines = structureLines; _heads = heads;
        _discounts = discounts; _discountRules = discountRules;
        _invoices = invoices; _invoiceLines = invoiceLines; _payments = payments;
        _allocations = allocations; _bankAccounts = bankAccounts;
        _enrollments = enrollments; _students = students; _years = years;
        _levels = levels; _batches = batches; _campuses = campuses;
        _uow = unitOfWork; _user = currentUser; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<FeeBillingOptionsDto>> GetOptionsAsync(CancellationToken ct = default)
    {
        if (!CanManage()) return Fail<FeeBillingOptionsDto>("Finance permission required.",403);
        var tenant=_user.TenantId;
        var years=await _years.GetQueryable().AsNoTracking().Where(x=>x.TenantId==tenant&&x.IsActive)
            .OrderByDescending(x=>x.StartDate).Take(200)
            .Select(x=>new FeeOptionDto{Id=x.Id,Name=x.Name}).ToListAsync(ct);
        var levels=await _levels.GetQueryable().AsNoTracking().Where(x=>x.TenantId==tenant&&x.IsActive)
            .OrderBy(x=>x.Name).Take(500)
            .Select(x=>new FeeOptionDto{Id=x.Id,Name=x.Name}).ToListAsync(ct);
        var batches=await _batches.GetQueryable().AsNoTracking().Where(x=>x.TenantId==tenant&&x.IsActive)
            .OrderBy(x=>x.Name).Take(500)
            .Select(x=>new FeeOptionDto{Id=x.Id,Name=x.Name,AcademicYearId=x.AcademicYearId,AcademicLevelId=x.AcademicLevelId}).ToListAsync(ct);
        var heads=await _heads.GetQueryable().AsNoTracking().Where(x=>x.TenantId==tenant&&x.IsActive)
            .OrderBy(x=>x.Name).Take(200)
            .Select(x=>new FeeOptionDto{Id=x.Id,Name=x.Name}).ToListAsync(ct);
        return ApiResponse<FeeBillingOptionsDto>.SuccessResponse(new FeeBillingOptionsDto
        {AcademicYears=years,AcademicLevels=levels,AcademicBatches=batches,FeeHeads=heads});
    }

    public async Task<ApiResponse<IReadOnlyList<FeeStudentOptionDto>>> SearchStudentsAsync(string search,CancellationToken ct = default)
    {
        if (!CanManage()) return Fail<IReadOnlyList<FeeStudentOptionDto>>("Finance permission required.",403);
        var term=search?.Trim();
        if (string.IsNullOrWhiteSpace(term)||term.Length<2||term.Length>100)
            return Fail<IReadOnlyList<FeeStudentOptionDto>>("Search requires 2–100 characters.");
        IReadOnlyList<FeeStudentOptionDto> rows=await _students.GetQueryable().AsNoTracking()
            .Where(x=>x.TenantId==_user.TenantId&&x.IsActive&&
                (x.StudentCode.StartsWith(term)||x.FullName.StartsWith(term)))
            .OrderBy(x=>x.StudentCode).ThenBy(x=>x.Id)
            .Select(x=>new FeeStudentOptionDto{Reference=x.PublicId,StudentCode=x.StudentCode,Name=x.FullName})
            .Take(25).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<FeeStudentOptionDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<bool>> SaveFeeStructureAsync(SaveFeeStructureDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Fail<bool>("Finance management permission required.", 403);
        if (request == null || request.AcademicYearId <= 0 || request.ClassId <= 0 ||
            request.FeeHeadId <= 0 || request.Amount < 0m)
            return Fail<bool>("Fee structure inputs are invalid.");
        var tenant = _user.TenantId;
        try
        {
            using var tx = SerializableScope();
            var year = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicYearId && x.IsActive, ct);
            var level = await _levels.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.ClassId && x.IsActive, ct);
            var head = await _heads.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.FeeHeadId && x.IsActive, ct);
            if (year == null || level == null || head == null)
                return Fail<bool>("Academic year, level or fee head is unavailable.", 409);
            var campusId = year.CampusId;
            if (!campusId.HasValue)
            {
                var campuses = await _campuses.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.IsActive).Select(x => x.Id).Take(2).ToListAsync(ct);
                if (campuses.Count != 1)
                    return Fail<bool>("This legacy fee endpoint requires an unambiguous campus. Use the scoped fee structure API.", 409);
                campusId = campuses[0];
            }
            var candidate = await _structures.GetQueryable().Where(x => x.TenantId == tenant &&
                x.CampusId == campusId.Value && x.AcademicYearId == year.Id &&
                x.AcademicLevelId == level.Id && x.AcademicProgramId == level.AcademicProgramId &&
                x.IsActive).Take(2).ToListAsync(ct);
            if (candidate.Count > 1)
                return Fail<bool>("Multiple fee structures match. Use the canonical fee structure API.", 409);
            FeeStructure structure;
            if (candidate.Count == 0)
            {
                structure = new FeeStructure
                {
                    TenantId = tenant, CampusId = campusId.Value, AcademicYearId = year.Id,
                    AcademicProgramId = level.AcademicProgramId, AcademicLevelId = level.Id,
                    Name = "Fees - " + year.Name + " - " + level.Name,
                    EffectiveFrom = year.StartDate, EffectiveTo = year.EndDate,
                    CurrencyCode = "BDT", IsActive = true,
                    CreatedAt = _clock.GetUtcNow().UtcDateTime, CreatedBy = _user.UserId
                };
                if (structure.Name.Length > 150) structure.Name = structure.Name[..150];
                await _structures.AddAsync(structure);
                await _uow.SaveChangesAsync(ct);
            }
            else structure = candidate[0];
            var lines = await _structureLines.GetQueryable().Where(x => x.TenantId == tenant &&
                x.FeeStructureId == structure.Id && x.FeeHeadId == head.Id &&
                x.Frequency == FeeFrequencyType.Monthly).Take(2).ToListAsync(ct);
            if (lines.Count > 1) return Fail<bool>("Fee line data is duplicated.", 409);
            if (lines.Count == 1)
            {
                if (lines[0].Amount != request.Amount)
                    return Fail<bool>("Existing fee amount changes require row-version validation in the canonical fee structure API.", 409);
            }
            else
            {
                await _structureLines.AddAsync(new FeeStructureLine
                {
                    TenantId = tenant, FeeStructureId = structure.Id, FeeHeadId = head.Id,
                    Frequency = FeeFrequencyType.Monthly, Amount = request.Amount,
                    IsMandatory = true, CreatedAt = _clock.GetUtcNow().UtcDateTime,
                    CreatedBy = _user.UserId
                });
                await _uow.SaveChangesAsync(ct);
            }
            tx.Complete();
            return ApiResponse<bool>.SuccessResponse(true, "Monthly fee structure saved.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Fee structure conflict for tenant {TenantId}", tenant);
            return Fail<bool>("Fee structure conflicts with an existing record.", 409);
        }
        catch (TransactionAbortedException) { return Fail<bool>("Concurrent fee structure write rejected.", 409); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fee structure save failed for tenant {TenantId}", tenant);
            return Fail<bool>("Fee structure could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<InvoiceBatchResultDto>> GenerateInvoicesAsync(
        GenerateStudentInvoicesDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Fail<InvoiceBatchResultDto>("Finance permission required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.Year is < 2000 or > 2200 ||
            request.Month is < 1 or > 12 || request.AcademicYearId <= 0 ||
            request.ClassId <= 0 || request.SectionId <= 0 || request.DueDate == default)
            return Fail<InvoiceBatchResultDto>("Billing period, batch and request ID are invalid.");
        var tenant = _user.TenantId;
        var invoiceDate = new DateOnly(request.Year, request.Month, 1);
        var dueDate = DateOnly.FromDateTime(request.DueDate);
        if (dueDate < invoiceDate)
            return Fail<InvoiceBatchResultDto>("Due date cannot be before the invoice month.");
        try
        {
            using var tx = SerializableScope();
            var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.SectionId && x.IsActive &&
                x.AcademicYearId == request.AcademicYearId && x.AcademicLevelId == request.ClassId, ct);
            if (batch == null) return Fail<InvoiceBatchResultDto>("Academic batch is unavailable.", 409);
            var matching = await _structures.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.CampusId == batch.CampusId && x.AcademicYearId == batch.AcademicYearId &&
                (!x.AcademicProgramId.HasValue || x.AcademicProgramId == batch.AcademicProgramId) &&
                (!x.AcademicLevelId.HasValue || x.AcademicLevelId == batch.AcademicLevelId) &&
                (!x.AcademicBatchId.HasValue || x.AcademicBatchId == batch.Id) &&
                x.IsActive && x.EffectiveFrom <= invoiceDate &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= invoiceDate))
                .Take(2).ToListAsync(ct);
            if (matching.Count != 1)
                return Fail<InvoiceBatchResultDto>("Exactly one active fee structure must match the selected batch and billing month.", 409);
            var structure = matching[0];
            var lines = await (from l in _structureLines.GetQueryable().AsNoTracking()
                join h in _heads.GetQueryable().AsNoTracking() on l.FeeHeadId equals h.Id
                where l.TenantId == tenant && h.TenantId == tenant &&
                    l.FeeStructureId == structure.Id
                select new { Line = l, Head = h }).ToListAsync(ct);
            if (lines.Count == 0 || lines.Any(x => !x.Head.IsActive || x.Line.Frequency != FeeFrequencyType.Monthly ||
                x.Line.Amount < 0m))
                return Fail<InvoiceBatchResultDto>("Only valid monthly fee lines may be billed by this legacy endpoint.", 409);
            var rows = await (from enrollment in _enrollments.GetQueryable().AsNoTracking()
                join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
                where enrollment.TenantId == tenant && student.TenantId == tenant &&
                    enrollment.AcademicBatchId == batch.Id && enrollment.IsCurrent && enrollment.IsActive &&
                    enrollment.State == EnrollmentState.Active && student.IsActive
                orderby enrollment.RollNo, enrollment.Id
                select enrollment.Id).Take(501).ToListAsync(ct);
            if (rows.Count == 0) return Fail<InvoiceBatchResultDto>("There are no active students to bill.", 409);
            if (rows.Count > 500)
                return Fail<InvoiceBatchResultDto>("The batch exceeds 500 students. Use paged/queued canonical billing.", 409);
            var endMonth = invoiceDate.AddMonths(1);
            var existingInvoices = await _invoices.GetQueryable().Where(x => x.TenantId == tenant &&
                rows.Contains(x.StudentEnrollmentId) && x.InvoiceDate >= invoiceDate &&
                x.InvoiceDate < endMonth && x.State != InvoiceState.Cancelled &&
                x.State != InvoiceState.Refunded).ToListAsync(ct);
            var byEnrollment = existingInvoices.GroupBy(x => x.StudentEnrollmentId)
                .ToDictionary(x => x.Key, x => x.ToList());
            var assignmentRows = await (from assigned in _discounts.GetQueryable().AsNoTracking()
                join rule in _discountRules.GetQueryable().AsNoTracking()
                    on assigned.DiscountRuleId equals rule.Id
                where assigned.TenantId == tenant && rule.TenantId == tenant &&
                    rows.Contains(assigned.StudentEnrollmentId) && assigned.IsActive && rule.IsActive &&
                    assigned.EffectiveFrom <= invoiceDate &&
                    (!assigned.EffectiveTo.HasValue || assigned.EffectiveTo >= invoiceDate) &&
                    (!rule.EffectiveFrom.HasValue || rule.EffectiveFrom <= invoiceDate) &&
                    (!rule.EffectiveTo.HasValue || rule.EffectiveTo >= invoiceDate)
                select new { assigned.StudentEnrollmentId, Rule = rule }).ToListAsync(ct);
            var discounts = assignmentRows.GroupBy(x => x.StudentEnrollmentId)
                .ToDictionary(x => x.Key, x => x.Select(v => v.Rule).ToList());
            var now = _clock.GetUtcNow().UtcDateTime;
            var created = 0; var replayed = 0;
            var invoiceIds = new List<long>();
            foreach (var enrollmentId in rows)
            {
                var requestId = PerEnrollmentKey(request.ClientRequestId, enrollmentId);
                if (byEnrollment.TryGetValue(enrollmentId, out var others) && others.Count > 0)
                {
                    if (others.Count != 1 || others[0].ClientRequestId != requestId ||
                        others[0].DueDate != dueDate)
                        return Fail<InvoiceBatchResultDto>("An invoice already exists for this student and billing month.", 409);
                    replayed++; invoiceIds.Add(others[0].Id);
                    continue;
                }
                discounts.TryGetValue(enrollmentId, out var applied);
                var subtotal = lines.Sum(x => x.Line.Amount);
                var discount = CalculateDiscount(lines.Select(x => (x.Line.FeeHeadId, x.Line.Amount)).ToArray(),
                    applied ?? new List<DiscountRule>());
                var publicId = Guid.NewGuid();
                var invoice = new StudentInvoice
                {
                    TenantId = tenant, PublicId = publicId, ClientRequestId = requestId,
                    StudentEnrollmentId = enrollmentId,
                    InvoiceNumber = "INV-" + invoiceDate.ToString("yyyyMM") + "-" + publicId.ToString("N")[..19].ToUpperInvariant(),
                    InvoiceDate = invoiceDate, DueDate = dueDate, Subtotal = subtotal,
                    DiscountAmount = discount, FineAmount = 0m, TotalAmount = subtotal - discount,
                    PaidAmount = 0m, DueAmount = subtotal - discount, CurrencyCode = structure.CurrencyCode,
                    State = InvoiceState.Issued, CreatedAt = now, CreatedBy = _user.UserId
                };
                await _invoices.AddAsync(invoice);
                await _uow.SaveChangesAsync(ct);
                var appliedSoFar = 0m;
                for (var i = 0; i < lines.Count; i++)
                {
                    var entry = lines[i];
                    var lineDiscount = i == lines.Count - 1 ? discount - appliedSoFar :
                        subtotal <= 0m ? 0m : Math.Round(entry.Line.Amount / subtotal * discount, 2);
                    appliedSoFar += lineDiscount;
                    await _invoiceLines.AddAsync(new StudentInvoiceLine
                    {
                        TenantId = tenant, StudentInvoiceId = invoice.Id,
                        FeeHeadId = entry.Head.Id, FeeHeadCodeSnapshot = entry.Head.Code,
                        FeeHeadNameSnapshot = entry.Head.Name, Description = entry.Head.Name,
                        Amount = entry.Line.Amount, DiscountAmount = lineDiscount, FineAmount = 0m,
                        NetAmount = entry.Line.Amount - lineDiscount,
                        CreatedAt = now, CreatedBy = _user.UserId
                    });
                }
                await _uow.SaveChangesAsync(ct);
                created++; invoiceIds.Add(invoice.Id);
            }
            var dtos = await InvoiceDtosAsync(invoiceIds, ct);
            tx.Complete();
            return ApiResponse<InvoiceBatchResultDto>.SuccessResponse(new InvoiceBatchResultDto
            {
                ClientRequestId = request.ClientRequestId, Generated = created, Existing = replayed,
                Invoices = dtos
            }, "Monthly student invoice request completed.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Invoice generation conflict for tenant {TenantId}", tenant);
            return Fail<InvoiceBatchResultDto>("Billing conflicts with a prior request or existing invoice.", 409);
        }
        catch (TransactionAbortedException) { return Fail<InvoiceBatchResultDto>("Concurrent billing request rejected.", 409); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Monthly billing failed for tenant {TenantId}", tenant);
            return Fail<InvoiceBatchResultDto>("Invoices could not be generated.", 500);
        }
    }

    public async Task<ApiResponse<StudentPaymentDto>> CollectPaymentAsync(
        CollectStudentPaymentDto request, CancellationToken ct = default)
    {
        if (!CanCollect()) return Fail<StudentPaymentDto>("Fee collection permission required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.InvoiceReference == Guid.Empty ||
            request.Amount <= 0m || !TryVersion(request.InvoiceRowVersion, out var expected))
            return Fail<StudentPaymentDto>("Valid payment amount, invoice and row version are required.");
        if (request.PaymentMethod != "Cash")
            return Fail<StudentPaymentDto>("Non-cash payments require verified gateway/bank reconciliation through the canonical payment API.", 409);
        if (request.BankAccountId.HasValue || !string.IsNullOrWhiteSpace(request.TransactionId) ||
            !string.IsNullOrWhiteSpace(request.Note))
            return Fail<StudentPaymentDto>("Cash receipt cannot include untracked bank, transaction or note details.");
        var tenant = _user.TenantId;
        try
        {
            using var tx = SerializableScope();
            var replay = await _payments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, ct);
            if (replay != null)
            {
                var allocation = await _allocations.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.StudentPaymentId == replay.Id, ct);
                var sourceInvoice = allocation == null ? null : await _invoices.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == allocation.StudentInvoiceId, ct);
                if (sourceInvoice?.PublicId != request.InvoiceReference ||
                    replay.Amount != request.Amount || replay.PaymentMethod != PaymentMethodType.Cash)
                    return Fail<StudentPaymentDto>("Client request ID is already used for different payment details.", 409);
                tx.Complete();
                return ApiResponse<StudentPaymentDto>.SuccessResponse(await PaymentDtoAsync(replay, ct),
                    "Cash payment already recorded.");
            }
            var invoice = await _invoices.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.InvoiceReference, ct);
            if (invoice == null) return Fail<StudentPaymentDto>("Invoice not found.", 404);
            if (!VersionsMatch(invoice.RowVersion, expected))
                return Fail<StudentPaymentDto>("Invoice changed. Reload and retry.", 409);
            if (invoice.State is InvoiceState.Cancelled or InvoiceState.Refunded or InvoiceState.Draft)
                return Fail<StudentPaymentDto>("Invoice is not eligible for collection.", 409);
            if (request.Amount > invoice.DueAmount)
                return Fail<StudentPaymentDto>("Payment exceeds the remaining invoice balance.", 409);
            var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == invoice.StudentEnrollmentId, ct);
            if (enrollment == null) return Fail<StudentPaymentDto>("Invoice's student enrollment is missing.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            var publicId = Guid.NewGuid();
            var payment = new StudentPayment
            {
                TenantId = tenant, PublicId = publicId, ClientRequestId = request.ClientRequestId,
                StudentId = enrollment.StudentId,
                ReceiptNumber = "RCP-" + now.ToString("yyyyMMdd") + "-" + publicId.ToString("N")[..18].ToUpperInvariant(),
                PaymentDate = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime),
                PaymentMethod = PaymentMethodType.Cash, State = PaymentState.Successful,
                Amount = request.Amount, CurrencyCode = invoice.CurrencyCode,
                ReceivedByUserId = _user.UserId, InitiatedAt = now, CompletedAt = now,
                CreatedAt = now, CreatedBy = _user.UserId
            };
            await _payments.AddAsync(payment);
            await _uow.SaveChangesAsync(ct);
            await _allocations.AddAsync(new PaymentAllocation
            {
                TenantId = tenant, StudentPaymentId = payment.Id, StudentInvoiceId = invoice.Id,
                Amount = request.Amount, CreatedAt = now, CreatedBy = _user.UserId
            });
            invoice.PaidAmount += request.Amount;
            invoice.DueAmount -= request.Amount;
            invoice.State = invoice.DueAmount <= 0m ? InvoiceState.Paid : InvoiceState.PartiallyPaid;
            if (invoice.State == InvoiceState.Paid) invoice.PaidAt = now;
            invoice.UpdatedAt = now; invoice.UpdatedBy = _user.UserId;
            await _uow.SaveChangesAsync(ct);
            var dto = await PaymentDtoAsync(payment, ct);
            tx.Complete();
            return new ApiResponse<StudentPaymentDto>
            {
                Success = true, StatusCode = 201, Message = "Cash payment and invoice allocation saved.",
                Data = dto
            };
        }
        catch (DbUpdateConcurrencyException) { return Fail<StudentPaymentDto>("Invoice changed concurrently. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Payment allocation conflict for tenant {TenantId}", tenant);
            return Fail<StudentPaymentDto>("Payment conflicts with another collection.", 409);
        }
        catch (TransactionAbortedException) { return Fail<StudentPaymentDto>("Concurrent payment transaction rejected.", 409); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cash collection failed for tenant {TenantId}", tenant);
            return Fail<StudentPaymentDto>("Payment could not be recorded.", 500);
        }
    }

    public Task<ApiResponse<StudentInvoiceDto>> SetFineAsync(SetInvoiceFineDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Fail<StudentInvoiceDto>("Finance management permission required.", 403));
        return Task.FromResult(Fail<StudentInvoiceDto>(
            "Fines must be recorded through the audited StudentFine workflow; direct invoice header mutation is not supported.", 409));
    }

    public async Task<ApiResponse<StudentLedgerDto>> GetStudentLedgerAsync(Guid studentReference, CancellationToken ct = default)
    {
        if (!CanManage()) return Fail<StudentLedgerDto>("Finance ledger permission required.", 403);
        var tenant = _user.TenantId;
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == studentReference, ct);
        if (student == null) return Fail<StudentLedgerDto>("Student not found.", 404);
        var ids = await _enrollments.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.StudentId == student.Id).Select(x => x.Id).ToListAsync(ct);
        if (ids.Count == 0)
            return ApiResponse<StudentLedgerDto>.SuccessResponse(new StudentLedgerDto
            { StudentReference = student.PublicId, StudentCode = student.StudentCode, StudentName = student.FullName });
        var invoiceRows = await _invoices.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            ids.Contains(x.StudentEnrollmentId) && x.State != InvoiceState.Cancelled &&
            x.State != InvoiceState.Refunded).OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.Id)
            .Take(501).Select(x => x.Id).ToListAsync(ct);
        if (invoiceRows.Count > 500)
            return Fail<StudentLedgerDto>("Ledger contains more than 500 invoices. Use the paged ledger/report endpoint.", 409);
        var invoices = await InvoiceDtosAsync(invoiceRows, ct);
        var invoiceIds = invoices.Select(x => x.Id).ToArray();
        var paymentIds = await _allocations.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            invoiceIds.Contains(x.StudentInvoiceId)).Select(x => x.StudentPaymentId)
            .Distinct().Take(501).ToListAsync(ct);
        if (paymentIds.Count > 500)
            return Fail<StudentLedgerDto>("Ledger contains more than 500 payments. Use paged ledger/report endpoint.", 409);
        var payments = await _payments.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            paymentIds.Contains(x.Id)).OrderByDescending(x => x.PaymentDate)
            .ToListAsync(ct);
        var paymentDtos = new List<StudentPaymentDto>();
        foreach (var p in payments) paymentDtos.Add(await PaymentDtoAsync(p, ct));
        return ApiResponse<StudentLedgerDto>.SuccessResponse(new StudentLedgerDto
        {
            StudentReference = student.PublicId, StudentCode = student.StudentCode,
            StudentName = student.FullName,
            TotalBilled = invoices.Sum(x => x.TotalAmount),
            TotalPaid = invoices.Sum(x => x.PaidAmount),
            TotalDue = invoices.Sum(x => x.DueAmount),
            Invoices = invoices, Payments = paymentDtos
        });
    }

    private async Task<List<StudentInvoiceDto>> InvoiceDtosAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var rows = await (from inv in _invoices.GetQueryable().AsNoTracking()
            join en in _enrollments.GetQueryable().AsNoTracking() on inv.StudentEnrollmentId equals en.Id
            join student in _students.GetQueryable().AsNoTracking() on en.StudentId equals student.Id
            where inv.TenantId == tenant && en.TenantId == tenant && student.TenantId == tenant &&
                ids.Contains(inv.Id)
            orderby inv.InvoiceDate descending, inv.Id descending
            select new { inv, en, student }).ToListAsync(ct);
        var details = await _invoiceLines.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            ids.Contains(x.StudentInvoiceId)).OrderBy(x => x.Id).ToListAsync(ct);
        var byInvoice = details.GroupBy(x => x.StudentInvoiceId).ToDictionary(x => x.Key, x => x.Select(l =>
            new StudentInvoiceLineDto
            {
                Id = l.Id, FeeHeadId = l.FeeHeadId, FeeHeadName = l.FeeHeadNameSnapshot ?? "",
                Description = l.Description, Amount = l.Amount, DiscountAmount = l.DiscountAmount,
                FineAmount = l.FineAmount, NetAmount = l.NetAmount
            }).ToArray());
        return rows.Select(x => new StudentInvoiceDto
        {
            Id = x.inv.Id, Reference = x.inv.PublicId, StudentEnrollmentReference = x.en.PublicId,
            StudentReference = x.student.PublicId, StudentCode = x.student.StudentCode,
            StudentName = x.student.FullName, InvoiceNumber = x.inv.InvoiceNumber,
            InvoiceDate = x.inv.InvoiceDate, DueDate = x.inv.DueDate, Subtotal = x.inv.Subtotal,
            DiscountAmount = x.inv.DiscountAmount, FineAmount = x.inv.FineAmount,
            TotalAmount = x.inv.TotalAmount, PaidAmount = x.inv.PaidAmount, DueAmount = x.inv.DueAmount,
            CurrencyCode = x.inv.CurrencyCode, State = x.inv.State, PaidAt = x.inv.PaidAt,
            RowVersion = Convert.ToBase64String(x.inv.RowVersion),
            Lines = byInvoice.GetValueOrDefault(x.inv.Id) ?? Array.Empty<StudentInvoiceLineDto>()
        }).ToList();
    }

    private async Task<StudentPaymentDto> PaymentDtoAsync(StudentPayment payment, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var allocations = await (from a in _allocations.GetQueryable().AsNoTracking()
            join invoice in _invoices.GetQueryable().AsNoTracking() on a.StudentInvoiceId equals invoice.Id
            where a.TenantId == tenant && invoice.TenantId == tenant &&
                a.StudentPaymentId == payment.Id
            select new PaymentAllocationDto
            {
                Id = a.Id, InvoiceReference = invoice.PublicId,
                InvoiceNumber = invoice.InvoiceNumber, Amount = a.Amount
            }).ToListAsync(ct);
        var bankName = payment.BankAccountId.HasValue
            ? await _bankAccounts.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.Id == payment.BankAccountId.Value).Select(x => x.BankName).FirstOrDefaultAsync(ct) : null;
        return new StudentPaymentDto
        {
            Id = payment.Id, Reference = payment.PublicId, ReceiptNumber = payment.ReceiptNumber,
            PaymentDate = payment.PaymentDate, PaymentMethod = payment.PaymentMethod,
            Amount = payment.Amount, CurrencyCode = payment.CurrencyCode, State = payment.State,
            BankAccountId = payment.BankAccountId, BankAccountName = bankName,
            ExternalReference = payment.ExternalReference, ReceivedByUserId = payment.ReceivedByUserId,
            JournalId = payment.JournalId, RowVersion = Convert.ToBase64String(payment.RowVersion),
            Allocations = allocations
        };
    }
    private static decimal CalculateDiscount((long HeadId, decimal Amount)[] lines, List<DiscountRule> rules)
    {
        var total = lines.Sum(x => x.Amount);
        decimal applied = 0m;
        foreach (var rule in rules)
        {
            var basis = rule.FeeHeadId.HasValue
                ? lines.Where(x => x.HeadId == rule.FeeHeadId.Value).Sum(x => x.Amount)
                : total;
            if (basis == 0m) continue;
            var amount = rule.IsPercentage
                ? basis * Math.Clamp(rule.Value, 0m, 100m) / 100m
                : Math.Max(0m, rule.Value);
            applied += Math.Min(basis, Math.Round(amount, 2));
        }
        return Math.Min(total, applied);
    }
    private static Guid PerEnrollmentKey(Guid batchRequestId, long enrollmentId)
    {
        var source = Encoding.UTF8.GetBytes(batchRequestId.ToString("N") + "|" + enrollmentId);
        var hash = SHA256.HashData(source);
        return new Guid(hash.AsSpan(0, 16));
    }
    private static bool TryVersion(string? supplied, out byte[] value)
    {
        value = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { value = Convert.FromBase64String(supplied); return value.Length > 0; }
        catch (FormatException) { return false; }
    }
    private static bool VersionsMatch(byte[] current, byte[] expected) =>
        current.Length == expected.Length && CryptographicOperations.FixedTimeEquals(current, expected);
    private static TransactionScope SerializableScope() => new(TransactionScopeOption.Required,
        new TransactionOptions { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);
    private bool CanManage() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("Accountant"));
    private bool CanCollect() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (CanManage() || _user.IsInRole("Cashier"));
    private static ApiResponse<T> Fail<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);
}
