using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FeeBillingService
{
    // Preview one bounded monthly invoice batch without mutating invoices, ledger or audit.
    // This is not a reservation: the posting endpoint repeats its transactional checks.
    public async Task<ApiResponse<InvoiceBatchPreflightDto>> PreviewInvoiceBatchAsync(
        GenerateStudentInvoiceBatchRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage())
            return Error<InvoiceBatchPreflightDto>("Finance management access required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.AcademicBatchId <= 0 || request.FeeStructureId <= 0 ||
            request.Year is < 2000 or > 2200 || request.Month is < 1 or > 12 ||
            request.InvoiceDate == default || request.InvoiceDate.Year != request.Year ||
            request.InvoiceDate.Month != request.Month || request.DueDate < request.InvoiceDate)
            return Error<InvoiceBatchPreflightDto>("Invalid invoice period, dates or request ID.", 400);

        var tenant = _user.TenantId;
        var monthStart = new DateOnly(request.Year, request.Month, 1);
        var monthEnd = monthStart.AddMonths(1);
        var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
            x.Id == request.AcademicBatchId, ct);
        var structure = await _structures.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
            x.Id == request.FeeStructureId && x.EffectiveFrom <= monthStart &&
            (!x.EffectiveTo.HasValue || x.EffectiveTo >= monthStart), ct);
        if (batch == null || structure == null || batch.CampusId != structure.CampusId ||
            batch.AcademicYearId != structure.AcademicYearId ||
            (structure.AcademicBatchId.HasValue && structure.AcademicBatchId != batch.Id) ||
            (structure.AcademicLevelId.HasValue && structure.AcademicLevelId != batch.AcademicLevelId) ||
            (structure.AcademicProgramId.HasValue && structure.AcademicProgramId != batch.AcademicProgramId) ||
            structure.CurrencyCode != "BDT")
            return Error<InvoiceBatchPreflightDto>("Active BDT fee structure does not match the academic batch.", 409);

        var lines = await (from line in _structureLines.GetQueryable().AsNoTracking()
            join head in _heads.GetQueryable().AsNoTracking()
                on new { line.TenantId, Id = line.FeeHeadId }
                equals new { head.TenantId, head.Id }
            where line.TenantId == tenant && !line.IsDeleted && head.IsActive &&
                !head.IsDeleted && line.FeeStructureId == structure.Id
            select new { line.FeeHeadId, line.Amount, line.Frequency })
            .ToListAsync(ct);
        if (lines.Count == 0 || lines.Count > 100 ||
            lines.Any(x => x.Amount < 0m || x.Frequency != FeeFrequencyType.Monthly) ||
            lines.Sum(x => x.Amount) <= 0m)
            return Error<InvoiceBatchPreflightDto>("Monthly billing requires active positive monthly fee lines.", 409);

        var enrollments = await (from enrollment in _enrollments.GetQueryable().AsNoTracking()
            join student in _students.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where enrollment.TenantId == tenant && !enrollment.IsDeleted &&
                enrollment.AcademicBatchId == batch.Id && enrollment.CampusId == batch.CampusId &&
                enrollment.AcademicYearId == batch.AcademicYearId &&
                enrollment.AcademicProgramId == batch.AcademicProgramId &&
                enrollment.AcademicLevelId == batch.AcademicLevelId && enrollment.IsCurrent &&
                enrollment.State == EnrollmentState.Active && !student.IsDeleted &&
                student.StatusCode == "Active"
            orderby enrollment.Id
            select new { EnrollmentId = enrollment.Id, student.StudentCode,
                StudentName = student.FullName })
            .Take(251).ToListAsync(ct);
        if (enrollments.Count is < 1 or > 250)
            return Error<InvoiceBatchPreflightDto>("No eligible students or more than 250; split the billing batch.", 422);

        var ids = enrollments.Select(x => x.EnrollmentId).ToArray();
        var keys = ids.Select(id => PerEnrollmentKey(request.ClientRequestId, id)).ToArray();
        // Include matching request IDs outside this month: a reused key cannot silently
        // claim a prior invoice from a different period.
        var prior = await _invoices.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted &&
            ((ids.Contains(x.StudentEnrollmentId) &&
              x.InvoiceDate >= monthStart && x.InvoiceDate < monthEnd &&
              x.State != InvoiceState.Cancelled) ||
              keys.Contains(x.ClientRequestId)))
            .Select(x => new { x.StudentEnrollmentId, x.ClientRequestId, x.InvoiceNumber,
                x.InvoiceDate, x.DueDate, x.State, x.Subtotal,
                x.DiscountAmount, x.TotalAmount })
            .ToListAsync(ct);
        var byEnrollment = prior.GroupBy(x => x.StudentEnrollmentId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var discounts = await (from award in _discounts.GetQueryable().AsNoTracking()
            join rule in _discountRules.GetQueryable().AsNoTracking()
                on new { award.TenantId, Id = award.DiscountRuleId }
                equals new { rule.TenantId, rule.Id }
            where award.TenantId == tenant && ids.Contains(award.StudentEnrollmentId) &&
                award.IsActive && !award.IsDeleted && rule.IsActive && !rule.IsDeleted &&
                award.EffectiveFrom <= request.InvoiceDate &&
                (!award.EffectiveTo.HasValue || award.EffectiveTo >= request.InvoiceDate) &&
                (!rule.EffectiveFrom.HasValue || rule.EffectiveFrom <= request.InvoiceDate) &&
                (!rule.EffectiveTo.HasValue || rule.EffectiveTo >= request.InvoiceDate)
            select new { award.StudentEnrollmentId, Rule = rule }).ToListAsync(ct);
        var rules = discounts.GroupBy(x => x.StudentEnrollmentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Rule).ToList());
        var amounts = lines.Select(x => (HeadId: x.FeeHeadId, x.Amount)).ToArray();
        var subtotal = amounts.Sum(x => x.Amount);
        var rows = new List<InvoiceBatchPreflightRowDto>(enrollments.Count);
        foreach (var student in enrollments)
        {
            var key = PerEnrollmentKey(request.ClientRequestId, student.EnrollmentId);
            byEnrollment.TryGetValue(student.EnrollmentId, out var old);
            var match = old != null && old.Length == 1 &&
                old[0].ClientRequestId == key &&
                old[0].InvoiceDate == request.InvoiceDate &&
                old[0].DueDate == request.DueDate &&
                old[0].State != InvoiceState.Cancelled;
            rules.TryGetValue(student.EnrollmentId, out var applied);
            var discount = CalculateDiscount(amounts, applied ?? new());
            rows.Add(new InvoiceBatchPreflightRowDto
            {
                StudentCode = student.StudentCode,
                StudentName = student.StudentName,
                Status = old == null ? "New" : match ? "Existing" : "Conflict",
                InvoiceNumber = match ? old![0].InvoiceNumber : null,
                Subtotal = match ? old![0].Subtotal : subtotal,
                Discount = match ? old![0].DiscountAmount : discount,
                Total = match ? old![0].TotalAmount : subtotal - discount
            });
        }
        // A reused request key attached to a different enrollment is a hard conflict.
        var knownIds = ids.ToHashSet();
        if (prior.Any(x => keys.Contains(x.ClientRequestId) && !knownIds.Contains(x.StudentEnrollmentId)))
            return Error<InvoiceBatchPreflightDto>("Request key already belongs to a different enrollment.", 409);
        var fresh = rows.Where(x => x.Status == "New").ToArray();
        return ApiResponse<InvoiceBatchPreflightDto>.SuccessResponse(new InvoiceBatchPreflightDto
        {
            ClientRequestId = request.ClientRequestId, CampusId = batch.CampusId,
            CurrencyCode = structure.CurrencyCode, EligibleCount = rows.Count,
            NewCount = fresh.Length, ExistingCount = rows.Count(x => x.Status == "Existing"),
            ConflictCount = rows.Count(x => x.Status == "Conflict"),
            NewSubtotal = fresh.Sum(x => x.Subtotal), NewDiscount = fresh.Sum(x => x.Discount),
            NewTotal = fresh.Sum(x => x.Total),
            CanGenerate = rows.All(x => x.Status != "Conflict"), Rows = rows
        });
    }
}
