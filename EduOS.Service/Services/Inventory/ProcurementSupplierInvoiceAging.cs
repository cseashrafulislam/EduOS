using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

public sealed partial class ProcurementSupplierInvoiceService
{
    /// <summary>Current posted source-linked AP aged against a due-date reference; never deducts unlinked payments.</summary>
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
                x.PublicId,x.SupplierInvoiceNumber,x.SupplierId,x.CampusId,x.DueDate,
                x.TotalAmount,x.ReversalJournalId,
                JournalReference=db.Journals.Where(j=>j.TenantId==user.TenantId && j.Id==x.JournalId)
                    .Select(j=>j.PublicId).FirstOrDefault(),
                OrderNumber=db.ProcurementPurchaseOrders.Where(p=>p.TenantId==user.TenantId &&
                    p.Id==x.PurchaseOrderId && !p.IsDeleted).Select(p=>p.OrderNumber).FirstOrDefault()
            }).ToListAsync(ct);
        var items=rows.Select(x=>{
            var days=Math.Max(0,reference.DayNumber-x.DueDate.DayNumber);
            return new ProcurementSupplierPayableAgingRowDto {
                InvoiceReference=x.PublicId,JournalReference=x.JournalReference,
                SupplierInvoiceNumber=x.SupplierInvoiceNumber,OrderNumber=x.OrderNumber??"",
                SupplierId=x.SupplierId,CampusId=x.CampusId,DueDate=x.DueDate,
                GrossPostedAmount=x.TotalAmount,DaysOverdue=days,
                AgingBucket=days==0?"NotDue":days<=30?"1-30":days<=60?"31-60":days<=90?"61-90":"90+",
                ReversalPending=x.ReversalJournalId.HasValue
            };
        }).ToList();
        return ApiResponse<PagedResult<ProcurementSupplierPayableAgingRowDto>>.SuccessResponse(
            new PagedResult<ProcurementSupplierPayableAgingRowDto>{
                Items=items,TotalCount=count,Page=page,PageSize=pageSize});
    }
}
