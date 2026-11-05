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
        public decimal StockValue { get; set; }
        public decimal PriceVariance { get; set; }
    }

    private sealed class PayableCredit
    {
        public long Id { get; set; }
        public long SupplierInvoiceId { get; set; }
        public long? JournalId { get; set; }
        public long CampusId { get; set; }
        public long StockAccountId { get; set; }
        public long PayableAccountId { get; set; }
        public long? VarianceAccountId { get; set; }
        public long? RefundReceivableAccountId { get; set; }
        public decimal StockValue { get; set; }
        public decimal CreditAmount { get; set; }
        public decimal UnpaidCreditAmount { get; set; }
        public decimal RefundReceivableAmount { get; set; }
    }

    private static bool ExactReversal(JournalLine[] original, JournalLine[] contra) =>
        original.Length == contra.Length && original.OrderBy(x => x.LineNo)
            .Zip(contra.OrderBy(x => x.LineNo), (source, reverse) =>
                source.LineNo == reverse.LineNo && source.AccountId == reverse.AccountId &&
                source.CampusId == reverse.CampusId &&
                source.DebitAmount == reverse.CreditAmount &&
                source.CreditAmount == reverse.DebitAmount).All(x => x);

    private static bool TypedLine(JournalLine line, AccountType type,
        IReadOnlyDictionary<long, AccountType> accounts, decimal debit, decimal credit) =>
        accounts.TryGetValue(line.AccountId, out var actual) && actual == type &&
        line.DebitAmount == debit && line.CreditAmount == credit;

    private static bool ExactOriginal(PayableSource source, decimal total, JournalLine[] journal,
        IReadOnlyDictionary<long, AccountType> accounts)
    {
        var ordered = journal.OrderBy(x => x.LineNo).ToArray();
        if (total <= 0m || ordered.Length != (source.Kind == "Stock" && source.PriceVariance != 0m ? 3 : 2) ||
            ordered[0].LineNo != 1 || ordered[1].LineNo != 2 ||
            ordered[0].AccountId == ordered[1].AccountId ||
            !TypedLine(ordered[1], AccountType.Liability, accounts, 0m, total)) return false;
        if (source.Kind == "Service")
            return TypedLine(ordered[0], AccountType.Expense, accounts, total, 0m);
        if (source.StockValue <= 0m || source.PriceVariance != total - source.StockValue ||
            !TypedLine(ordered[0], AccountType.Asset, accounts, source.StockValue, 0m)) return false;
        if (ordered.Length == 2) return true;
        return ordered[2].LineNo == 3 && ordered[2].AccountId != ordered[0].AccountId &&
            ordered[2].AccountId != ordered[1].AccountId &&
            TypedLine(ordered[2], AccountType.Expense, accounts,
                Math.Max(source.PriceVariance, 0m), Math.Max(-source.PriceVariance, 0m));
    }

    private static bool ExactCredit(PayableCredit credit, JournalLine[] journal,
        IReadOnlyDictionary<long, AccountType> accounts, long payableId)
    {
        if (credit.CreditAmount <= 0m || credit.StockValue <= 0m ||
            credit.UnpaidCreditAmount < 0m || credit.RefundReceivableAmount < 0m ||
            credit.UnpaidCreditAmount + credit.RefundReceivableAmount != credit.CreditAmount ||
            credit.PayableAccountId != payableId) return false;
        var variance = credit.CreditAmount - credit.StockValue;
        var expected = new List<(long AccountId, AccountType Type, decimal Debit, decimal Credit)>();
        if (credit.UnpaidCreditAmount > 0m)
            expected.Add((payableId, AccountType.Liability, credit.UnpaidCreditAmount, 0m));
        if (credit.RefundReceivableAmount > 0m && credit.RefundReceivableAccountId.HasValue)
            expected.Add((credit.RefundReceivableAccountId.Value, AccountType.Asset, credit.RefundReceivableAmount, 0m));
        else if (credit.RefundReceivableAmount > 0m) return false;
        expected.Add((credit.StockAccountId, AccountType.Asset, 0m, credit.StockValue));
        if (variance != 0m && credit.VarianceAccountId.HasValue)
            expected.Add((credit.VarianceAccountId.Value, AccountType.Expense,
                Math.Max(-variance, 0m), Math.Max(variance, 0m)));
        else if (variance != 0m) return false;
        var ordered = journal.OrderBy(x => x.LineNo).ToArray();
        if (ordered.Length != expected.Count || expected.Select(x => x.AccountId).Distinct().Count() != expected.Count)
            return false;
        for (var i = 0; i < expected.Count; i++)
            if (ordered[i].LineNo != i + 1 || ordered[i].AccountId != expected[i].AccountId ||
                !TypedLine(ordered[i], expected[i].Type, accounts, expected[i].Debit, expected[i].Credit)) return false;
        return true;
    }

    /// <summary>
    /// Compare the independently computed, historical supplier subledger with
    /// the source-linked posted GL liability leg. Never re-post or adjust money.
    /// Each bounded page is explicitly a page-level control, not the full AP GL.
    /// </summary>
    public Task<ApiResponse<FacilitySupplierAgingTotalsDto>> SupplierAgingWithGlAsync(
        DateOnly asOf, long? campusId, int page, int pageSize, CancellationToken ct = default)
        => SupplierAgingWithGlCoreAsync(asOf, campusId, page, pageSize, null, ct);

    private async Task<ApiResponse<FacilitySupplierAgingTotalsDto>> SupplierAgingWithGlCoreAsync(
        DateOnly asOf, long? campusId, int page, int pageSize,
        int? snapshotTotalCount, CancellationToken ct, AgingCursor? cursor = null)
    {
        // The aging source, linked settlements, returns and Finance GL must all
        // reflect the same read snapshot. A bounded (<=50 invoice) serializable
        // SQL transaction prevents an in-flight posting/reversal from producing
        // a false audit variance between these independently queried tables.
        // No transaction is started for EF's non-relational test provider.
        await using var snapshot = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var aging = await SupplierAgingCoreAsync(asOf, campusId, page, pageSize,
            snapshotTotalCount, ct, cursor);
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
            .Select(x => new PayableSource { Id=x.Id, JournalId=x.JournalId, Reference=x.PublicId,
                Kind="Stock", StockValue=x.StockValue, PriceVariance=x.PriceVariance }).ToListAsync(ct);
        var service = await db.FacilityVendorServiceBills.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && serviceRefs.Contains(x.PublicId))
            .Select(x => new PayableSource { Id=x.Id, JournalId=x.JournalId, Reference=x.PublicId, Kind="Service" }).ToListAsync(ct);
        var sources = stock.Concat(service).ToArray();
        var stockIds = stock.Select(x => x.Id).ToArray();
        var serviceIds = service.Select(x => x.Id).ToArray();
        var creditRecords = await db.ProcurementSupplierReturnCredits.AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && stockIds.Contains(x.SupplierInvoiceId) &&
            x.StateCode == "Posted" && x.ReturnDate <= asOf && x.JournalId.HasValue)
            .Select(x => new PayableCredit { Id=x.Id, SupplierInvoiceId=x.SupplierInvoiceId,
                JournalId=x.JournalId, CampusId=x.CampusId, StockAccountId=x.StockAccountId,
                PayableAccountId=x.PayableAccountId, VarianceAccountId=x.VarianceAccountId,
                RefundReceivableAccountId=x.RefundReceivableAccountId, StockValue=x.StockValue,
                CreditAmount=x.CreditAmount, UnpaidCreditAmount=x.UnpaidCreditAmount,
                RefundReceivableAmount=x.RefundReceivableAmount }).ToListAsync(ct);
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
        // A cross-tenant line attached to a tenant journal must not be silently
        // excluded by the tenant filter and make a corrupt journal appear valid.
        var foreignLineIds = await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId != tenant && allIds.Contains(x.JournalId))
            .Select(x => x.JournalId).Distinct().ToArrayAsync(ct);
        var foreignLineJournals = foreignLineIds.ToHashSet();
        var accountIds = lines.Select(x => x.AccountId).Distinct().ToArray();
        var accounts = await db.Accounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant &&
            accountIds.Contains(x.Id) && x.CurrencyCode == "BDT")
            .ToDictionaryAsync(x => x.Id, x => x.AccountType, ct);
        // Bank designations are historical evidence: disabling a bank today
        // must not invalidate a correctly posted payment from an earlier period.
        var banks = await db.BankAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant &&
            accountIds.Contains(x.AccountId) && x.CurrencyCode == "BDT")
            .Select(x => new { x.AccountId, x.CampusId }).ToListAsync(ct);

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
            if (payable.Length != 1 || !ExactOriginal(source, row.InvoiceAmount, originalLines, accounts) ||
                original.ReversalOfJournalId.HasValue || foreignLineJournals.Contains(originalId) ||
                original.State is not (JournalState.Posted or JournalState.Reversed) ||
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
                .ToArray() : Array.Empty<PayableCredit>();
            var relevant = new List<Journal> { original };
            relevant.AddRange(payments);
            var mismatch = false;
            foreach (var credit in credits)
            {
                if (!credit.JournalId.HasValue || !journalById.TryGetValue(credit.JournalId.Value, out var cj) ||
                    cj.SourceType != "ProcurementSupplierReturnCredit" || cj.SourceId != credit.Id ||
                    cj.State != JournalState.Posted || cj.ReversalOfJournalId.HasValue ||
                    cj.CampusId != row.CampusId || credit.CampusId != row.CampusId ||
                    !ExactCredit(credit, byJournal[cj.Id].ToArray(), accounts, payableId))
                    mismatch = true;
                else relevant.Add(cj);
            }
            var relevantIds = relevant.Select(x => x.Id).ToHashSet();
            var contras = reversals.Where(x => x.ReversalOfJournalId.HasValue &&
                relevantIds.Contains(x.ReversalOfJournalId.Value)).ToArray();
            foreach (var contra in contras)
            {
                var reversed = relevant.FirstOrDefault(x => x.Id == contra.ReversalOfJournalId);
                if (reversed == null ||
                    contra.ReversalOfJournalId == originalId &&
                        (contra.SourceType != invoiceReversalType ||
                         contra.SourceId != (row.InvoiceSource == "Stock" ? source.Id : originalId)) ||
                    payments.Any(x => x.Id == contra.ReversalOfJournalId) &&
                        (contra.SourceType != reversalType || contra.SourceId != contra.ReversalOfJournalId) ||
                    credits.Any(x => x.JournalId == contra.ReversalOfJournalId) ||
                    reversed != null && !ExactReversal(byJournal[reversed.Id].ToArray(), byJournal[contra.Id].ToArray()))
                    mismatch = true;
                relevant.Add(contra);
            }
            foreach (var journal in relevant)
            {
                var journalLines = byJournal[journal.Id].ToArray();
                if (journal.CurrencyCode != "BDT" || journal.CampusId != row.CampusId ||
                    foreignLineJournals.Contains(journal.Id) ||
                    journal.State is not (JournalState.Posted or JournalState.Reversed) ||
                    journalLines.Length < 2 || journalLines.Sum(x => x.DebitAmount) != journalLines.Sum(x => x.CreditAmount) ||
                    journalLines.Any(x => x.IsDeleted || x.CampusId != row.CampusId ||
                        !accounts.ContainsKey(x.AccountId) || x.DebitAmount < 0m ||
                        x.CreditAmount < 0m || (x.DebitAmount > 0m) == (x.CreditAmount > 0m))) mismatch = true;
            }
            foreach (var payment in payments)
            {
                var paymentLines = byJournal[payment.Id].OrderBy(x => x.LineNo).ToArray();
                if (payment.ReversalOfJournalId.HasValue || paymentLines.Length != 2 ||
                    paymentLines[0].LineNo != 1 || paymentLines[1].LineNo != 2 ||
                    paymentLines[0].AccountId == paymentLines[1].AccountId ||
                    paymentLines[0].DebitAmount <= 0m ||
                    paymentLines[1].AccountId == payableId ||
                    !TypedLine(paymentLines[0], AccountType.Liability, accounts,
                        paymentLines[0].DebitAmount, 0m) || paymentLines[0].AccountId != payableId ||
                    !TypedLine(paymentLines[1], AccountType.Asset, accounts,
                        0m, paymentLines[0].DebitAmount) ||
                    !banks.Any(x => x.AccountId == paymentLines[1].AccountId &&
                        (!x.CampusId.HasValue || x.CampusId == row.CampusId))) mismatch = true;
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
