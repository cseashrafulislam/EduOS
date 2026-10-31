using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Inventory;
using EduOS.Service.Services.Enterprise;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerV3ProcurementPurchaseOrderTests
{
    // Exercise the trace's own fail-closed authorization, independently of HTTP role filters.
    private sealed class ReadOnlyActor(long tenantId, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenantId;
        public long UserId => userId;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => ["Viewer"];
        public bool IsInRole(string role) => role == "Viewer";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
    private static ProcurementSupplierInvoiceService Invoices(EduOSDbContext ctx, long tenantId, long userId) =>
        new(ctx, new Actor(tenantId,userId), Goods(ctx,tenantId,userId), TimeProvider.System,
            NullLogger<ProcurementSupplierInvoiceService>.Instance);

    [Fact]
    public async Task SQL_supplier_invoice_partial_billing_three_way_payable_variance_and_contra()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, makerId, checkerId, poId, poLineId, locationId, periodId,
            assetAccountId, payableAccountId, varianceAccountId;
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "SI-"+suffix, Name="Supplier Invoice SQL",
                Email="si-"+suffix+"@example.test",State=TenantState.Active };
            var maker = new ApplicationUser { UserName="si-m-"+suffix,
                NormalizedUserName=("si-m-"+suffix).ToUpperInvariant(),
                Email="si-m-"+suffix+"@example.test",FullName="Payable maker" };
            var checker = new ApplicationUser { UserName="si-c-"+suffix,
                NormalizedUserName=("si-c-"+suffix).ToUpperInvariant(),
                Email="si-c-"+suffix+"@example.test",FullName="Finance approver" };
            setup.Tenants.Add(tenant);setup.ApplicationUsers.AddRange(maker,checker);
            await setup.SaveChangesAsync();
            tenantId=tenant.Id;makerId=maker.Id;checkerId=checker.Id;
        }
        await using (var setup = Open(options,tenantId,makerId))
        {
            var campus = new Campus { TenantId=tenantId,Code="SI-C-"+suffix,Name="Campus" };
            var item = new InventoryItem { TenantId=tenantId,Code="SI-I-"+suffix,
                Name="Supplier stock",IsActive=true,IsStockTracked=true };
            setup.Campuses.Add(campus);setup.InventoryItems.Add(item);
            await setup.SaveChangesAsync();
            var location = new InventoryLocation { TenantId=tenantId,CampusId=campus.Id,
                Code="SI-L-"+suffix,Name="Store",IsActive=true };
            var supplier = new ProcurementSupplier { TenantId=tenantId,CampusId=campus.Id,
                ClientRequestId=Guid.NewGuid(),SupplierCode="SI-S-"+suffix,
                Name="Test supplier",StateCode="Approved" };
            var requisition = new ProcurementRequisition { TenantId=tenantId,CampusId=campus.Id,
                ClientRequestId=Guid.NewGuid(),RequestNumber="SI-R-"+suffix,
                Purpose="Procurement financial acceptance",StateCode="Approved" };
            setup.InventoryLocations.Add(location);setup.ProcurementSuppliers.Add(supplier);
            setup.ProcurementRequisitions.Add(requisition);
            await setup.SaveChangesAsync();locationId=location.Id;
            var reqLine = new ProcurementRequisitionLine {TenantId=tenantId,RequisitionId=requisition.Id,
                LineNo=1,InventoryItemId=item.Id,Quantity=10m,EstimatedUnitPrice=10m};
            setup.ProcurementRequisitionLines.Add(reqLine);await setup.SaveChangesAsync();
            var po = new ProcurementPurchaseOrder { TenantId=tenantId,ClientRequestId=Guid.NewGuid(),
                CampusId=campus.Id,SupplierId=supplier.Id,RequisitionId=requisition.Id,
                OrderNumber="SI-PO-"+suffix,StateCode="Approved",
                SubmittedAt=DateTime.UtcNow.AddMinutes(-5),DecidedAt=DateTime.UtcNow,
                DecidedByUserId=checkerId };
            setup.ProcurementPurchaseOrders.Add(po);await setup.SaveChangesAsync();poId=po.Id;
            var poLine = new ProcurementPurchaseOrderLine { TenantId=tenantId,PurchaseOrderId=po.Id,
                RequisitionLineId=reqLine.Id,LineNo=1,InventoryItemId=item.Id,Quantity=10m,UnitPrice=10m };
            setup.ProcurementPurchaseOrderLines.Add(poLine);await setup.SaveChangesAsync();poLineId=poLine.Id;
            var period = new AccountingPeriod {TenantId=tenantId,Name="Open BDT payable period",
                StartDate=date.AddMonths(-1),EndDate=date.AddMonths(1),IsClosed=false};
            var asset=new Account {TenantId=tenantId,Code="SI-AS-"+suffix,Name="Inventory",
                AccountType=AccountType.Asset,IsActive=true,AllowsPosting=true};
            var payable=new Account {TenantId=tenantId,Code="SI-AP-"+suffix,Name="Supplier payable",
                AccountType=AccountType.Liability,IsActive=true,AllowsPosting=true};
            var variance=new Account {TenantId=tenantId,Code="SI-VR-"+suffix,Name="Price variance",
                AccountType=AccountType.Expense,IsActive=true,AllowsPosting=true};
            setup.AccountingPeriods.Add(period);setup.Accounts.AddRange(asset,payable,variance);
            await setup.SaveChangesAsync();periodId=period.Id;assetAccountId=asset.Id;
            payableAccountId=payable.Id;varianceAccountId=variance.Id;
        }
        Guid receiptReference;
        await using (var maker = Open(options,tenantId,makerId))
        {
            var stock=Goods(maker,tenantId,makerId);
            var draft=await stock.CreateGoodsReceiptAsync(new CreateProcurementGoodsReceiptRequest
            {
                ClientRequestId=Guid.NewGuid(),PurchaseOrderId=poId,LocationId=locationId,
                Reason="Recorded delivery of four units against approved order",
                Lines=[new ProcurementGoodsReceiptLineInput {PurchaseOrderLineId=poLineId,
                    Quantity=4m,LotOrBatchNo="LOT-SI"}]
            });
            Assert.True(draft.Success,draft.Message);receiptReference=draft.Data!.Reference;
            var submitted=await stock.SubmitGoodsReceiptAsync(receiptReference,new SubmitProcurementGoodsReceiptRequest
                {ClientRequestId=Guid.NewGuid(),RowVersion=draft.Data.RowVersion});
            Assert.True(submitted.Success,submitted.Message);
        }
        await using (var checker=Open(options,tenantId,checkerId))
        {
            var stock=Goods(checker,tenantId,checkerId);
            var current=(await stock.GetGoodsReceiptAsync(receiptReference)).Data!;
            var posted=await stock.ReviewGoodsReceiptAsync(receiptReference,new ReviewProcurementGoodsReceiptRequest
            {
                ClientRequestId=Guid.NewGuid(),RowVersion=current.RowVersion,Decision="Approve",
                ValuationMode="Automatic",Reason="Independent receipt inspection confirms quantity"
            });
            Assert.True(posted.Success,posted.Message);
        }
        CreateProcurementSupplierInvoiceRequest Draft(string number,decimal quantity=2m,
            string mode="Automatic",decimal price=0m,string? varianceReason=null) => new()
        {
            ClientRequestId=Guid.NewGuid(),PurchaseOrderId=poId,SupplierInvoiceNumber=number,
            InvoiceDate=date,DueDate=date.AddDays(15),PriceMode=mode,
            Reason="Supplier original bill checked against accepted delivery",
            Lines=[new PreviewProcurementInvoiceLineRequest { GoodsReceiptReference=receiptReference,
                ReceiptLineNo=1,Quantity=quantity,Amount=price,VarianceReason=varianceReason }]
        };
        async Task<ProcurementSupplierInvoiceDto> Submit(CreateProcurementSupplierInvoiceRequest request)
        {
            await using var ctx=Open(options,tenantId,makerId);
            var service=Invoices(ctx,tenantId,makerId);
            var draft=await service.CreateAsync(request);Assert.True(draft.Success,draft.Message);
            var replay=await service.CreateAsync(request);
            Assert.True(replay.Success,replay.Message);
            Assert.Equal(draft.Data!.Reference,replay.Data!.Reference);
            var submitted=await service.SubmitAsync(draft.Data.Reference,new SubmitProcurementSupplierInvoiceRequest
                {ClientRequestId=Guid.NewGuid(),RowVersion=draft.Data.RowVersion});
            Assert.True(submitted.Success,submitted.Message);
            return submitted.Data!;
        }
        var firstInput=Draft("INV-"+suffix);
        var first=await Submit(firstInput);
        await using (var verify=Open(options,tenantId,makerId))
        {
            var duplicate=await Invoices(verify,tenantId,makerId).CreateAsync(Draft(("inv-"+suffix).ToLowerInvariant()));
            Assert.Equal(409,duplicate.StatusCode);
            Assert.Equal(409,(await Invoices(verify,tenantId,makerId).CreateAsync(new CreateProcurementSupplierInvoiceRequest
            {
                ClientRequestId=firstInput.ClientRequestId,PurchaseOrderId=poId,
                SupplierInvoiceNumber=firstInput.SupplierInvoiceNumber,InvoiceDate=date,DueDate=date.AddDays(15),
                Reason=firstInput.Reason,Lines=[new PreviewProcurementInvoiceLineRequest
                    {GoodsReceiptReference=receiptReference,ReceiptLineNo=1,Quantity=3m}]
            })).StatusCode);
        }
        var second=await Submit(Draft("INV2-"+suffix,2m,"AutoPlusManual",-1m,
            "Documented supplier discount reduces final unit price"));
        await using (var stockReview=Open(options,tenantId,checkerId))
        {
            var postedMovement=await stockReview.InventoryMovements.AsNoTracking()
                .SingleAsync(x=>x.SourceType=="ProcurementGoodsReceipt" && x.StateCode=="Posted");
            var blocked=await Goods(stockReview,tenantId,checkerId).ReverseMovementAsync(
                postedMovement.PublicId,new ReverseInventoryMovementRequestDto
                {
                    ClientRequestId=Guid.NewGuid(),RowVersion=Convert.ToBase64String(postedMovement.RowVersion),
                    Reason="Cannot reverse goods accepted against submitted supplier bills"
                });
            Assert.Equal(409,blocked.StatusCode);
        }
        await using (var over=Open(options,tenantId,makerId))
        {
            var proposed=await Invoices(over,tenantId,makerId).CreateAsync(Draft("INV3-"+suffix,1m));
            Assert.True(proposed.Success,proposed.Message); // Draft does not reserve another invoice's capacity.
            Assert.Equal(409,(await Invoices(over,tenantId,makerId).SubmitAsync(proposed.Data!.Reference,
                new SubmitProcurementSupplierInvoiceRequest {ClientRequestId=Guid.NewGuid(),
                    RowVersion=proposed.Data.RowVersion})).StatusCode);
        }
        ReviewProcurementSupplierInvoiceRequest Approve(ProcurementSupplierInvoiceDto x) => new()
        {
            ClientRequestId=Guid.NewGuid(),RowVersion=x.RowVersion,Decision="Approve",
            Reason="Independent Finance verified delivery valuation and supplier bill",
            AccountingPeriodId=periodId,InventoryAccountId=assetAccountId,
            PayableAccountId=payableAccountId,PriceVarianceAccountId=varianceAccountId
        };
        await using (var own=Open(options,tenantId,makerId))
            Assert.Equal(403,(await Invoices(own,tenantId,makerId).ReviewAsync(first.Reference,Approve(first))).StatusCode);
        await using (var checker=Open(options,tenantId,checkerId))
        {
            var service=Invoices(checker,tenantId,checkerId);
            var approval=Approve(first);
            var posted=await service.ReviewAsync(first.Reference,approval);
            Assert.True(posted.Success,posted.Message);
            Assert.Equal("Posted",posted.Data!.StateCode);
            Assert.Equal(20m,posted.Data.TotalAmount);
            Assert.Equal(20m,posted.Data.StockValue);
            Assert.NotNull(posted.Data.JournalReference);
            Assert.Equal(posted.Data.JournalReference,(await service.ReviewAsync(first.Reference,approval)).Data!.JournalReference);
            var secondPosted=await service.ReviewAsync(second.Reference,Approve(second));
            Assert.True(secondPosted.Success,secondPosted.Message);
            Assert.Equal(18m,secondPosted.Data!.TotalAmount);
            Assert.Equal(-2m,secondPosted.Data.PriceVariance);
            var aged=await service.AgingAsync(date.AddDays(20),null,true,1,25);
            Assert.True(aged.Success,aged.Message);
            Assert.Equal(2,aged.Data!.TotalCount);
            Assert.Equal(38m,aged.Data.Items.Sum(x=>x.GrossPostedAmount));
            Assert.All(aged.Data.Items,x=>Assert.Equal(5,x.DaysOverdue));
            Assert.Empty((await service.AgingAsync(date,null,true,1,25)).Data!.Items);
        }
        await using (var verify=Open(options,tenantId,makerId))
        {
            Assert.Equal(2,await verify.Journals.CountAsync(x=>x.SourceType=="ProcurementSupplierInvoice"));
            var journals=await verify.Journals.Where(x=>x.SourceType=="ProcurementSupplierInvoice").Select(x=>x.Id).ToListAsync();
            var entries=await verify.JournalLines.Where(x=>journals.Contains(x.JournalId)).ToListAsync();
            Assert.Equal(38m,entries.Where(x=>x.AccountId==payableAccountId).Sum(x=>x.CreditAmount));
            Assert.Equal(40m,entries.Where(x=>x.AccountId==assetAccountId).Sum(x=>x.DebitAmount));
            Assert.Equal(2m,entries.Where(x=>x.AccountId==varianceAccountId).Sum(x=>x.CreditAmount));
            Assert.Equal(entries.Sum(x=>x.DebitAmount),entries.Sum(x=>x.CreditAmount));
            Assert.Single(await verify.InventoryMovements.Where(x=>x.SourceType=="ProcurementGoodsReceipt").ToListAsync());
        }
        await using (var traceDb=Open(options,tenantId,makerId))
        {
            var trace=new V3EnterpriseSupplierPayableTraceService(traceDb,new Actor(tenantId,makerId));
            var result=await trace.GetAsync(first.Reference);
            Assert.True(result.Success,result.Message);
            Assert.Equal("ExplicitBalancedReference",result.Data!.FinanceEvidenceStatus);
            Assert.Equal(20m,result.Data.TotalAmount);
            Assert.Equal(20m,result.Data.StockValue);
            Assert.Equal(1,result.Data.LineCount);
            Assert.Single(result.Data.GoodsReceiptReferences);
            Assert.Equal(receiptReference,result.Data.GoodsReceiptReferences[0]);
            Assert.Single(result.Data.Journals);
            Assert.Equal(20m,result.Data.Journals[0].Debit);
            Assert.Equal(20m,result.Data.Journals[0].Credit);
            var discounted=await trace.GetAsync(second.Reference);
            Assert.True(discounted.Success,discounted.Message);
            Assert.Equal(-2m,discounted.Data!.PriceVariance);
            Assert.Equal(20m,discounted.Data.Journals[0].Credit); // 18 payable + 2 variance credit; total balanced at 20.
            Assert.Equal(20m,discounted.Data.Journals[0].Debit);
            Assert.Equal(403,(await new V3EnterpriseSupplierPayableTraceService(
                traceDb,new ReadOnlyActor(tenantId,makerId)).GetAsync(first.Reference)).StatusCode);
        }
        // Disposable SQL historical-corruption probes: Finance/Procurement commands
        // cannot create these states, but a legacy import or privileged SQL repair
        // could. Both probes must fail closed, and transaction rollback must restore
        // the exact immutable source evidence without touching canonical balances.
        await using (var probeDb = Open(options, tenantId, makerId))
        {
            var originalJournalId = await probeDb.ProcurementSupplierInvoices.AsNoTracking()
                .Where(x => x.PublicId == first.Reference)
                .Select(x => x.JournalId).SingleAsync();
            Assert.True(originalJournalId.HasValue);
            var trace = new V3EnterpriseSupplierPayableTraceService(
                probeDb, new Actor(tenantId, makerId));
            Assert.Equal(200, (await trace.GetAsync(first.Reference)).StatusCode);

            await using (var transaction = await probeDb.Database.BeginTransactionAsync())
            {
                // A balanced Finance journal from the wrong campus is NOT valid
                // invoice-to-GL evidence, even with the correct journal ID.
                await probeDb.Journals.Where(x => x.Id == originalJournalId.Value)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.CampusId, (long?)null));
                Assert.Equal(409, (await trace.GetAsync(first.Reference)).StatusCode);
                await transaction.RollbackAsync();
            }
            Assert.Equal(200, (await trace.GetAsync(first.Reference)).StatusCode);

            await using (var transaction = await probeDb.Database.BeginTransactionAsync())
            {
                // An otherwise balanced journal with a forged SourceId must not
                // be accepted merely because the invoice retains its JournalId.
                await probeDb.Journals.Where(x => x.Id == originalJournalId.Value)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.SourceId, (long?)0));
                Assert.Equal(409, (await trace.GetAsync(first.Reference)).StatusCode);
                await transaction.RollbackAsync();
            }
            var restored = await trace.GetAsync(first.Reference);
            Assert.Equal(200, restored.StatusCode);
            Assert.Equal("ExplicitBalancedReference", restored.Data!.FinanceEvidenceStatus);
            Assert.Single(restored.Data.Journals);
            Assert.Equal(20m, restored.Data.Journals[0].Debit);
            Assert.Equal(20m, restored.Data.Journals[0].Credit);
            Assert.Equal(originalJournalId.Value, await probeDb.ProcurementSupplierInvoices
                .Where(x => x.PublicId == first.Reference)
                .Select(x => x.JournalId!.Value).SingleAsync());
        }

        // Concurrent read-only replay must never cross tenant boundaries or alter payable evidence.
        var replayStatuses=await Task.WhenAll(Enumerable.Range(0,8).Select(async index =>
        {
            var selectedTenant=index%2==0 ? tenantId : tenantId+999999;
            await using var scoped=Open(options,selectedTenant,makerId);
            return (await new V3EnterpriseSupplierPayableTraceService(
                scoped,new Actor(selectedTenant,makerId)).GetAsync(first.Reference)).StatusCode;
        }));
        Assert.Equal(new[] {200,404,200,404,200,404,200,404},replayStatuses);
        await using (var mismatched=Open(options,tenantId+999999,makerId))
            Assert.Equal(403,(await new V3EnterpriseSupplierPayableTraceService(
                mismatched,new Actor(tenantId,makerId)).GetAsync(first.Reference)).StatusCode);
        await using (var foreignTrace=Open(options,tenantId+999999,checkerId))
            Assert.Equal(404,(await new V3EnterpriseSupplierPayableTraceService(
                foreignTrace,new Actor(tenantId+999999,checkerId)).GetAsync(first.Reference)).StatusCode);
        await using (var foreign=Open(options,tenantId+999999,checkerId))
            Assert.Equal(404,(await Invoices(foreign,tenantId+999999,checkerId).GetAsync(first.Reference)).StatusCode);
        ProcurementSupplierInvoiceDto afterRequest;
        await using (var maker=Open(options,tenantId,makerId))
        {
            var service=Invoices(maker,tenantId,makerId);
            var current=(await service.GetAsync(first.Reference)).Data!;
            var reversed=await service.RequestReversalAsync(first.Reference,new RequestProcurementSupplierInvoiceReversal
            {
                ClientRequestId=Guid.NewGuid(),RowVersion=current.RowVersion,ReversalDate=date,
                AccountingPeriodId=periodId,Reason="Supplier invoice cancellation verified by Finance"
            });
            Assert.True(reversed.Success,reversed.Message);
            Assert.Equal("Posted",reversed.Data!.StateCode); // Contra is only submitted, not posted.
            afterRequest=reversed.Data;
        }
        await using (var pendingTraceDb=Open(options,tenantId,makerId))
        {
            var pending=await new V3EnterpriseSupplierPayableTraceService(
                pendingTraceDb,new Actor(tenantId,makerId)).GetAsync(first.Reference);
            Assert.True(pending.Success,pending.Message);
            Assert.Equal("ReversalPending",pending.Data!.FinanceEvidenceStatus);
            Assert.Equal("Posted",pending.Data.State);
            Assert.Equal(2,pending.Data.Journals.Count);
            Assert.Contains(pending.Data.Journals,x=>x.State==JournalState.Submitted.ToString());
        }
        await using (var checker=Open(options,tenantId,checkerId))
        {
            var service=Invoices(checker,tenantId,checkerId);
            var reversed=await service.ReviewReversalAsync(first.Reference,new ReviewProcurementSupplierInvoiceReversal
            {RowVersion=afterRequest.ReversalJournalRowVersion!,Approve=true,
                Reason="Second Finance reviewer approves exact payable contra"});
            Assert.True(reversed.Success,reversed.Message);
            Assert.Equal("Reversed",reversed.Data!.StateCode);
            Assert.NotNull(reversed.Data.ReversalJournalReference);
            var aged=await service.AgingAsync(date.AddDays(20),null,true,1,25);
            Assert.True(aged.Success,aged.Message);
            Assert.Single(aged.Data!.Items);
            Assert.Equal(18m,aged.Data.Items[0].GrossPostedAmount);
        }
        await using (var verify=Open(options,tenantId,makerId))
        {
            Assert.Equal(3,await verify.Journals.CountAsync(x=>
                x.SourceType=="ProcurementSupplierInvoice" || x.SourceType=="ProcurementSupplierInvoiceReversal"));
            var invoiceIds=await verify.ProcurementSupplierInvoices.Select(x=>x.Id).ToListAsync();
            Assert.Equal(3,invoiceIds.Count);
            Assert.Single(await verify.InventoryMovements.Where(x=>x.SourceType=="ProcurementGoodsReceipt").ToListAsync());
        }
        await using (var reversedTraceDb=Open(options,tenantId,makerId))
        {
            var trace=new V3EnterpriseSupplierPayableTraceService(reversedTraceDb,new Actor(tenantId,makerId));
            var reversedTrace=await trace.GetAsync(first.Reference);
            Assert.True(reversedTrace.Success,reversedTrace.Message);
            Assert.Equal("ExplicitReversedReference",reversedTrace.Data!.FinanceEvidenceStatus);
            Assert.Equal(2,reversedTrace.Data.Journals.Count);
            Assert.All(reversedTrace.Data.Journals,j=>Assert.True(j.Balanced));
            var invoiceLine=await reversedTraceDb.ProcurementSupplierInvoiceLines
                .FirstAsync(x=>x.SupplierInvoiceId==reversedTraceDb.ProcurementSupplierInvoices
                    .Where(y=>y.PublicId==first.Reference).Select(y=>y.Id).First());
            var original=invoiceLine.StockValue;
            // Canonical SQL trigger protects posted invoice evidence even against
            // direct ExecuteUpdate; assert denial rather than bypassing immutability.
            var immutable=await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(
                async () => await reversedTraceDb.ProcurementSupplierInvoiceLines
                    .Where(x=>x.Id==invoiceLine.Id)
                    .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.StockValue,original+1m)));
            Assert.Contains("Immutable invoice receipt-line evidence",immutable.Message);
            Assert.True((await trace.GetAsync(first.Reference)).Success);
        }
        ProcurementSupplierInvoiceDto rejectedRequest;
        await using (var maker=Open(options,tenantId,makerId))
        {
            var service=Invoices(maker,tenantId,makerId);
            var current=(await service.GetAsync(second.Reference)).Data!;
            var requested=await service.RequestReversalAsync(second.Reference,new RequestProcurementSupplierInvoiceReversal
            {
                ClientRequestId=Guid.NewGuid(),RowVersion=current.RowVersion,ReversalDate=date,
                AccountingPeriodId=periodId,Reason="Prepare second bill reversal pending independent review"
            });
            Assert.True(requested.Success,requested.Message);rejectedRequest=requested.Data!;
        }
        await using (var checker=Open(options,tenantId,checkerId))
        {
            var rejected=await Invoices(checker,tenantId,checkerId).ReviewReversalAsync(second.Reference,
                new ReviewProcurementSupplierInvoiceReversal
                {
                    RowVersion=rejectedRequest.ReversalJournalRowVersion!,Approve=false,
                    Reason="Supplier did not approve requested reversal; keep liability"
                });
            Assert.True(rejected.Success,rejected.Message);
            Assert.Equal("Posted",rejected.Data!.StateCode);
            Assert.Null(rejected.Data.ReversalJournalReference);
        }
        await using (var rejectedTraceDb=Open(options,tenantId,makerId))
        {
            var rejected=await new V3EnterpriseSupplierPayableTraceService(
                rejectedTraceDb,new Actor(tenantId,makerId)).GetAsync(second.Reference);
            Assert.True(rejected.Success,rejected.Message);
            Assert.Equal("ExplicitBalancedReference",rejected.Data!.FinanceEvidenceStatus);
            Assert.Equal("Posted",rejected.Data.State);
            Assert.Single(rejected.Data.Journals); // Rejected contra is immutable audit, not a posted reversal.
            Assert.Equal(18m,rejected.Data.TotalAmount);
            Assert.Equal(1,await rejectedTraceDb.Journals.CountAsync(x=>
                x.SourceType=="ProcurementSupplierInvoiceReversal" &&
                x.State==JournalState.Rejected));
        }
    }
}
