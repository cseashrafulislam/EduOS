using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
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
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicLevel> _levels;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<Tenant> _tenants;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<FeeBillingService> _logger;

    public FeeBillingService(IGenericRepository<FeeStructure> structures,
        IGenericRepository<FeeStructureLine> structureLines, IGenericRepository<FeeHead> heads,
        IGenericRepository<StudentDiscount> discounts, IGenericRepository<DiscountRule> discountRules,
        IGenericRepository<StudentInvoice> invoices, IGenericRepository<StudentInvoiceLine> invoiceLines,
        IGenericRepository<StudentPayment> payments, IGenericRepository<PaymentAllocation> allocations,
        IGenericRepository<StudentEnrollment> enrollments, IGenericRepository<Student> students,
        IGenericRepository<AcademicYear> years, IGenericRepository<AcademicLevel> levels,
        IGenericRepository<AcademicBatch> batches, IGenericRepository<Campus> campuses,
        IGenericRepository<Tenant> tenants, IUnitOfWork uow, ICurrentUserService user,
        TimeProvider clock, ILogger<FeeBillingService> logger)
    {
        _structures = structures; _structureLines = structureLines; _heads = heads;
        _discounts = discounts; _discountRules = discountRules; _invoices = invoices;
        _invoiceLines = invoiceLines; _payments = payments; _allocations = allocations;
        _enrollments = enrollments; _students = students; _years = years;
        _levels = levels; _batches = batches; _campuses = campuses; _tenants = tenants;
        _uow = uow; _user = user; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<FeeBillingOptionsDto>> GetOptionsAsync(CancellationToken ct = default)
    {
        if (!CanManage()) return Error<FeeBillingOptionsDto>("Finance access required.", 403);
        var tenant = _user.TenantId;
        var years = await _years.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && !x.IsDeleted).OrderByDescending(x => x.StartDate)
            .Take(200).Select(x => new FeeOptionDto { Id = x.Id, Name = x.Name }).ToListAsync(ct);
        var levels = await _levels.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && !x.IsDeleted).OrderBy(x => x.Name)
            .Take(300).Select(x => new FeeOptionDto { Id = x.Id, Name = x.Name }).ToListAsync(ct);
        var batches = await _batches.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && !x.IsDeleted).OrderBy(x => x.Name)
            .Take(500).Select(x => new FeeOptionDto { Id = x.Id, Name = x.Name,
                AcademicYearId = x.AcademicYearId, AcademicLevelId = x.AcademicLevelId }).ToListAsync(ct);
        var heads = await _heads.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && !x.IsDeleted).OrderBy(x => x.Name)
            .Take(300).Select(x => new FeeOptionDto { Id = x.Id, Name = x.Name }).ToListAsync(ct);
        return ApiResponse<FeeBillingOptionsDto>.SuccessResponse(new FeeBillingOptionsDto
        { AcademicYears = years, AcademicLevels = levels, AcademicBatches = batches, FeeHeads = heads });
    }

    public async Task<ApiResponse<IReadOnlyList<FeeStudentOptionDto>>> SearchStudentsAsync(
        string search, int take = 20, CancellationToken ct = default)
    {
        if (!CanManage()) return Error<IReadOnlyList<FeeStudentOptionDto>>("Finance access required.", 403);
        if (string.IsNullOrWhiteSpace(search) || search.Trim().Length is < 2 or > 100 ||
            take is < 1 or > 100)
            return Error<IReadOnlyList<FeeStudentOptionDto>>("Search requires 2–100 characters; take 1–100.");
        var term = search.Trim();
        IReadOnlyList<FeeStudentOptionDto> students = await _students.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted &&
                x.StatusCode == "Active" &&
                (x.StudentCode.StartsWith(term) || x.FullName.StartsWith(term)))
            .OrderBy(x => x.StudentCode).ThenBy(x => x.Id).Take(take)
            .Select(x => new FeeStudentOptionDto
            { Reference = x.PublicId, StudentCode = x.StudentCode, Name = x.FullName }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<FeeStudentOptionDto>>.SuccessResponse(students);
    }

    public Task<ApiResponse<FeeStructureDto>> SaveFeeStructureAsync(long? feeStructureId,
        SaveFeeStructureRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<FeeStructureDto>("Finance management required.", 403));
        if (feeStructureId is <= 0 || request == null || request.ClientRequestId == Guid.Empty ||
            request.CampusId <= 0 || request.AcademicYearId <= 0 ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 150 ||
            string.IsNullOrWhiteSpace(request.CurrencyCode) || request.CurrencyCode.Length != 3 ||
            request.EffectiveFrom == default ||
            request.EffectiveTo.HasValue && request.EffectiveTo < request.EffectiveFrom ||
            request.Lines == null || request.Lines.Count is < 1 or > 100 ||
            request.Lines.Any(x => x == null || x.FeeHeadId <= 0 || x.Amount < 0m ||
                x.Amount != decimal.Round(x.Amount, 2) || !Enum.IsDefined(x.Frequency) ||
                x.DueDayOfMonth is < 1 or > 31 || x.Id is <= 0) ||
            request.Lines.GroupBy(x => (x.FeeHeadId, x.Frequency)).Any(g => g.Count() > 1) ||
            request.Lines.Sum(x => x.Amount) <= 0m)
            return Task.FromResult(Error<FeeStructureDto>("Invalid fee structure or lines."));
        return WriteAsync("fee structure", async token =>
        {
            var tenant = _user.TenantId;
            var year = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicYearId &&
                x.IsActive && !x.IsDeleted, token);
            var campus = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.CampusId && x.IsActive && !x.IsDeleted, token);
            if (year == null || campus == null ||
                year.CampusId.HasValue && year.CampusId != campus.Id ||
                request.EffectiveFrom < year.StartDate ||
                request.EffectiveTo.HasValue && request.EffectiveTo > year.EndDate)
                return Error<FeeStructureDto>("Fee structure is outside campus or academic year.", 409);
            if (request.AcademicLevelId.HasValue && !await _levels.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenant && x.Id == request.AcademicLevelId &&
                    x.IsActive && !x.IsDeleted, token))
                return Error<FeeStructureDto>("Academic level not found.", 404);
            if (request.AcademicBatchId.HasValue)
            {
                var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.AcademicBatchId &&
                    x.AcademicYearId == year.Id && x.CampusId == campus.Id &&
                    x.IsActive && !x.IsDeleted, token);
                if (batch == null || request.AcademicProgramId.HasValue &&
                    batch.AcademicProgramId != request.AcademicProgramId ||
                    request.AcademicLevelId.HasValue && batch.AcademicLevelId != request.AcademicLevelId)
                    return Error<FeeStructureDto>("Batch scope does not match fee structure.", 409);
            }
            var headIds = request.Lines.Select(x => x.FeeHeadId).Distinct().ToArray();
            var heads = await _heads.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && headIds.Contains(x.Id) &&
                    x.IsActive && !x.IsDeleted).Select(x => x.Id).ToListAsync(token);
            if (heads.Count != headIds.Length)
                return Error<FeeStructureDto>("One or more fee heads are unavailable.", 409);
            var row = feeStructureId.HasValue ? await _structures.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == feeStructureId.Value && !x.IsDeleted, token) : null;
            if (feeStructureId.HasValue && row == null) return Error<FeeStructureDto>("Fee structure not found.", 404);
            if (row != null && !Match(row.RowVersion, request.RowVersion))
                return Error<FeeStructureDto>("Fee structure changed. Reload and retry.", 409);
            var overlaps = await _structures.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.CampusId == campus.Id && x.AcademicYearId == year.Id &&
                x.AcademicBatchId == request.AcademicBatchId &&
                x.AcademicProgramId == request.AcademicProgramId &&
                x.AcademicLevelId == request.AcademicLevelId && x.IsActive && !x.IsDeleted &&
                (!feeStructureId.HasValue || x.Id != feeStructureId.Value) &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= request.EffectiveFrom) &&
                (!request.EffectiveTo.HasValue || x.EffectiveFrom <= request.EffectiveTo.Value), token);
            if (overlaps) return Error<FeeStructureDto>("Overlapping fee structure already exists.", 409);
            var existingLines = row == null ? new List<FeeStructureLine>() :
                await _structureLines.GetQueryable().Where(x => x.TenantId == tenant &&
                    x.FeeStructureId == row.Id && !x.IsDeleted).ToListAsync(token);
            if (row != null)
            {
                var billed = await (from invoice in _invoices.GetQueryable().AsNoTracking()
                    join enrollment in _enrollments.GetQueryable().AsNoTracking()
                        on invoice.StudentEnrollmentId equals enrollment.Id
                    where invoice.TenantId == tenant && enrollment.TenantId == tenant &&
                        enrollment.AcademicYearId == row.AcademicYearId &&
                        enrollment.CampusId == row.CampusId &&
                        (!row.AcademicBatchId.HasValue || enrollment.AcademicBatchId == row.AcademicBatchId) &&
                        !invoice.IsDeleted && invoice.State != InvoiceState.Cancelled
                    select invoice.Id).AnyAsync(token);
                if (billed) return Error<FeeStructureDto>(
                    "Billed fee structures cannot be edited; create a future effective-dated version.", 409);
                if (request.Lines.Count != existingLines.Count +
                    request.Lines.Count(x => !x.Id.HasValue))
                    return Error<FeeStructureDto>("Existing fee lines must be retained or amended with row versions.", 409);
                foreach (var line in request.Lines.Where(x => x.Id.HasValue))
                {
                    var old = existingLines.FirstOrDefault(x => x.Id == line.Id);
                    if (old == null || !Match(old.RowVersion, line.RowVersion))
                        return Error<FeeStructureDto>("Fee line changed. Reload.", 409);
                }
            }
            var now = _clock.GetUtcNow().UtcDateTime;
            if (row == null)
            {
                row = new FeeStructure { TenantId = tenant, CreatedAt = now, CreatedBy = _user.UserId };
                await _structures.AddAsync(row);
            }
            row.Name = request.Name.Trim(); row.CurrencyCode = request.CurrencyCode.ToUpperInvariant();
            row.CampusId = campus.Id; row.AcademicYearId = year.Id;
            row.AcademicProgramId = request.AcademicProgramId; row.AcademicLevelId = request.AcademicLevelId;
            row.AcademicBatchId = request.AcademicBatchId;
            row.EffectiveFrom = request.EffectiveFrom; row.EffectiveTo = request.EffectiveTo;
            row.IsActive = request.IsActive; row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
            if (feeStructureId.HasValue) _structures.Update(row);
            await _uow.SaveChangesAsync(token);
            foreach (var line in request.Lines)
            {
                var entity = line.Id.HasValue ? existingLines.First(x => x.Id == line.Id.Value) :
                    new FeeStructureLine { TenantId = tenant, FeeStructureId = row.Id,
                        CreatedAt = now, CreatedBy = _user.UserId };
                entity.FeeHeadId = line.FeeHeadId; entity.Frequency = line.Frequency;
                entity.Amount = line.Amount; entity.DueDayOfMonth = line.DueDayOfMonth;
                entity.IsMandatory = line.IsMandatory;
                if (line.Id.HasValue)
                {
                    entity.UpdatedAt = now; entity.UpdatedBy = _user.UserId; _structureLines.Update(entity);
                }
                else await _structureLines.AddAsync(entity);
            }
            await _uow.SaveChangesAsync(token);
            return ApiResponse<FeeStructureDto>.SuccessResponse(await MapStructureAsync(row, token),
                "Fee structure saved.");
        }, ct);
    }

    public Task<ApiResponse<InvoiceBatchResultDto>> GenerateInvoicesAsync(
        GenerateStudentInvoiceBatchRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<InvoiceBatchResultDto>("Finance access required.", 403));
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.AcademicBatchId <= 0 || request.FeeStructureId <= 0 ||
            request.Year is < 2000 or > 2200 || request.Month is < 1 or > 12 ||
            request.InvoiceDate == default || request.InvoiceDate.Year != request.Year ||
            request.InvoiceDate.Month != request.Month || request.DueDate < request.InvoiceDate)
            return Task.FromResult(Error<InvoiceBatchResultDto>("Invalid billing period, dates or idempotency key."));
        return WriteAsync("monthly invoice batch", async token =>
        {
            var tenant = _user.TenantId;
            var monthStart = new DateOnly(request.Year, request.Month, 1);
            var monthEnd = monthStart.AddMonths(1);
            var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicBatchId &&
                x.IsActive && !x.IsDeleted, token);
            var structure = await _structures.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.FeeStructureId &&
                x.IsActive && !x.IsDeleted && x.EffectiveFrom <= monthStart &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= monthStart), token);
            if (batch == null || structure == null || batch.CampusId != structure.CampusId ||
                batch.AcademicYearId != structure.AcademicYearId ||
                structure.AcademicBatchId.HasValue && structure.AcademicBatchId != batch.Id ||
                structure.AcademicLevelId.HasValue && structure.AcademicLevelId != batch.AcademicLevelId ||
                structure.AcademicProgramId.HasValue && structure.AcademicProgramId != batch.AcademicProgramId)
                return Error<InvoiceBatchResultDto>("Fee structure is not valid for this academic batch.", 409);
            var lines = await (from line in _structureLines.GetQueryable().AsNoTracking()
                join head in _heads.GetQueryable().AsNoTracking()
                    on new { line.TenantId, Id = line.FeeHeadId }
                    equals new { head.TenantId, head.Id }
                where line.TenantId == tenant && line.FeeStructureId == structure.Id &&
                    !line.IsDeleted && !head.IsDeleted && head.IsActive
                select new { line, head }).ToListAsync(token);
            if (lines.Count == 0 || lines.Any(x => x.line.Amount < 0m ||
                x.line.Frequency != FeeFrequencyType.Monthly) ||
                lines.Sum(x => x.line.Amount) <= 0m)
                return Error<InvoiceBatchResultDto>("Monthly billing requires positive, active monthly fee lines.", 409);
            var enrollments = await (from enrollment in _enrollments.GetQueryable().AsNoTracking()
                join student in _students.GetQueryable().AsNoTracking()
                    on new { enrollment.TenantId, Id = enrollment.StudentId }
                    equals new { student.TenantId, student.Id }
                where enrollment.TenantId == tenant && enrollment.AcademicBatchId == batch.Id &&
                    enrollment.IsCurrent && enrollment.State == EnrollmentState.Active &&
                    !enrollment.IsDeleted && !student.IsDeleted && student.StatusCode == "Active"
                orderby enrollment.Id
                select enrollment.Id).Take(251).ToListAsync(token);
            if (enrollments.Count == 0 || enrollments.Count > 250)
                return Error<InvoiceBatchResultDto>("Batch has no eligible students or exceeds 250; use smaller billing batches.", 409);
            var previous = await _invoices.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && enrollments.Contains(x.StudentEnrollmentId) &&
                x.InvoiceDate >= monthStart && x.InvoiceDate < monthEnd && !x.IsDeleted &&
                x.State != InvoiceState.Cancelled).ToListAsync(token);
            var byEnrollment = previous.GroupBy(x => x.StudentEnrollmentId)
                .ToDictionary(x => x.Key, x => x.ToList());
            foreach (var enrollmentId in enrollments)
            {
                if (!byEnrollment.TryGetValue(enrollmentId, out var old)) continue;
                var key = PerEnrollmentKey(request.ClientRequestId, enrollmentId);
                if (old.Count != 1 || old[0].ClientRequestId != key ||
                    old[0].InvoiceDate != request.InvoiceDate || old[0].DueDate != request.DueDate)
                    return Error<InvoiceBatchResultDto>(
                        "An invoice already exists for a student in this billing month.", 409);
            }
            var discountLinks = await (from assignment in _discounts.GetQueryable().AsNoTracking()
                join discount in _discountRules.GetQueryable().AsNoTracking()
                    on new { assignment.TenantId, Id = assignment.DiscountRuleId }
                    equals new { discount.TenantId, discount.Id }
                where assignment.TenantId == tenant && enrollments.Contains(assignment.StudentEnrollmentId) &&
                    assignment.IsActive && !assignment.IsDeleted && discount.IsActive && !discount.IsDeleted &&
                    assignment.EffectiveFrom <= request.InvoiceDate &&
                    (!assignment.EffectiveTo.HasValue || assignment.EffectiveTo >= request.InvoiceDate) &&
                    (!discount.EffectiveFrom.HasValue || discount.EffectiveFrom <= request.InvoiceDate) &&
                    (!discount.EffectiveTo.HasValue || discount.EffectiveTo >= request.InvoiceDate)
                select new { assignment.StudentEnrollmentId, discount }).ToListAsync(token);
            var discounts = discountLinks.GroupBy(x => x.StudentEnrollmentId)
                .ToDictionary(x => x.Key, x => x.Select(v => v.discount).ToList());
            var now = _clock.GetUtcNow().UtcDateTime;
            var generated = 0; var existing = 0;
            // Updating the tenant's rowversion serializes concurrent billing of the same month,
            // including requests with different idempotency keys.
            var tenantGate = await _tenants.GetQueryable().FirstAsync(x =>
                x.Id == tenant && !x.IsDeleted, token);
            foreach (var enrollmentId in enrollments)
            {
                if (byEnrollment.ContainsKey(enrollmentId)) { existing++; continue; }
                var subtotal = lines.Sum(x => x.line.Amount);
                discounts.TryGetValue(enrollmentId, out var applied);
                var discount = CalculateDiscount(lines.Select(x =>
                    (x.line.FeeHeadId, x.line.Amount)).ToArray(), applied ?? new List<DiscountRule>());
                var publicId = Guid.NewGuid();
                var invoice = new StudentInvoice
                {
                    TenantId = tenant, PublicId = publicId,
                    ClientRequestId = PerEnrollmentKey(request.ClientRequestId, enrollmentId),
                    StudentEnrollmentId = enrollmentId,
                    InvoiceNumber = "INV-" + publicId.ToString("N").ToUpperInvariant(),
                    InvoiceDate = request.InvoiceDate, DueDate = request.DueDate,
                    Subtotal = subtotal, DiscountAmount = discount, TotalAmount = subtotal - discount,
                    DueAmount = subtotal - discount, PaidAmount = 0m, FineAmount = 0m,
                    CurrencyCode = structure.CurrencyCode, State = InvoiceState.Issued,
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _invoices.AddAsync(invoice);
                await _uow.SaveChangesAsync(token);
                var appliedSoFar = 0m;
                for (var i = 0; i < lines.Count; i++)
                {
                    var entry = lines[i];
                    var share = i == lines.Count - 1 ? discount - appliedSoFar :
                        decimal.Round(entry.line.Amount / subtotal * discount, 2);
                    appliedSoFar += share;
                    await _invoiceLines.AddAsync(new StudentInvoiceLine
                    {
                        TenantId = tenant, StudentInvoiceId = invoice.Id, FeeHeadId = entry.head.Id,
                        FeeHeadCodeSnapshot = entry.head.Code, FeeHeadNameSnapshot = entry.head.Name,
                        Description = entry.head.Name, Amount = entry.line.Amount,
                        DiscountAmount = share, NetAmount = entry.line.Amount - share,
                        CreatedAt = now, CreatedBy = _user.UserId
                    });
                }
                generated++;
            }
            tenantGate.UpdatedAt = now; tenantGate.UpdatedBy = _user.UserId; _tenants.Update(tenantGate);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<InvoiceBatchResultDto>.SuccessResponse(new InvoiceBatchResultDto
            {
                ClientRequestId = request.ClientRequestId, Generated = generated,
                Existing = existing, Failed = 0
            }, "Monthly invoices generated atomically.");
        }, ct);
    }

    public Task<ApiResponse<StudentPaymentDto>> CollectPaymentAsync(
        CreateStudentPaymentRequestDto request, CancellationToken ct = default)
    {
        if (!CanCollect()) return Task.FromResult(Error<StudentPaymentDto>("Fee collection access required.", 403));
        if (request == null || request.ClientRequestId == Guid.Empty || request.Amount <= 0m ||
            request.Amount != decimal.Round(request.Amount, 2) ||
            request.PaymentDate == default ||
            request.PaymentDate > DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime).AddDays(1) ||
            request.PaymentMethod != PaymentMethodType.Cash || request.BankAccountId.HasValue ||
            !string.IsNullOrWhiteSpace(request.ExternalReference) ||
            request.Allocations == null || request.Allocations.Count is < 1 or > 100 ||
            request.Allocations.Any(x => x.InvoiceReference == Guid.Empty ||
                x.Amount <= 0m || x.Amount != decimal.Round(x.Amount, 2)) ||
            request.Allocations.GroupBy(x => x.InvoiceReference).Any(g => g.Count() > 1) ||
            request.Allocations.Sum(x => x.Amount) != request.Amount)
            return Task.FromResult(Error<StudentPaymentDto>(
                "Cash collection requires valid date and allocations totalling the payment amount."));
        return WriteAsync("cash collection", async token =>
        {
            var tenant = _user.TenantId;
            var replay = await _payments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, token);
            if (replay != null)
            {
                var prior = await _allocations.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.StudentPaymentId == replay.Id && !x.IsDeleted)
                    .Join(_invoices.GetQueryable().AsNoTracking(),
                        x => x.StudentInvoiceId, i => i.Id,
                        (x, i) => new { i.PublicId, x.Amount, i.TenantId }).Where(x =>
                            x.TenantId == tenant).ToListAsync(token);
                var match = replay.Amount == request.Amount &&
                    replay.PaymentDate == request.PaymentDate &&
                    replay.PaymentMethod == PaymentMethodType.Cash &&
                    prior.Count == request.Allocations.Count &&
                    request.Allocations.All(x => prior.Any(y =>
                        y.PublicId == x.InvoiceReference && y.Amount == x.Amount));
                if (!match) return Error<StudentPaymentDto>("Request ID already used for different payment.", 409);
                return ApiResponse<StudentPaymentDto>.SuccessResponse(
                    (await PaymentDtosAsync(new[] { replay }, token))[0], "Payment already recorded.");
            }
            var refs = request.Allocations.Select(x => x.InvoiceReference).ToArray();
            var invoiceRows = await (from invoice in _invoices.GetQueryable()
                join enrollment in _enrollments.GetQueryable().AsNoTracking()
                    on new { invoice.TenantId, Id = invoice.StudentEnrollmentId }
                    equals new { enrollment.TenantId, enrollment.Id }
                where invoice.TenantId == tenant && refs.Contains(invoice.PublicId) &&
                    !invoice.IsDeleted && !enrollment.IsDeleted
                select new { invoice, enrollment.StudentId }).ToListAsync(token);
            if (invoiceRows.Count != refs.Length || invoiceRows.Select(x => x.StudentId).Distinct().Count() != 1 ||
                invoiceRows.Select(x => x.invoice.CurrencyCode).Distinct().Count() != 1)
                return Error<StudentPaymentDto>("All invoices must belong to one student and currency.", 409);
            foreach (var allocation in request.Allocations)
            {
                var invoice = invoiceRows.First(x => x.invoice.PublicId == allocation.InvoiceReference).invoice;
                if (invoice.State is not (InvoiceState.Issued or InvoiceState.PartiallyPaid) ||
                    invoice.DueAmount < allocation.Amount ||
                    invoice.TotalAmount != invoice.PaidAmount + invoice.DueAmount)
                    return Error<StudentPaymentDto>("Invoice is closed, inconsistent or has insufficient due.", 409);
            }
            var now = _clock.GetUtcNow().UtcDateTime;
            var paymentId = Guid.NewGuid();
            var payment = new StudentPayment
            {
                TenantId = tenant, PublicId = paymentId,
                ClientRequestId = request.ClientRequestId,
                StudentId = invoiceRows[0].StudentId,
                ReceiptNumber = "RCP-" + paymentId.ToString("N").ToUpperInvariant(),
                PaymentDate = request.PaymentDate, PaymentMethod = PaymentMethodType.Cash,
                Amount = request.Amount, CurrencyCode = invoiceRows[0].invoice.CurrencyCode,
                State = PaymentState.Successful, InitiatedAt = now, CompletedAt = now,
                ReceivedByUserId = _user.UserId, CreatedAt = now, CreatedBy = _user.UserId
            };
            await _payments.AddAsync(payment);
            await _uow.SaveChangesAsync(token);
            foreach (var allocation in request.Allocations)
            {
                var row = invoiceRows.First(x => x.invoice.PublicId == allocation.InvoiceReference).invoice;
                await _allocations.AddAsync(new PaymentAllocation
                {
                    TenantId = tenant, StudentPaymentId = payment.Id,
                    StudentInvoiceId = row.Id, Amount = allocation.Amount,
                    CreatedAt = now, CreatedBy = _user.UserId
                });
                row.PaidAmount += allocation.Amount; row.DueAmount -= allocation.Amount;
                row.State = row.DueAmount == 0m ? InvoiceState.Paid : InvoiceState.PartiallyPaid;
                if (row.State == InvoiceState.Paid) row.PaidAt = now;
                row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
                _invoices.Update(row);
            }
            await _uow.SaveChangesAsync(token);
            var dto = (await PaymentDtosAsync(new[] { payment }, token))[0];
            return ApiResponse<StudentPaymentDto>.SuccessResponse(dto, "Cash payment allocated atomically.");
        }, ct);
    }

    public async Task<ApiResponse<StudentLedgerDto>> GetStudentLedgerAsync(
        Guid studentReference, CancellationToken ct = default)
    {
        if (!CanManage()) return Error<StudentLedgerDto>("Finance ledger access required.", 403);
        var student = await StudentAsync(studentReference, ct);
        if (student == null) return Error<StudentLedgerDto>("Student not found.", 404);
        var enrollmentIds = _enrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.StudentId == student.Id && !x.IsDeleted)
            .Select(x => x.Id);
        var totals = await _invoices.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && !x.IsDeleted &&
            enrollmentIds.Contains(x.StudentEnrollmentId) &&
            x.State != InvoiceState.Cancelled && x.State != InvoiceState.Refunded)
            .GroupBy(x => x.TenantId).Select(g => new
            {
                Billed = g.Sum(x => x.TotalAmount), Paid = g.Sum(x => x.PaidAmount),
                Due = g.Sum(x => x.DueAmount)
            }).FirstOrDefaultAsync(ct);
        return ApiResponse<StudentLedgerDto>.SuccessResponse(new StudentLedgerDto
        {
            StudentReference = student.PublicId, StudentCode = student.StudentCode,
            StudentName = student.FullName, TotalBilled = totals?.Billed ?? 0m,
            TotalPaid = totals?.Paid ?? 0m, TotalDue = totals?.Due ?? 0m
        });
    }

    public async Task<ApiResponse<PagedResult<StudentInvoiceDto>>> GetStudentInvoicesAsync(
        Guid studentReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage()) return Error<PagedResult<StudentInvoiceDto>>("Finance ledger access required.", 403);
        if (!ValidPage(page, pageSize)) return Error<PagedResult<StudentInvoiceDto>>("Page size must be 1–100.");
        var student = await StudentAsync(studentReference, ct);
        if (student == null) return Error<PagedResult<StudentInvoiceDto>>("Student not found.", 404);
        var ids = _enrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.StudentId == student.Id && !x.IsDeleted).Select(x => x.Id);
        var query = _invoices.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && ids.Contains(x.StudentEnrollmentId) && !x.IsDeleted);
        var total = await query.CountAsync(ct);
        var skip = Skip(page, pageSize);
        if (skip < 0) return Error<PagedResult<StudentInvoiceDto>>("Page exceeds limit.");
        var selected = await query.OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.Id)
            .Skip(skip).Take(pageSize).Select(x => x.Id).ToListAsync(ct);
        var items = await InvoiceDtosAsync(selected, ct);
        return ApiResponse<PagedResult<StudentInvoiceDto>>.SuccessResponse(
            new PagedResult<StudentInvoiceDto> { Page = page, PageSize = pageSize, TotalCount = total, Items = items });
    }

    public async Task<ApiResponse<PagedResult<StudentPaymentDto>>> GetStudentPaymentsAsync(
        Guid studentReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage()) return Error<PagedResult<StudentPaymentDto>>("Finance ledger access required.", 403);
        if (!ValidPage(page, pageSize)) return Error<PagedResult<StudentPaymentDto>>("Page size must be 1–100.");
        var student = await StudentAsync(studentReference, ct);
        if (student == null) return Error<PagedResult<StudentPaymentDto>>("Student not found.", 404);
        var query = _payments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.StudentId == student.Id && !x.IsDeleted);
        var count = await query.CountAsync(ct);
        var skip = Skip(page, pageSize);
        if (skip < 0) return Error<PagedResult<StudentPaymentDto>>("Page exceeds limit.");
        var rows = await query.OrderByDescending(x => x.PaymentDate).ThenByDescending(x => x.Id)
            .Skip(skip).Take(pageSize).ToListAsync(ct);
        var dtos = await PaymentDtosAsync(rows, ct);
        return ApiResponse<PagedResult<StudentPaymentDto>>.SuccessResponse(
            new PagedResult<StudentPaymentDto> { Page = page, PageSize = pageSize, TotalCount = count, Items = dtos });
    }

    private async Task<List<StudentInvoiceDto>> InvoiceDtosAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var tenant = _user.TenantId;
        var rows = await (from invoice in _invoices.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on new { invoice.TenantId, Id = invoice.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in _students.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where invoice.TenantId == tenant && ids.Contains(invoice.Id) && !invoice.IsDeleted
            select new { invoice, enrollment, student }).ToListAsync(ct);
        var lines = await _invoiceLines.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && ids.Contains(x.StudentInvoiceId) && !x.IsDeleted)
            .OrderBy(x => x.Id).ToListAsync(ct);
        var byInvoice = lines.GroupBy(x => x.StudentInvoiceId).ToDictionary(x => x.Key, x =>
            (IReadOnlyList<StudentInvoiceLineDto>)x.Select(line => new StudentInvoiceLineDto
            {
                Id = line.Id, FeeHeadId = line.FeeHeadId,
                FeeHeadName = line.FeeHeadNameSnapshot ?? "", Description = line.Description,
                Amount = line.Amount, DiscountAmount = line.DiscountAmount,
                FineAmount = line.FineAmount, NetAmount = line.NetAmount
            }).ToList());
        var byId = rows.ToDictionary(x => x.invoice.Id, x => new StudentInvoiceDto
        {
            Id = x.invoice.Id, Reference = x.invoice.PublicId,
            StudentEnrollmentReference = x.enrollment.PublicId, StudentReference = x.student.PublicId,
            StudentCode = x.student.StudentCode, StudentName = x.student.FullName,
            InvoiceNumber = x.invoice.InvoiceNumber, InvoiceDate = x.invoice.InvoiceDate,
            DueDate = x.invoice.DueDate, Subtotal = x.invoice.Subtotal,
            DiscountAmount = x.invoice.DiscountAmount, FineAmount = x.invoice.FineAmount,
            TotalAmount = x.invoice.TotalAmount, PaidAmount = x.invoice.PaidAmount,
            DueAmount = x.invoice.DueAmount, CurrencyCode = x.invoice.CurrencyCode,
            State = x.invoice.State, PaidAt = x.invoice.PaidAt,
            Lines = byInvoice.GetValueOrDefault(x.invoice.Id) ?? [],
            RowVersion = Version(x.invoice.RowVersion)
        });
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    private async Task<List<StudentPaymentDto>> PaymentDtosAsync(
        IReadOnlyCollection<StudentPayment> payments, CancellationToken ct)
    {
        if (payments.Count == 0) return [];
        var tenant = _user.TenantId;
        var paymentIds = payments.Select(x => x.Id).ToArray();
        var allocations = await (from allocation in _allocations.GetQueryable().AsNoTracking()
            join invoice in _invoices.GetQueryable().AsNoTracking()
                on new { allocation.TenantId, Id = allocation.StudentInvoiceId }
                equals new { invoice.TenantId, invoice.Id }
            where allocation.TenantId == tenant && paymentIds.Contains(allocation.StudentPaymentId) &&
                !allocation.IsDeleted && !invoice.IsDeleted
            select new { allocation, invoice.PublicId, invoice.InvoiceNumber }).ToListAsync(ct);
        var byPayment = allocations.GroupBy(x => x.allocation.StudentPaymentId).ToDictionary(x => x.Key,
            x => (IReadOnlyList<PaymentAllocationDto>)x.Select(v => new PaymentAllocationDto
            {
                Id = v.allocation.Id, InvoiceReference = v.PublicId,
                InvoiceNumber = v.InvoiceNumber, Amount = v.allocation.Amount
            }).ToList());
        return payments.Select(x => new StudentPaymentDto
        {
            Id = x.Id, Reference = x.PublicId, ReceiptNumber = x.ReceiptNumber,
            PaymentDate = x.PaymentDate, PaymentMethod = x.PaymentMethod, Amount = x.Amount,
            CurrencyCode = x.CurrencyCode, State = x.State,
            BankAccountId = x.BankAccountId, ExternalReference = x.ExternalReference,
            ReceivedByUserId = x.ReceivedByUserId, JournalId = x.JournalId,
            RowVersion = Version(x.RowVersion),
            Allocations = byPayment.GetValueOrDefault(x.Id) ?? []
        }).ToList();
    }

    private async Task<FeeStructureDto> MapStructureAsync(FeeStructure structure, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var lines = await (from line in _structureLines.GetQueryable().AsNoTracking()
            join head in _heads.GetQueryable().AsNoTracking()
                on new { line.TenantId, Id = line.FeeHeadId }
                equals new { head.TenantId, head.Id }
            where line.TenantId == tenant && line.FeeStructureId == structure.Id && !line.IsDeleted
            select new { line, head.Name }).ToListAsync(ct);
        var campus = await _campuses.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Id == structure.CampusId)
            .Select(x => x.Name).FirstOrDefaultAsync(ct);
        var year = await _years.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Id == structure.AcademicYearId)
            .Select(x => x.Name).FirstOrDefaultAsync(ct);
        return new FeeStructureDto
        {
            Id = structure.Id, CampusId = structure.CampusId, CampusName = campus ?? "",
            AcademicYearId = structure.AcademicYearId, AcademicYearName = year ?? "",
            AcademicProgramId = structure.AcademicProgramId,
            AcademicLevelId = structure.AcademicLevelId, AcademicBatchId = structure.AcademicBatchId,
            Name = structure.Name, CurrencyCode = structure.CurrencyCode,
            EffectiveFrom = structure.EffectiveFrom, EffectiveTo = structure.EffectiveTo,
            IsActive = structure.IsActive, RowVersion = Version(structure.RowVersion),
            Lines = lines.Select(x => new FeeStructureLineDto
            {
                Id = x.line.Id, FeeHeadId = x.line.FeeHeadId, FeeHeadName = x.Name,
                Frequency = x.line.Frequency, Amount = x.line.Amount,
                DueDayOfMonth = x.line.DueDayOfMonth, IsMandatory = x.line.IsMandatory,
                RowVersion = Version(x.line.RowVersion)
            }).ToList()
        };
    }

    private Task<Student?> StudentAsync(Guid reference, CancellationToken ct) =>
        reference == Guid.Empty ? Task.FromResult<Student?>(null) :
        _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);

    private async Task<ApiResponse<T>> WriteAsync<T>(
        string action, Func<CancellationToken, Task<ApiResponse<T>>> callback, CancellationToken ct)
    {
        try { return await _uow.ExecuteInTransactionAsync(callback, ct); }
        catch (DbUpdateConcurrencyException) { return Error<T>("A record changed concurrently; reload.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Finance conflict for action {Action} tenant {TenantId}",
                action, _user.TenantId);
            return Error<T>("Finance request conflicts with a prior transaction.", 409);
        }
    }

    private static decimal CalculateDiscount((long HeadId, decimal Amount)[] lines, List<DiscountRule> rules)
    {
        var total = lines.Sum(x => x.Amount);
        decimal discount = 0m;
        foreach (var rule in rules)
        {
            var basis = rule.FeeHeadId.HasValue ?
                lines.Where(x => x.HeadId == rule.FeeHeadId).Sum(x => x.Amount) : total;
            if (basis <= 0) continue;
            var amount = rule.IsPercentage ? basis * Math.Clamp(rule.Value, 0m, 100m) / 100m :
                Math.Max(0m, rule.Value);
            discount += Math.Min(basis, decimal.Round(amount, 2));
        }
        return Math.Min(total, discount);
    }
    private static Guid PerEnrollmentKey(Guid clientId, long enrollmentId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(clientId.ToString("N") + "|" + enrollmentId));
        return new Guid(hash.AsSpan(0, 16));
    }
    private static bool ValidPage(int page, int size) => page >= 1 && size is >= 1 and <= 100;
    private static int Skip(int page, int size)
    {
        var skip = (long)(page - 1) * size;
        return skip > int.MaxValue ? -1 : (int)skip;
    }
    private static string Version(byte[] bytes) => Convert.ToBase64String(bytes);
    private static bool Match(byte[] current, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return false;
        try
        {
            var expected = Convert.FromBase64String(encoded);
            return current != null && current.Length > 0 && current.Length == expected.Length &&
                CryptographicOperations.FixedTimeEquals(current, expected);
        }
        catch (FormatException) { return false; }
    }
    private bool CanManage() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("Accountant"));
    private bool CanCollect() => CanManage() || _user.IsAuthenticated && _user.TenantId > 0 &&
        _user.IsInRole("Cashier");
    private static ApiResponse<T> Error<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
}
