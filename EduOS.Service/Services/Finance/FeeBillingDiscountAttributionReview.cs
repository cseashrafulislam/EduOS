using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FeeBillingService
{
    /// <summary>
    /// Bounded historical fee-head attribution triage. The current discount-rule
    /// table is mutable, so this intentionally reports candidates, never proven
    /// historic entitlements or amounts to reverse.
    /// </summary>
    public async Task<ApiResponse<FeeDiscountAttributionReviewReportDto>> GetDiscountAttributionReviewAsync(
        FeeDiscountAttributionReviewQueryDto request, CancellationToken ct = default)
    {
        if (!CanManage() || _uow is not EduOSDbContext db ||
            db.CurrentTenantId != _user.TenantId || !_user.IsAuthenticated || _user.TenantId <= 0)
            return Error<FeeDiscountAttributionReviewReportDto>("Finance access required.", 403);
        if (request == null || request.CampusId <= 0 ||
            request.FromDate.Year is < 2000 or > 2199 ||
            request.ToDate < request.FromDate ||
            request.ToDate.DayNumber - request.FromDate.DayNumber > 89 ||
            request.Page < 1 || request.PageSize is < 1 or > 100 ||
            (long)(request.Page - 1) * request.PageSize > int.MaxValue)
            return Error<FeeDiscountAttributionReviewReportDto>(
                "Choose a campus, a period of up to 90 days and page size 1–100.");
        if (!await CanAccessBillingCampusAsync(request.CampusId, ct))
            return Error<FeeDiscountAttributionReviewReportDto>("Campus unavailable.", 404);
        var tenant = _user.TenantId;
        var campus = await _campuses.GetQueryable().AsNoTracking()
            .Where(c => c.TenantId == tenant && c.Id == request.CampusId && c.IsActive && !c.IsDeleted)
            .Select(c => c.Name).FirstOrDefaultAsync(ct);
        if (campus == null)
            return Error<FeeDiscountAttributionReviewReportDto>("Campus unavailable.", 404);

        // All scope predicates are translated to SQL before paging. No cross-campus
        // student, invoice, rule or award can be returned by guessed identifiers.
        var scoped = from invoice in _invoices.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on new { invoice.TenantId, Id = invoice.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in _students.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where invoice.TenantId == tenant && !invoice.IsDeleted &&
                !enrollment.IsDeleted && !student.IsDeleted &&
                enrollment.CampusId == request.CampusId &&
                invoice.InvoiceDate >= request.FromDate &&
                invoice.InvoiceDate <= request.ToDate &&
                invoice.State != InvoiceState.Cancelled
            select new { invoice.Id, invoice.PublicId, invoice.InvoiceNumber,
                invoice.InvoiceDate, invoice.CurrencyCode, invoice.Subtotal,
                invoice.DiscountAmount, invoice.FineAmount, invoice.TotalAmount,
                invoice.PaidAmount, invoice.DueAmount, invoice.JournalId,
                enrollmentId = enrollment.Id, student.StudentCode };
        var total = await scoped.CountAsync(ct);
        var invoices = await scoped.OrderBy(x => x.InvoiceDate).ThenBy(x => x.Id)
            .Skip((int)((long)(request.Page - 1) * request.PageSize))
            .Take(request.PageSize).ToListAsync(ct);
        var invoiceIds = invoices.Select(x => x.Id).ToArray();
        var enrollmentIds = invoices.Select(x => x.enrollmentId).Distinct().ToArray();
        var lines = await db.StudentInvoiceLines.AsNoTracking().Where(l =>
            l.TenantId == tenant && invoiceIds.Contains(l.StudentInvoiceId) && !l.IsDeleted)
            .Select(l => new { l.StudentInvoiceId, l.FeeHeadId, l.Amount,
                l.DiscountAmount, l.FineAmount, l.NetAmount })
            .Take(10001).ToListAsync(ct);
        if (lines.Count > 10000)
            return Error<FeeDiscountAttributionReviewReportDto>("Invoice line evidence exceeds the bounded report.", 422);
        var awards = await (from award in db.StudentDiscounts.AsNoTracking()
            join rule in db.DiscountRules.AsNoTracking()
                on new { award.TenantId, Id = award.DiscountRuleId }
                equals new { rule.TenantId, rule.Id }
            where award.TenantId == tenant && !award.IsDeleted && !rule.IsDeleted &&
                enrollmentIds.Contains(award.StudentEnrollmentId)
            select new { award.StudentEnrollmentId, award.EffectiveFrom, award.EffectiveTo,
                RuleFrom = rule.EffectiveFrom, RuleTo = rule.EffectiveTo,
                rule.FeeHeadId }).Take(10001).ToListAsync(ct);
        if (awards.Count > 10000)
            return Error<FeeDiscountAttributionReviewReportDto>("Award evidence exceeds the bounded report.", 422);
        var linesByInvoice = lines.GroupBy(l => l.StudentInvoiceId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var awardsByEnrollment = awards.GroupBy(a => a.StudentEnrollmentId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var rows = new List<FeeDiscountAttributionReviewRowDto>(invoices.Count);
        foreach (var invoice in invoices)
        {
            linesByInvoice.TryGetValue(invoice.Id, out var invoiceLines);
            invoiceLines ??= [];
            awardsByEnrollment.TryGetValue(invoice.enrollmentId, out var knownAwards);
            var active = (knownAwards ?? []).Where(a =>
                a.EffectiveFrom <= invoice.InvoiceDate &&
                (!a.EffectiveTo.HasValue || a.EffectiveTo >= invoice.InvoiceDate) &&
                (!a.RuleFrom.HasValue || a.RuleFrom <= invoice.InvoiceDate) &&
                (!a.RuleTo.HasValue || a.RuleTo >= invoice.InvoiceDate)).ToArray();
            var integrityIssue = invoiceLines.Length == 0 ||
                invoiceLines.Any(l => l.Amount < 0m || l.DiscountAmount < 0m ||
                    l.FineAmount < 0m || l.NetAmount < 0m ||
                    l.DiscountAmount > l.Amount ||
                    l.NetAmount != l.Amount - l.DiscountAmount + l.FineAmount) ||
                invoice.Subtotal != invoiceLines.Sum(l => l.Amount) ||
                invoice.DiscountAmount != invoiceLines.Sum(l => l.DiscountAmount) ||
                invoice.FineAmount != invoiceLines.Sum(l => l.FineAmount) ||
                invoice.TotalAmount != invoiceLines.Sum(l => l.NetAmount) ||
                invoice.TotalAmount != invoice.Subtotal - invoice.DiscountAmount + invoice.FineAmount ||
                invoice.PaidAmount < 0m || invoice.DueAmount < 0m ||
                invoice.PaidAmount + invoice.DueAmount != invoice.TotalAmount;
            var scopedHeads = active.Where(a => a.FeeHeadId.HasValue)
                .Select(a => a.FeeHeadId!.Value).ToHashSet();
            var hasGlobal = active.Any(a => !a.FeeHeadId.HasValue);
            // This is only a candidate signal: old mutable rule records are NOT
            // authoritative historical snapshots. Never label an invoice overcharged.
            var candidate = !hasGlobal && scopedHeads.Count > 0
                ? invoiceLines.Where(l => !scopedHeads.Contains(l.FeeHeadId))
                    .Sum(l => l.DiscountAmount)
                : 0m;
            var status = integrityIssue ? "Header/line integrity exception" :
                invoice.DiscountAmount == 0m ? "No discount recorded" :
                active.Length == 0 ? "Historical award evidence unavailable" :
                hasGlobal ? "Global/mixed awards: manual review" :
                candidate > 0m ? "Possible cross-head attribution" :
                "No cross-head signal in surviving metadata";
            rows.Add(new FeeDiscountAttributionReviewRowDto
            {
                InvoiceReference = invoice.PublicId, InvoiceNumber = invoice.InvoiceNumber,
                StudentCode = invoice.StudentCode, InvoiceDate = invoice.InvoiceDate,
                CurrencyCode = invoice.CurrencyCode, Subtotal = invoice.Subtotal,
                DiscountAmount = invoice.DiscountAmount, TotalAmount = invoice.TotalAmount,
                PotentiallyMisattributedDiscount = candidate,
                HasJournalLink = invoice.JournalId.HasValue,
                NeedsReview = integrityIssue || invoice.DiscountAmount > 0m &&
                    (active.Length == 0 || hasGlobal || candidate > 0m),
                EvidenceStatus = status
            });
        }
        return ApiResponse<FeeDiscountAttributionReviewReportDto>.SuccessResponse(
            new FeeDiscountAttributionReviewReportDto
            {
                CampusId = request.CampusId, CampusName = campus,
                FromDate = request.FromDate, ToDate = request.ToDate,
                ReviewRowsOnPage = rows.Count(r => r.NeedsReview),
                // Only same-currency sums are meaningful. Suppress total when mixed.
                PotentialAmountOnPage = rows.Select(r => r.CurrencyCode).Distinct().Count() <= 1
                    ? rows.Sum(r => r.PotentiallyMisattributedDiscount) : 0m,
                Invoices = new PagedResult<FeeDiscountAttributionReviewRowDto>
                {
                    Page = request.Page, PageSize = request.PageSize,
                    TotalCount = total, Items = rows
                }
            });
    }
}
