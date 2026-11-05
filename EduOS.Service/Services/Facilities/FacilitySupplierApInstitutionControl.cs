using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Facilities;

public sealed partial class FacilityVendorServiceBillService
{
    /// <summary>
    /// Audit-only full population control for a bounded institution/campus.
    /// Invoice-lineage and full liability-account GL are independent checks:
    /// manual and orphan journal amounts cannot disappear into a page subtotal.
    /// </summary>
    public async Task<ApiResponse<InstitutionalSupplierApControlDto>> SupplierApInstitutionControlAsync(
        DateOnly asOf, long? campusId, CancellationToken ct = default)
    {
        if (!ValidTenant || !user.IsTenantAdmin)
            return Error<InstitutionalSupplierApControlDto>("Institution administrator access required.", 403);
        if (asOf == default || asOf.Year is < 2000 or > 2100 || campusId is <= 0)
            return Error<InstitutionalSupplierApControlDto>("Valid as-of date and campus scope required.", 400);
        if (campusId.HasValue && !await CampusAllowed(campusId.Value, ct))
            return Error<InstitutionalSupplierApControlDto>("Campus access denied.", 403);
        await using var snapshot = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        const int batchSize = 50;
        var totals = new Dictionary<long, InstitutionalSupplierApControlAccountDto>();
        var count = -1;
        var processed = 0;
        var exceptions = 0;
        var subledgerTotal = 0m;
        for (var page = 1; count < 0 || processed < count; page++)
        {
            var response = await SupplierAgingWithGlAsync(asOf, campusId, page, batchSize, ct);
            if (!response.Success || response.Data == null)
                return Error<InstitutionalSupplierApControlDto>(response.Message, response.StatusCode);
            var result = response.Data;
            if (count < 0)
            {
                count = result.Page.TotalCount;
                // The control is an aggregate audit, not a paged display. Never
                // silently cap the source population: every source must be checked.
                // SQL remains the paging authority; only 50 rows are held per pass.
            }
            if (result.Page.TotalCount != count || result.Page.Items.Count == 0 && processed < count)
                return Error<InstitutionalSupplierApControlDto>("Supplier source population changed during reconciliation.", 409);
            foreach (var row in result.Page.Items)
            {
                processed++;
                subledgerTotal += row.OpenBalance;
                if (row.GlReconciliationStatus != "Reconciled") exceptions++;
                if (row.PayableLedgerAccountId is not long ledgerId) continue;
                if (!totals.TryGetValue(ledgerId, out var account))
                    totals.Add(ledgerId, account = new InstitutionalSupplierApControlAccountDto { AccountId = ledgerId });
                account.SourceCount++;
                account.SubledgerOutstanding += row.OpenBalance;
                if (row.PostedGlPayableBalance.HasValue)
                    account.LinkedPostedGlBalance += row.PostedGlPayableBalance.Value;
                if (row.GlReconciliationStatus != "Reconciled") account.ExceptionCount++;
            }
        }
        // Also discover GL accounts on supplier-origin journals where the source
        // document was deleted, damaged or not included in the historical aging.
        // Otherwise an orphan GL balance could be silently omitted.
        var supplierJournals = db.Journals.AsNoTracking().Where(j => j.TenantId == user.TenantId &&
            !j.IsDeleted && (j.State == JournalState.Posted || j.State == JournalState.Reversed) &&
            j.JournalDate <= asOf && (!campusId.HasValue || j.CampusId == campusId) &&
            (j.SourceType == "ProcurementSupplierInvoice" ||
             j.SourceType == "FacilityVendorServiceBill" ||
             j.SourceType == "ProcurementSupplierPayment" ||
             j.SourceType == "FacilityVendorServicePayment" ||
             j.SourceType == "ProcurementSupplierReturnCredit" ||
             j.SourceType == "ProcurementSupplierInvoiceReversal" ||
             j.SourceType == "FacilityVendorServiceBillReversal" ||
             j.SourceType == "ProcurementSupplierPaymentReversal" ||
             j.SourceType == "FacilityVendorServicePaymentReversal"));
        var discovered = await (from line in db.JournalLines.AsNoTracking()
            join journal in supplierJournals on line.JournalId equals journal.Id
            join account in db.Accounts.AsNoTracking() on line.AccountId equals account.Id
            where line.TenantId == user.TenantId && !line.IsDeleted &&
                account.TenantId == user.TenantId && !account.IsDeleted &&
                account.AccountType == AccountType.Liability
            select line.AccountId).Distinct().ToListAsync(ct);
        foreach (var id in discovered)
            if (!totals.ContainsKey(id)) totals.Add(id, new InstitutionalSupplierApControlAccountDto { AccountId = id });
        var ids = totals.Keys.ToArray();
        if (ids.Length > 0)
        {
            // This reads the actual entire GL balance for the control account,
            // including unmatched manual entries and journals outside AP modules.
            // Chunk only the SQL IN parameter list, never the audit population.
            // A single serializable snapshot protects every batch's consistency.
            var journals = db.Journals.AsNoTracking().Where(j => j.TenantId == user.TenantId &&
                !j.IsDeleted && (j.State == JournalState.Posted || j.State == JournalState.Reversed) &&
                j.JournalDate <= asOf && (!campusId.HasValue || j.CampusId == campusId));
            foreach (var slice in ids.Chunk(400))
            {
                var accounts = await db.Accounts.AsNoTracking().Where(a => a.TenantId == user.TenantId &&
                    !a.IsDeleted && slice.Contains(a.Id) && !a.IsGroup && a.AllowsPosting &&
                    a.AccountType == AccountType.Liability && a.CurrencyCode == "BDT")
                    .Select(a => new { a.Id, a.Code, a.Name }).ToListAsync(ct);
                if (accounts.Count != slice.Length)
                    return Error<InstitutionalSupplierApControlDto>("Supplier control ledger is missing or invalid.", 409);
                foreach (var a in accounts)
                {
                    totals[a.Id].AccountCode = a.Code;
                    totals[a.Id].AccountName = a.Name;
                }
                var gl = await (from line in db.JournalLines.AsNoTracking()
                    join j in journals on line.JournalId equals j.Id
                    where line.TenantId == user.TenantId && !line.IsDeleted && slice.Contains(line.AccountId)
                    group line by line.AccountId into g
                    select new { AccountId = g.Key, Balance = g.Sum(x => x.CreditAmount - x.DebitAmount) })
                    .ToListAsync(ct);
                foreach (var row in gl) totals[row.AccountId].ControlAccountGlBalance = row.Balance;
            }
        }
        var values = totals.Values.OrderBy(x => x.AccountCode).ThenBy(x => x.AccountId).ToArray();
        var totalGl = values.Sum(x => x.ControlAccountGlBalance);
        var responseDto = new InstitutionalSupplierApControlDto
        {
            AsOf = asOf, CampusId = campusId, InvoiceCount = count, InvalidSourceCount = exceptions,
            SubledgerOutstanding = subledgerTotal,
            LinkedPostedGlBalance = values.Sum(x => x.LinkedPostedGlBalance),
            ControlAccountGlBalance = totalGl,
            UnattributedGlBalance = values.Sum(x => x.UnattributedGlBalance),
            SubledgerVsControlDifference = subledgerTotal - totalGl,
            Reconciled = exceptions == 0 && values.All(x => x.Reconciled), Accounts = values
        };
        if (snapshot != null) await snapshot.CommitAsync(ct);
        return ApiResponse<InstitutionalSupplierApControlDto>.SuccessResponse(responseDto);
    }
}
