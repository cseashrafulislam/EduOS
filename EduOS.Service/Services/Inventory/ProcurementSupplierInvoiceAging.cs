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
        var query=Visible().Where(x=>x.StateCode=="Posted" && x.JournalId.HasValue &&
            db.Journals.Any(j=>j.TenantId==user.TenantId && j.Id==x.JournalId &&
                !j.IsDeleted && j.State==JournalState.Posted &&
                j.SourceType=="ProcurementSupplierInvoice" && j.SourceId==x.Id &&
                j.CampusId==x.CampusId && j.CurrencyCode=="BDT" &&
                !j.ReversalOfJournalId.HasValue &&
                db.JournalLines.Any(l=>l.TenantId==user.TenantId && !l.IsDeleted &&
                    l.JournalId==j.Id && l.CampusId==x.CampusId &&
                    l.CreditAmount==x.TotalAmount && l.DebitAmount==0m &&
                    db.Accounts.Any(a=>a.TenantId==user.TenantId && a.Id==l.AccountId &&
                        !a.IsDeleted && a.AccountType==AccountType.Liability))));
        if(supplierId.HasValue) query=query.Where(x=>x.SupplierId==supplierId.Value);
        if(overdueOnly) query=query.Where(x=>x.DueDate<reference);
        var count=await query.CountAsync(ct);
        var rows=await query.OrderBy(x=>x.DueDate).ThenBy(x=>x.Id)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(x=>new {
                x.Id,x.PublicId,x.SupplierInvoiceNumber,x.SupplierId,x.CampusId,x.DueDate,
                x.TotalAmount,x.ReversalJournalId,
                JournalReference=db.Journals.Where(j=>j.TenantId==user.TenantId && j.Id==x.JournalId)
                    .Select(j=>j.PublicId).FirstOrDefault(),
                OrderNumber=db.ProcurementPurchaseOrders.Where(p=>p.TenantId==user.TenantId &&
                    p.Id==x.PurchaseOrderId && !p.IsDeleted).Select(p=>p.OrderNumber).FirstOrDefault()
            }).ToListAsync(ct);
        var invoiceIds = rows.Select(x => x.Id).ToArray();
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
        var items=rows.Select(x=>{
            var days=Math.Max(0,reference.DayNumber-x.DueDate.DayNumber);
            decimal posted=0m, pending=0m;
            var conflicted=false;
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
            if(posted<0m || pending<0m || posted+pending>x.TotalAmount)conflicted=true;
            return new ProcurementSupplierPayableAgingRowDto {
                InvoiceReference=x.PublicId,JournalReference=x.JournalReference,
                SupplierInvoiceNumber=x.SupplierInvoiceNumber,OrderNumber=x.OrderNumber??"",
                SupplierId=x.SupplierId,CampusId=x.CampusId,DueDate=x.DueDate,
                GrossPostedAmount=x.TotalAmount,PostedSettlementAmount=posted,
                ReservedUnpostedAmount=pending,
                SourceLinkedOutstandingAmount=conflicted?null:x.TotalAmount-posted,
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
