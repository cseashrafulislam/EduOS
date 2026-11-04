using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Facilities;

public sealed partial class FacilityVendorServiceBillService
{
    private sealed class PayableSource
    {
        public long Id { get; set; }
        public long? JournalId { get; set; }
        public Guid Reference { get; set; }
        public string Kind { get; set; } = "";
    }

    /// <summary>
    /// Compare the independently computed, historical supplier subledger with
    /// the source-linked posted GL liability leg. Never re-post or adjust money.
    /// Each bounded page is explicitly a page-level control, not the full AP GL.
    /// </summary>
    public async Task<ApiResponse<FacilitySupplierAgingTotalsDto>> SupplierAgingWithGlAsync(
        DateOnly asOf, long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        // The aging source, linked settlements, returns and Finance GL must all
        // reflect the same read snapshot. A bounded (<=50 invoice) serializable
        // SQL transaction prevents an in-flight posting/reversal from producing
        // a false audit variance between these independently queried tables.
        // No transaction is started for EF's non-relational test provider.
        await using var snapshot = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var aging = await SupplierAgingAsync(asOf, campusId, page, pageSize, ct);
        if (!aging.Success || aging.Data == null)
            return Error<FacilitySupplierAgingTotalsDto>(aging.Message, aging.StatusCode);
        var result = aging.Data;
        var rows = result.Page.Items.ToArray();
        if (rows.Length == 0)
        {
            result.PostedGlPayableOnPage = 0m;
            result.SubledgerVsGlDifferenceOnPage = 0m;
            if (snapshot != null) await snapshot.CommitAsync(ct);
            return ApiResponse<FacilitySupplierAgingTotalsDto>.SuccessResponse(result);
        }
        var tenant = user.TenantId;
        var stockRefs = rows.Where(x => x.InvoiceSource == "Stock").Select(x => x.InvoiceReference).ToArray();
        var serviceRefs = rows.Where(x => x.InvoiceSource == "Service").Select(x => x.InvoiceReference).ToArray();
        var stock = await db.ProcurementSupplierInvoices.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && stockRefs.Contains(x.PublicId))
            .Select(x => new PayableSource { Id=x.Id, JournalId=x.JournalId, Reference=x.PublicId, Kind="Stock" }).ToListAsync(ct);
        var service = await db.FacilityVendorServiceBills.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && serviceRefs.Contains(x.PublicId))
            .Select(x => new PayableSource { Id=x.Id, JournalId=x.JournalId, Reference=x.PublicId, Kind="Service" }).ToListAsync(ct);
        var sources = stock.Concat(service).ToArray();
        var stockIds = stock.Select(x => x.Id).ToArray();
        var serviceIds = service.Select(x => x.Id).ToArray();
        var creditRecords = await db.ProcurementSupplierReturnCredits.AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && stockIds.Contains(x.SupplierInvoiceId) &&
            x.StateCode == "Posted" && x.ReturnDate <= asOf && x.JournalId.HasValue)
            .Select(x => new { x.Id, x.SupplierInvoiceId, x.JournalId, x.UnpaidCreditAmount }).ToListAsync(ct);
        var originalIds = sources.Where(x => x.JournalId.HasValue).Select(x => x.JournalId!.Value).ToArray();
        var creditIds = creditRecords.Where(x => x.JournalId.HasValue).Select(x => x.JournalId!.Value).ToArray();
        var related = await db.Journals.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted &&
            x.JournalDate <= asOf && (originalIds.Contains(x.Id) || creditIds.Contains(x.Id) ||
                x.SourceType == "ProcurementSupplierPayment" && x.SourceId.HasValue && stockIds.Contains(x.SourceId.Value) ||
                x.SourceType == "FacilityVendorServicePayment" && x.SourceId.HasValue && serviceIds.Contains(x.SourceId.Value)))
            .Take(5001).ToListAsync(ct);
        if (related.Count > 5000)
            return Error<FacilitySupplierAgingTotalsDto>("Too many payable GL journals on this page; narrow the query.", 422);
        var ids = related.Select(x => x.Id).ToArray();
        var reversals = await db.Journals.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted &&
            x.State == JournalState.Posted && x.JournalDate <= asOf &&
            x.ReversalOfJournalId.HasValue && ids.Contains(x.ReversalOfJournalId.Value))
            .Take(5001).ToListAsync(ct);
        if (reversals.Count > 5000)
            return Error<FacilitySupplierAgingTotalsDto>("Too many payable reversals on this page.", 422);
        var journalById = related.Concat(reversals).ToDictionary(x => x.Id);
        var allIds = journalById.Keys.ToArray();
        var lines = await db.JournalLines.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TenantId == tenant && allIds.Contains(x.JournalId)).Take(15001).ToListAsync(ct);
        if (lines.Count > 15000)
            return Error<FacilitySupplierAgingTotalsDto>("Too many payable GL lines on this page.", 422);
        var byJournal = lines.ToLookup(x => x.JournalId);
        var accountIds = lines.Select(x => x.AccountId).Distinct().ToArray();
        var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && accountIds.Contains(x.Id) && !x.IsGroup && x.AllowsPosting && x.CurrencyCode == "BDT")
            .ToDictionaryAsync(x => x.Id, x => x.AccountType, ct);

        foreach (var row in rows)
        {
            var source = sources.SingleOrDefault(x => x.Reference == row.InvoiceReference && x.Kind == row.InvoiceSource);
            if (source?.JournalId is not long originalId || !journalById.TryGetValue(originalId, out var original))
            {
                row.GlReconciliationStatus = "MissingOriginal";
                continue;
            }
            var originalLines = byJournal[originalId].ToArray();
            var payable = originalLines.Where(x => accounts.TryGetValue(x.AccountId, out var type) &&
                type == AccountType.Liability && x.CreditAmount == row.InvoiceAmount && x.DebitAmount == 0m).ToArray();
            if (payable.Length != 1 || original.State is not (JournalState.Posted or JournalState.Reversed) ||
                original.CurrencyCode != "BDT" || original.CampusId != row.CampusId ||
                original.SourceType != (row.InvoiceSource == "Stock" ? "ProcurementSupplierInvoice" : "FacilityVendorServiceBill") ||
                original.SourceId != source.Id)
            {
                row.GlReconciliationStatus = "InvalidOriginal";
                continue;
            }
            var payableId = payable[0].AccountId;
            row.PayableLedgerAccountId = payableId;
            var paymentType = row.InvoiceSource == "Stock" ? "ProcurementSupplierPayment" : "FacilityVendorServicePayment";
            var reversalType = row.InvoiceSource == "Stock" ? "ProcurementSupplierPaymentReversal" : "FacilityVendorServicePaymentReversal";
            var invoiceReversalType = row.InvoiceSource == "Stock" ? "ProcurementSupplierInvoiceReversal" : "FacilityVendorServiceBillReversal";
            var payments = related.Where(x => x.SourceType == paymentType && x.SourceId == source.Id &&
                x.State is JournalState.Posted or JournalState.Reversed).ToArray();
            var credits = row.InvoiceSource == "Stock" ? creditRecords.Where(x => x.SupplierInvoiceId == source.Id)
                .Select(x => (x.Id, x.JournalId, x.UnpaidCreditAmount)).ToArray() : Array.Empty<(long Id, long? JournalId, decimal UnpaidCreditAmount)>();
            var relevant = new List<Journal> { original };
            relevant.AddRange(payments);
            var mismatch = false;
            foreach (var credit in credits)
            {
                if (!credit.JournalId.HasValue || !journalById.TryGetValue(credit.JournalId.Value, out var cj) ||
                    cj.SourceType != "ProcurementSupplierReturnCredit" || cj.SourceId != credit.Id ||
                    cj.State != JournalState.Posted ||
                    byJournal[cj.Id].Where(x => x.AccountId == payableId).Sum(x => x.DebitAmount - x.CreditAmount) != credit.UnpaidCreditAmount)
                    mismatch = true;
                else relevant.Add(cj);
            }
            var relevantIds = relevant.Select(x => x.Id).ToHashSet();
            var contras = reversals.Where(x => x.ReversalOfJournalId.HasValue &&
                relevantIds.Contains(x.ReversalOfJournalId.Value)).ToArray();
            foreach (var contra in contras)
            {
                if (contra.ReversalOfJournalId == originalId && contra.SourceType != invoiceReversalType ||
                    payments.Any(x => x.Id == contra.ReversalOfJournalId) && contra.SourceType != reversalType ||
                    credits.Any(x => x.JournalId == contra.ReversalOfJournalId)) mismatch = true;
                relevant.Add(contra);
            }
            foreach (var journal in relevant)
            {
                var journalLines = byJournal[journal.Id].ToArray();
                if (journal.CurrencyCode != "BDT" || journal.CampusId != row.CampusId ||
                    journal.State is not (JournalState.Posted or JournalState.Reversed) ||
                    journalLines.Length < 2 || journalLines.Sum(x => x.DebitAmount) != journalLines.Sum(x => x.CreditAmount) ||
                    journalLines.Any(x => x.IsDeleted || !accounts.ContainsKey(x.AccountId) || x.DebitAmount < 0m ||
                        x.CreditAmount < 0m || (x.DebitAmount > 0m) == (x.CreditAmount > 0m))) mismatch = true;
            }
            // Fail closed on untracked posted payment reversals and extra payable accounts.
            if (contras.GroupBy(x => x.ReversalOfJournalId).Any(g => g.Count() > 1) ||
                relevant.Where(x => x.Id != originalId).Any(x =>
                    byJournal[x.Id].Any(l => accounts.TryGetValue(l.AccountId, out var type) &&
                        type == AccountType.Liability && l.AccountId != payableId))) mismatch = true;
            if (mismatch)
            {
                row.GlReconciliationStatus = "InvalidLinkedJournal";
                continue;
            }
            var balance = relevant.Sum(x => byJournal[x.Id].Where(l => l.AccountId == payableId)
                .Sum(l => l.CreditAmount - l.DebitAmount));
            row.PostedGlPayableBalance = balance;
            row.SubledgerVsGlDifference = row.OpenBalance - balance;
            row.GlReconciliationStatus = row.SubledgerVsGlDifference == 0m ? "Reconciled" : "Difference";
        }
        result.GlExceptionCountOnPage = rows.Count(x => x.GlReconciliationStatus != "Reconciled");
        if (rows.All(x => x.PostedGlPayableBalance.HasValue))
        {
            result.PostedGlPayableOnPage = rows.Sum(x => x.PostedGlPayableBalance!.Value);
            result.SubledgerVsGlDifferenceOnPage = rows.Sum(x => x.SubledgerVsGlDifference!.Value);
        }
        if (snapshot != null) await snapshot.CommitAsync(ct);
        return ApiResponse<FacilitySupplierAgingTotalsDto>.SuccessResponse(result);
    }
}
