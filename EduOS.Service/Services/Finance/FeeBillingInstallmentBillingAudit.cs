using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FeeBillingService
{
    // Bounded, non-mutating month-by-month reconciliation. Never infer a posted
    // journal from a nullable JournalId; it is only a link to investigate.
    public async Task<ApiResponse<StudentFeeInstallmentBillingAuditDto>> GetInstallmentBillingAuditAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (!CanManage() || _uow is not EduOSDbContext db ||
            db.CurrentTenantId != _user.TenantId)
            return Error<StudentFeeInstallmentBillingAuditDto>("Finance access required.", 403);
        if (reference == Guid.Empty)
            return Error<StudentFeeInstallmentBillingAuditDto>("Invalid plan reference.");
        var tenant = _user.TenantId;
        var plan = await db.StudentFeeInstallmentPlans.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == reference && !x.IsDeleted, ct);
        if (plan == null || !await CanAccessBillingCampusAsync(plan.CampusId, ct))
            return Error<StudentFeeInstallmentBillingAuditDto>("Plan unavailable.", 404);
        var provenance = await InstallmentPlanDtoAsync(db, plan, false, ct);
        if (provenance == null)
            return Error<StudentFeeInstallmentBillingAuditDto>("Plan provenance unavailable.", 409);
        if (plan.StartMonth.Day != 1 || plan.EndMonth.Day != 1 ||
            plan.EndMonth < plan.StartMonth || plan.EndMonth.Year > 2200 ||
            plan.StartMonth.Year < 2000)
            return Error<StudentFeeInstallmentBillingAuditDto>("Plan dates are invalid.", 409);
        var monthCount = (plan.EndMonth.Year - plan.StartMonth.Year) * 12 +
            plan.EndMonth.Month - plan.StartMonth.Month + 1;
        if (monthCount is < 1 or > 12 || plan.LineCount is < 1 or > 600)
            return Error<StudentFeeInstallmentBillingAuditDto>("Plan exceeds bounded review limits.", 409);

        var months = Enumerable.Range(0, monthCount)
            .Select(i => plan.StartMonth.AddMonths(i)).ToArray();
        var keys = months.Select(m => InstallmentMonthKey(plan.PublicId, m)).ToArray();
        var endExclusive = plan.EndMonth.AddMonths(1);
        var frozen = await db.StudentFeeInstallmentPlanLines.AsNoTracking().Where(x =>
            x.TenantId == tenant && x.StudentFeeInstallmentPlanId == plan.Id && !x.IsDeleted)
            .OrderBy(x => x.MonthStart).ThenBy(x => x.FeeHeadId).Take(601)
            .Select(x => new { x.MonthStart, x.DueDate, x.FeeHeadId, x.GrossAmount })
            .ToListAsync(ct);
        var planIssues = new List<string>();
        if (frozen.Count != plan.LineCount || frozen.Sum(x => x.GrossAmount) != plan.GrossAmount)
            planIssues.Add("Frozen plan line count or gross total differs from the agreement.");
        if (frozen.Any(x => x.GrossAmount <= 0m ||
            x.GrossAmount != decimal.Round(x.GrossAmount, 2) ||
            x.MonthStart < plan.StartMonth || x.MonthStart > plan.EndMonth ||
            x.DueDate < x.MonthStart || x.DueDate >= x.MonthStart.AddMonths(1)) ||
            frozen.GroupBy(x => new { x.MonthStart, x.FeeHeadId }).Any(g => g.Count() != 1))
            planIssues.Add("Frozen fee-head amounts, due dates or uniqueness require review.");

        // Include competing month invoices and deterministic-key invoices, but never
        // return invoice details for a different enrollment or institution.
        var invoices = await db.StudentInvoices.AsNoTracking().Where(x =>
            x.TenantId == tenant && x.StudentEnrollmentId == plan.StudentEnrollmentId &&
            !x.IsDeleted &&
            ((x.InvoiceDate >= plan.StartMonth && x.InvoiceDate < endExclusive) ||
                keys.Contains(x.ClientRequestId)))
            .OrderBy(x => x.InvoiceDate).ThenBy(x => x.Id).Take(101)
            .ToListAsync(ct);
        if (invoices.Count > 100)
            return Error<StudentFeeInstallmentBillingAuditDto>("Too many competing invoices; escalate review.", 409);
        var foreignKeyCollision = await db.StudentInvoices.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && !x.IsDeleted &&
            x.StudentEnrollmentId != plan.StudentEnrollmentId &&
            keys.Contains(x.ClientRequestId), ct);
        if (foreignKeyCollision)
            planIssues.Add("A plan invoice key conflicts with another enrollment; no foreign details are shown.");

        var invoiceIds = invoices.Select(x => x.Id).ToArray();
        var actualLines = await db.StudentInvoiceLines.AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && invoiceIds.Contains(x.StudentInvoiceId))
            .Select(x => new { x.StudentInvoiceId, x.FeeHeadId, x.Amount,
                x.DiscountAmount, x.FineAmount, x.NetAmount }).Take(601).ToListAsync(ct);
        if (actualLines.Count > 600)
            return Error<StudentFeeInstallmentBillingAuditDto>("Invoice evidence exceeds review limits.", 409);
        var allocated = await db.PaymentAllocations.AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && invoiceIds.Contains(x.StudentInvoiceId))
            .GroupBy(x => x.StudentInvoiceId)
            .Select(g => new { InvoiceId = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Amount, ct);

        var rows = new List<StudentFeeInstallmentBillingAuditMonthDto>(monthCount);
        foreach (var month in months)
        {
            var expected = frozen.Where(x => x.MonthStart == month).ToList();
            var issues = new List<string>();
            if (expected.Count == 0)
                issues.Add("Frozen monthly fee lines are missing.");
            var gross = expected.Sum(x => x.GrossAmount);
            var key = InstallmentMonthKey(plan.PublicId, month);
            var candidates = invoices.Where(x =>
                x.ClientRequestId == key ||
                x.InvoiceDate >= month && x.InvoiceDate < month.AddMonths(1)).ToList();
            if (candidates.Count > 1)
                issues.Add("Multiple or competing invoice records exist for this month.");
            var invoice = candidates.Count == 1 ? candidates[0] : null;
            var row = new StudentFeeInstallmentBillingAuditMonthDto {
                MonthStart = month,
                ContractDueDate = expected.Count > 0 ? expected.Max(x => x.DueDate) : null,
                ContractGross = gross
            };
            if (invoice != null)
            {
                row.InvoiceReference = invoice.PublicId;
                row.InvoiceNumber = invoice.InvoiceNumber;
                row.InvoiceGross = invoice.Subtotal;
                row.InvoiceDiscount = invoice.DiscountAmount;
                row.InvoiceNet = invoice.TotalAmount;
                row.InvoicePaid = invoice.PaidAmount;
                row.InvoiceDue = invoice.DueAmount;
                row.JournalLinked = invoice.JournalId.HasValue;
                if (invoice.ClientRequestId != key || invoice.InvoiceDate != month ||
                    invoice.DueDate != row.ContractDueDate ||
                    invoice.CurrencyCode != plan.CurrencyCode ||
                    invoice.State == InvoiceState.Cancelled)
                    issues.Add("Invoice key, month, due date, currency or state does not match the plan.");
                if (invoice.Subtotal != gross || invoice.DiscountAmount < 0m ||
                    invoice.FineAmount != 0m ||
                    invoice.TotalAmount != invoice.Subtotal - invoice.DiscountAmount + invoice.FineAmount ||
                    invoice.PaidAmount < 0m || invoice.DueAmount < 0m ||
                    invoice.PaidAmount + invoice.DueAmount != invoice.TotalAmount)
                    issues.Add("Invoice header or paid/due balance does not reconcile.");
                var billedLines = actualLines.Where(x => x.StudentInvoiceId == invoice.Id).ToList();
                if (billedLines.Count != expected.Count ||
                    billedLines.Sum(x => x.Amount) != invoice.Subtotal ||
                    billedLines.Sum(x => x.DiscountAmount) != invoice.DiscountAmount ||
                    billedLines.Sum(x => x.NetAmount) != invoice.TotalAmount ||
                    billedLines.Any(x => x.Amount < 0m || x.DiscountAmount < 0m ||
                        x.FineAmount != 0m || x.NetAmount != x.Amount - x.DiscountAmount) ||
                    expected.Any(x => billedLines.Count(y =>
                        y.FeeHeadId == x.FeeHeadId && y.Amount == x.GrossAmount) != 1))
                    issues.Add("Invoice fee-head lines or discount attribution do not match frozen evidence.");
                if (allocated.GetValueOrDefault(invoice.Id) != invoice.PaidAmount)
                    issues.Add("Receipt allocations do not reconcile with invoice paid amount.");
            }
            if (provenance.StateCode != 3 && invoice != null)
                issues.Add("An invoice exists without an active approved agreement.");
            if (planIssues.Count > 0)
                issues.Add("Plan-level integrity review is required.");
            if (foreignKeyCollision)
                issues.Add("Invoice key collision requires independent investigation.");
            row.Status = issues.Count > 0 ? "Review" : invoice == null ? "Unbilled" : "Billed";
            row.Issues = issues;
            rows.Add(row);
        }
        var report = new StudentFeeInstallmentBillingAuditDto {
            PlanReference = plan.PublicId, StudentReference = provenance.StudentReference,
            StudentCode = provenance.StudentCode, CampusId = plan.CampusId,
            CurrencyCode = plan.CurrencyCode, PlanStateCode = provenance.StateCode,
            Months = monthCount, ContractGross = plan.GrossAmount,
            FrozenLineGross = frozen.Sum(x => x.GrossAmount),
            BilledMonths = rows.Count(x => x.Status == "Billed"),
            UnbilledMonths = rows.Count(x => x.Status == "Unbilled"),
            ReviewMonths = rows.Count(x => x.Status == "Review"),
            VerifiedInvoiceNet = rows.Where(x => x.Status == "Billed")
                .Sum(x => x.InvoiceNet ?? 0m),
            PlanIssues = planIssues, Rows = rows
        };
        return ApiResponse<StudentFeeInstallmentBillingAuditDto>.SuccessResponse(report);
    }
}
