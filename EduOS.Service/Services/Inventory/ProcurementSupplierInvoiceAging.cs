using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

public sealed partial class ProcurementSupplierInvoiceService
{
    /// <summary>Source-linked GL settlement aging. Never subtract unlinked/unchecked bank transactions.</summary>
    public async Task<ApiResponse<PagedResult<ProcurementSupplierPayableAgingRowDto>>> AgingAsync(
        DateOnly? dueDateReference, long? supplierId, bool overdueOnly, int page, int pageSize,
        CancellationToken ct = default)
    {
        if (!CanRead) return Err<PagedResult<ProcurementSupplierPayableAgingRowDto>>("Payable report access denied.",403);
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            supplierId is <= 0 || dueDateReference == default(DateOnly))
            return Err<PagedResult<ProcurementSupplierPayableAgingRowDto>>("Invalid report filters.");
        var reference=dueDateReference ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        // Do not silently drop a posted payable whose source journal has drifted.
        // Keep it visible to authorized Finance reconciliation as EvidenceConflict;
        // the CSV export must then refuse the entire report (409, no partial CSV).
        var query=Visible().Where(x=>x.StateCode=="Posted");
        if(supplierId.HasValue) query=query.Where(x=>x.SupplierId==supplierId.Value);
        if(overdueOnly) query=query.Where(x=>x.DueDate<reference);
        var count=await query.CountAsync(ct);
        var rows=await query.OrderBy(x=>x.DueDate).ThenBy(x=>x.Id)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(x=>new {
                x.Id,x.PublicId,x.SupplierInvoiceNumber,x.SupplierId,x.CampusId,x.DueDate,
                x.TotalAmount,x.StockValue,x.PriceVariance,x.JournalId,x.ReversalJournalId,
                JournalReference=db.Journals.Where(j=>j.TenantId==user.TenantId && j.Id==x.JournalId)
                    .Select(j=>j.PublicId).FirstOrDefault(),
                OrderNumber=db.ProcurementPurchaseOrders.Where(p=>p.TenantId==user.TenantId &&
                    p.Id==x.PurchaseOrderId && !p.IsDeleted).Select(p=>p.OrderNumber).FirstOrDefault()
            }).ToListAsync(ct);
        var invoiceIds = rows.Select(x => x.Id).ToArray();
        // Source-linked posted-invoice validation is deliberately separate from
        // the register predicate: an invalid/missing GL must be reported as a
        // conflict, not omitted from the financial reconciliation.
        var verifiedInvoiceIds = await db.ProcurementSupplierInvoices.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && invoiceIds.Contains(x.Id) &&
                x.StateCode == "Posted" && x.JournalId.HasValue &&
                db.Journals.Any(j => j.TenantId == user.TenantId && j.Id == x.JournalId &&
                    !j.IsDeleted && j.State == JournalState.Posted &&
                    j.SourceType == "ProcurementSupplierInvoice" && j.SourceId == x.Id &&
                    j.CampusId == x.CampusId && j.CurrencyCode == "BDT" &&
                    !j.ReversalOfJournalId.HasValue &&
                    db.JournalLines.Any(l => l.TenantId == user.TenantId && !l.IsDeleted &&
                        l.JournalId == j.Id && l.CampusId == x.CampusId &&
                        l.CreditAmount == x.TotalAmount && l.DebitAmount == 0m &&
                        db.Accounts.Any(a => a.TenantId == user.TenantId && a.Id == l.AccountId &&
                            !a.IsDeleted && a.AccountType == AccountType.Liability))))
            .Select(x => x.Id).ToHashSetAsync(ct);
        var postedInvoiceJournalIds = rows.Where(x => x.JournalId.HasValue)
            .Select(x => x.JournalId!.Value).Distinct().ToArray();
        var invoiceJournalLines = await db.JournalLines.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                postedInvoiceJournalIds.Contains(x.JournalId))
            .Select(x => new { x.JournalId, x.LineNo, x.AccountId, x.CampusId, x.DebitAmount, x.CreditAmount })
            .ToListAsync(ct);
        var invoiceLinesByJournal = invoiceJournalLines.GroupBy(x => x.JournalId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        // Historical GL accounts may be inactive now; validate tenant, currency
        // and accounting type without requiring current posting eligibility.
        var invoiceAccountIds = invoiceJournalLines.Select(x => x.AccountId).Distinct().ToArray();
        var invoiceAccounts = await db.Accounts.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                x.CurrencyCode == "BDT" && invoiceAccountIds.Contains(x.Id))
            .Select(x => new { x.Id, x.AccountType }).ToDictionaryAsync(x => x.Id, ct);
        var payments = await db.Journals.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && x.SourceType == SettlementSource && x.SourceId.HasValue &&
            invoiceIds.Contains(x.SourceId.Value) &&
            (x.State == JournalState.Posted || x.State == JournalState.Approved ||
             x.State == JournalState.Submitted))
            .Select(x => new { x.Id, x.SourceId, x.CampusId, x.State, x.CurrencyCode }).ToListAsync(ct);
        var paymentIds = payments.Select(x => x.Id).ToArray();
        var reversals = await db.Journals.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && x.SourceType == SettlementReversalSource && x.SourceId.HasValue &&
            paymentIds.Contains(x.SourceId.Value) && x.State == JournalState.Posted)
            .Select(x => new { x.Id, x.SourceId, x.CampusId, x.ReversalOfJournalId, x.CurrencyCode }).ToListAsync(ct);
        var neededJournalIds = paymentIds.Concat(reversals.Select(x => x.Id)).ToArray();
        var journalLines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && neededJournalIds.Contains(x.JournalId))
            .Select(x => new { x.JournalId, x.LineNo, x.CampusId, x.DebitAmount, x.CreditAmount,
                x.AccountId }).ToListAsync(ct);
        var linesByJournal = journalLines.GroupBy(x => x.JournalId)
            .ToDictionary(x => x.Key, x => x.OrderBy(v => v.LineNo).ToArray());
        var paymentsByInvoice = payments.GroupBy(x => x.SourceId!.Value)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var reversalsByPayment = reversals.GroupBy(x => x.SourceId!.Value)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var credits = await db.ProcurementSupplierReturnCredits.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted &&
            invoiceIds.Contains(x.SupplierInvoiceId) &&
            (x.StateCode == "Posted" || x.StateCode == "Submitted"))
            .Select(x => new { x.Id, x.SupplierInvoiceId, x.CampusId, x.StateCode,
                x.CreditAmount, x.StockValue, x.UnpaidCreditAmount, x.RefundReceivableAmount,
                x.StockAccountId, x.PayableAccountId, x.VarianceAccountId,
                x.RefundReceivableAccountId, x.JournalId, x.StockMovementId })
            .ToListAsync(ct);
        var creditsByInvoice = credits.GroupBy(x => x.SupplierInvoiceId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var postedCreditIds = credits.Where(x => x.StateCode == "Posted").Select(x => x.Id).ToArray();
        var verifiedCreditIds = await (from credit in db.ProcurementSupplierReturnCredits.AsNoTracking()
            join j in db.Journals.AsNoTracking() on credit.JournalId equals j.Id
            join m in db.InventoryMovements.AsNoTracking() on credit.StockMovementId equals m.Id
            where credit.TenantId == user.TenantId && postedCreditIds.Contains(credit.Id) &&
                !credit.IsDeleted && j.TenantId == user.TenantId && !j.IsDeleted &&
                j.SourceType == ReturnCreditSource && j.SourceId == credit.Id &&
                j.State == JournalState.Posted && j.CampusId == credit.CampusId &&
                j.CurrencyCode == "BDT" &&
                m.TenantId == user.TenantId && !m.IsDeleted && m.StateCode == "Posted" &&
                m.MovementTypeCode == "Issue" &&
                m.SourceType == "ProcurementSupplierReturn" && m.SourceId == credit.Id
            select credit.Id).ToHashSetAsync(ct);
        // Verify the posted credit's precise GL composition, not only its
        // journal and stock movement references.
        var creditJournalIds = credits.Where(x => x.StateCode == "Posted" && x.JournalId.HasValue)
            .Select(x => x.JournalId!.Value).Distinct().ToArray();
        var creditJournalLines = await db.JournalLines.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                creditJournalIds.Contains(x.JournalId))
            .Select(x => new { x.JournalId, x.LineNo, x.CampusId, x.AccountId,
                x.DebitAmount, x.CreditAmount }).ToListAsync(ct);
        var creditLinesByJournal = creditJournalLines.GroupBy(x => x.JournalId)
            .ToDictionary(x => x.Key, x => x.OrderBy(l => l.LineNo).ToArray());
        var items=rows.Select(x=>{
            var days=Math.Max(0,reference.DayNumber-x.DueDate.DayNumber);
            decimal posted=0m, pending=0m;
            decimal credited=0m, pendingCredit=0m, refundReceivable=0m;
            var conflicted = !verifiedInvoiceIds.Contains(x.Id) ||
                !invoiceLinesByJournal.TryGetValue(x.JournalId ?? 0, out var sourceLines) ||
                sourceLines.Sum(l => l.DebitAmount) != sourceLines.Sum(l => l.CreditAmount) ||
                sourceLines.Any(l => l.CampusId != x.CampusId ||
                    l.DebitAmount < 0m || l.CreditAmount < 0m ||
                    (l.DebitAmount > 0m && l.CreditAmount > 0m));
            // A balanced GL with the correct payable credit is still corrupt if
            // stock valuation, signed price variance or account type has drifted.
            // Posting requires stock asset debit, payable liability credit and
            // one expense variance line (debit positive, credit negative).
            if (!conflicted && sourceLines is not null)
            {
                var source = sourceLines.OrderBy(l => l.LineNo).ToArray();
                var expectedCount = x.PriceVariance == 0m ? 2 : 3;
                if (x.TotalAmount <= 0m || x.StockValue <= 0m ||
                    x.PriceVariance != x.TotalAmount - x.StockValue ||
                    source.Length != expectedCount ||
                    source[0].LineNo != 1 || source[1].LineNo != 2 ||
                    source[0].DebitAmount != x.StockValue || source[0].CreditAmount != 0m ||
                    source[1].DebitAmount != 0m || source[1].CreditAmount != x.TotalAmount ||
                    source[0].AccountId == source[1].AccountId ||
                    !invoiceAccounts.TryGetValue(source[0].AccountId, out var stockAccount) ||
                    stockAccount.AccountType != AccountType.Asset ||
                    !invoiceAccounts.TryGetValue(source[1].AccountId, out var payableAccount) ||
                    payableAccount.AccountType != AccountType.Liability ||
                    (expectedCount == 3 &&
                        (source[2].LineNo != 3 ||
                         source[2].AccountId == source[0].AccountId ||
                         source[2].AccountId == source[1].AccountId ||
                         !invoiceAccounts.TryGetValue(source[2].AccountId, out var varianceAccount) ||
                         varianceAccount.AccountType != AccountType.Expense ||
                         source[2].DebitAmount != Math.Max(0m, x.PriceVariance) ||
                         source[2].CreditAmount != Math.Max(0m, -x.PriceVariance))))
                    conflicted = true;
            }
            if (creditsByInvoice.TryGetValue(x.Id,out var linkedCredits))
                foreach (var credit in linkedCredits)
                {
                    if (credit.CampusId != x.CampusId || credit.CreditAmount <= 0m)
                    { conflicted = true; continue; }
                    if (credit.StateCode == "Submitted")
                    { pendingCredit += credit.CreditAmount; continue; }
                    if (!verifiedCreditIds.Contains(credit.Id) ||
                        credit.JournalId is null or <= 0 ||
                        credit.StockAccountId <= 0 || credit.PayableAccountId <= 0 ||
                        credit.StockValue <= 0m || credit.UnpaidCreditAmount < 0m ||
                        credit.RefundReceivableAmount < 0m ||
                        credit.UnpaidCreditAmount + credit.RefundReceivableAmount != credit.CreditAmount)
                    { conflicted = true; continue; }
                    var variance = credit.CreditAmount - credit.StockValue;
                    if ((variance != 0m && credit.VarianceAccountId is null or <= 0) ||
                        (credit.RefundReceivableAmount > 0m &&
                         credit.RefundReceivableAccountId is null or <= 0))
                    { conflicted = true; continue; }
                    var expected = new List<(long AccountId, decimal Debit, decimal Credit)>();
                    if (credit.UnpaidCreditAmount > 0m)
                        expected.Add((credit.PayableAccountId, credit.UnpaidCreditAmount, 0m));
                    if (credit.RefundReceivableAmount > 0m)
                        expected.Add((credit.RefundReceivableAccountId!.Value,
                            credit.RefundReceivableAmount, 0m));
                    expected.Add((credit.StockAccountId, 0m, credit.StockValue));
                    if (variance > 0m)
                        expected.Add((credit.VarianceAccountId!.Value, 0m, variance));
                    else if (variance < 0m)
                        expected.Add((credit.VarianceAccountId!.Value, -variance, 0m));
                    if (!creditLinesByJournal.TryGetValue(credit.JournalId.Value, out var postedLines) ||
                        postedLines.Length != expected.Count ||
                        postedLines.Where((line, i) =>
                            line.LineNo != i + 1 || line.CampusId != credit.CampusId ||
                            line.AccountId != expected[i].AccountId ||
                            line.DebitAmount != expected[i].Debit ||
                            line.CreditAmount != expected[i].Credit).Any() ||
                        postedLines.Sum(l => l.DebitAmount) != postedLines.Sum(l => l.CreditAmount))
                    { conflicted = true; continue; }
                    credited += credit.CreditAmount;
                    refundReceivable += credit.RefundReceivableAmount;
                }
            if(paymentsByInvoice.TryGetValue(x.Id,out var linked))
                foreach(var payment in linked)
                {
                    if(payment.CampusId!=x.CampusId || payment.CurrencyCode!="BDT" ||
                        !linesByJournal.TryGetValue(payment.Id,out var p) || p.Length!=2 ||
                        p[0].LineNo!=1 || p[1].LineNo!=2 || p[0].CampusId!=x.CampusId ||
                        p[1].CampusId!=x.CampusId || p[0].DebitAmount<=0m ||
                        p[0].CreditAmount!=0m || p[1].DebitAmount!=0m ||
                        p[1].CreditAmount!=p[0].DebitAmount || p[0].AccountId==p[1].AccountId)
                    {conflicted=true;continue;}
                    if(payment.State!=JournalState.Posted){pending+=p[0].DebitAmount;continue;}
                    if(reversalsByPayment.TryGetValue(payment.Id,out var contra))
                    {
                        if(contra.Length!=1 || contra[0].CampusId!=x.CampusId ||
                            contra[0].CurrencyCode!="BDT" || contra[0].ReversalOfJournalId!=payment.Id ||
                            !linesByJournal.TryGetValue(contra[0].Id,out var c) || c.Length!=2 ||
                            c[0].LineNo!=1 || c[1].LineNo!=2 || c[0].CampusId!=x.CampusId ||
                            c[1].CampusId!=x.CampusId || c[0].AccountId!=p[0].AccountId ||
                            c[1].AccountId!=p[1].AccountId || c[0].DebitAmount!=p[0].CreditAmount ||
                            c[0].CreditAmount!=p[0].DebitAmount || c[1].DebitAmount!=p[1].CreditAmount ||
                            c[1].CreditAmount!=p[1].DebitAmount)
                            conflicted=true;
                        continue;
                    }
                    posted+=p[0].DebitAmount;
                }
            if(posted<0m || pending<0m || credited<0m ||
                credited>x.TotalAmount || posted>x.TotalAmount ||
                posted+credited>x.TotalAmount ||
                posted+pending+credited+pendingCredit>x.TotalAmount &&
                    (pending>0m || pendingCredit>0m))
                conflicted=true;
            var netPayable=Math.Max(0m,x.TotalAmount-credited-posted);
            return new ProcurementSupplierPayableAgingRowDto {
                InvoiceReference=x.PublicId,JournalReference=x.JournalReference,
                SupplierInvoiceNumber=x.SupplierInvoiceNumber,OrderNumber=x.OrderNumber??"",
                SupplierId=x.SupplierId,CampusId=x.CampusId,DueDate=x.DueDate,
                GrossPostedAmount=x.TotalAmount,PostedSettlementAmount=posted,
                PostedSupplierCreditAmount=credited,PendingSupplierCreditAmount=pendingCredit,
                SupplierRefundReceivableAmount=refundReceivable,
                ReservedUnpostedAmount=pending,
                SourceLinkedOutstandingAmount=conflicted?null:netPayable,
                SettlementIntegrityStatus=conflicted?"EvidenceConflict":"SourceLinked",
                DaysOverdue=days,
                AgingBucket=days==0?"NotDue":days<=30?"1-30":days<=60?"31-60":days<=90?"61-90":"90+",
                ReversalPending=x.ReversalJournalId.HasValue
            };
        }).ToList();
        return ApiResponse<PagedResult<ProcurementSupplierPayableAgingRowDto>>.SuccessResponse(
            new PagedResult<ProcurementSupplierPayableAgingRowDto>{
                Items=items,TotalCount=count,Page=page,PageSize=pageSize});
    }
}
