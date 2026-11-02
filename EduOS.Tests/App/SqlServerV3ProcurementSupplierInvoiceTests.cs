using EduOS.App.Controllers.Api;
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
using Microsoft.AspNetCore.Mvc;
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
    private sealed class CampusAccountantActor(long tenantId, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenantId;
        public long UserId => userId;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => ["Accountant"];
        public bool IsInRole(string role) => role == "Accountant";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
    private sealed class FrozenClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
    private static ProcurementSupplierInvoiceService Invoices(EduOSDbContext ctx, long tenantId, long userId,
        TimeProvider? clock = null) =>
        new(ctx, new Actor(tenantId,userId), Goods(ctx,tenantId,userId), clock ?? TimeProvider.System,
            NullLogger<ProcurementSupplierInvoiceService>.Instance);

    // Use an injected clock to cover the next month-end on any runner date.
    private static AssetAccountingService AssetFinanceAt(
        EduOSDbContext db, long tenantId, long actorId, TimeProvider clock)
    {
        var actor = new Actor(tenantId, actorId);
        return new AssetAccountingService(db, actor, clock,
            new InventoryMovementService(db, actor, clock,
                NullLogger<InventoryMovementService>.Instance),
            new AssetLifecycleService(db, actor, clock,
                NullLogger<AssetLifecycleService>.Instance),
            NullLogger<AssetAccountingService>.Instance);
    }

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
                Name="Supplier stock",IsActive=true,IsStockTracked=true,IsAssetTracked=true };
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
                StartDate=date.AddMonths(-1),EndDate=date.AddMonths(3),IsClosed=false};
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
            // SQL-only legacy corruption: a balanced GL cannot make a
            // self-approved invoice or a missing review reason trustworthy.
            await using (var transaction = await probeDb.Database.BeginTransactionAsync())
            {
                await probeDb.ProcurementSupplierInvoices
                    .Where(x => x.PublicId == first.Reference)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReviewedByUserId, (long?)makerId));
                Assert.Equal(409, (await trace.GetAsync(first.Reference)).StatusCode);
                await transaction.RollbackAsync();
            }
            await using (var transaction = await probeDb.Database.BeginTransactionAsync())
            {
                await probeDb.ProcurementSupplierInvoices
                    .Where(x => x.PublicId == first.Reference)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReviewReason, (string?)null));
                Assert.Equal(409, (await trace.GetAsync(first.Reference)).StatusCode);
                await transaction.RollbackAsync();
            }
            Assert.Equal(checkerId, await probeDb.ProcurementSupplierInvoices.AsNoTracking()
                .Where(x => x.PublicId == first.Reference)
                .Select(x => x.ReviewedByUserId).SingleAsync());
            Assert.False(string.IsNullOrWhiteSpace(await probeDb.ProcurementSupplierInvoices.AsNoTracking()
                .Where(x => x.PublicId == first.Reference)
                .Select(x => x.ReviewReason).SingleAsync()));

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
        // Settlement instruction is source-linked to the canonical Finance Journal, but never posts cash/GL.
        await using (var settlementSetup = Open(options,tenantId,makerId))
        {
            var bankLedger = new Account { TenantId=tenantId,Code="SI-BANK-"+suffix,
                Name="Approved bank asset",AccountType=AccountType.Asset,IsActive=true,AllowsPosting=true };
            settlementSetup.Accounts.Add(bankLedger);await settlementSetup.SaveChangesAsync();
            settlementSetup.BankAccounts.Add(new BankAccount {TenantId=tenantId,AccountId=bankLedger.Id,
                BankName="SQL fixture bank",AccountName="Supplier settlement",AccountNumber="TEST-"+suffix,
                CurrencyCode="BDT",IsActive=true});
            await settlementSetup.SaveChangesAsync();
        }
        long bankId;
        await using (var bankDb=Open(options,tenantId,makerId))
            bankId=await bankDb.BankAccounts.Where(x=>x.BankName=="SQL fixture bank").Select(x=>x.Id).SingleAsync();
        Guid instructionReference;
        string instructionVersion;
        await using (var maker=Open(options,tenantId,makerId))
        {
            var service=Invoices(maker,tenantId,makerId);
            var invoice=(await service.GetAsync(second.Reference)).Data!;
            var instruction=new CreateSupplierSettlementInstructionRequest {
                ClientRequestId=Guid.NewGuid(),InvoiceRowVersion=invoice.RowVersion,Amount=10m,
                BankAccountId=bankId,AccountingPeriodId=periodId,PlannedDate=date,
                Reason="Payable settlement instruction subject to independent review"
            };
            var created=await service.RequestSettlementAsync(second.Reference,instruction);
            Assert.True(created.Success,created.Message);
            instructionReference=created.Data!.Reference;instructionVersion=created.Data.RowVersion;
            Assert.Equal("Submitted",created.Data.State);
            Assert.Equal(instructionReference,(await service.RequestSettlementAsync(second.Reference,instruction)).Data!.Reference);
            Assert.Equal(409,(await service.RequestSettlementAsync(second.Reference,
                new CreateSupplierSettlementInstructionRequest {
                    ClientRequestId=Guid.NewGuid(),InvoiceRowVersion=invoice.RowVersion,Amount=9m,
                    BankAccountId=bankId,AccountingPeriodId=periodId,PlannedDate=date,
                    Reason="Second instruction would exceed remaining supplier liability"
                })).StatusCode);
            Assert.Equal(403,(await service.ReviewSettlementAsync(second.Reference,instructionReference,
                new ReviewSupplierSettlementInstructionRequest {RowVersion=instructionVersion,Approve=true,
                    Reason="Maker cannot independently approve this disbursement instruction"})).StatusCode);
        }
        // The maker's selected bank ledger must not become ambiguous before independent review.
        // Finance setup permits multiple bank records per ledger, so a duplicate active mapping
        // must block approval without changing the submitted journal or reserving more AP.
        long duplicateBankId;
        await using (var ambiguousSetup=Open(options,tenantId,makerId))
        {
            var bankLedgerId=await ambiguousSetup.BankAccounts.AsNoTracking()
                .Where(x=>x.Id==bankId).Select(x=>x.AccountId).SingleAsync();
            var duplicate=new BankAccount {TenantId=tenantId,AccountId=bankLedgerId,
                BankName="Duplicate SQL bank",AccountName="Ambiguous settlement bank",
                AccountNumber="DUP-"+suffix,CurrencyCode="BDT",IsActive=true};
            ambiguousSetup.BankAccounts.Add(duplicate);
            await ambiguousSetup.SaveChangesAsync();
            duplicateBankId=duplicate.Id;
        }
        await using (var ambiguousChecker=Open(options,tenantId,checkerId))
        {
            var rejected=await Invoices(ambiguousChecker,tenantId,checkerId).ReviewSettlementAsync(
                second.Reference,instructionReference,new ReviewSupplierSettlementInstructionRequest {
                    RowVersion=instructionVersion,Approve=true,
                    Reason="Ambiguous bank ledger must block independent approval" });
            Assert.Equal(409,rejected.StatusCode);
            var unchanged=await ambiguousChecker.Journals.AsNoTracking()
                .SingleAsync(x=>x.PublicId==instructionReference);
            Assert.Equal(JournalState.Submitted,unchanged.State);
            Assert.Null(unchanged.ApprovedByUserId);
            Assert.Null(unchanged.PostedAt);
        }
        // Deactivation does not erase the conflicting source identity: the
        // independent Finance trace still counts all non-deleted bank mappings.
        await using (var bankRepair=Open(options,tenantId,makerId))
            await bankRepair.BankAccounts.Where(x=>x.Id==duplicateBankId)
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsActive,false));
        await using (var inactiveConflict=Open(options,tenantId,checkerId))
            Assert.Equal(409,(await Invoices(inactiveConflict,tenantId,checkerId)
                .ReviewSettlementAsync(second.Reference,instructionReference,
                    new ReviewSupplierSettlementInstructionRequest {
                        RowVersion=instructionVersion,Approve=true,
                        Reason="Inactive duplicate bank mapping still conflicts with audit lineage" })).StatusCode);
        // Remove only this disposable fixture's duplicate before approving.
        await using (var bankRepair=Open(options,tenantId,makerId))
            await bankRepair.BankAccounts.Where(x=>x.Id==duplicateBankId)
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDeleted,true));
        await using (var checker=Open(options,tenantId,checkerId))
        {
            var service=Invoices(checker,tenantId,checkerId);
            var approved=await service.ReviewSettlementAsync(second.Reference,instructionReference,
                new ReviewSupplierSettlementInstructionRequest {RowVersion=instructionVersion,Approve=true,
                    Reason="Independent reviewer approves an unposted bank instruction"});
            Assert.True(approved.Success,approved.Message);
            Assert.Equal("Approved",approved.Data!.State);
            Assert.Equal(409,(await service.ReviewSettlementAsync(second.Reference,instructionReference,
                new ReviewSupplierSettlementInstructionRequest {RowVersion=instructionVersion,Approve=true,
                    Reason="Duplicate checker action must fail with stale version"})).StatusCode);
        }
        await using (var verify=Open(options,tenantId,makerId))
        {
            var settlement=await verify.Journals.SingleAsync(x=>x.PublicId==instructionReference);
            Assert.Equal(JournalState.Approved,settlement.State);
            Assert.Null(settlement.PostedAt);
            Assert.Null(settlement.PostedByUserId);
            var lines=await verify.JournalLines.Where(x=>x.JournalId==settlement.Id).OrderBy(x=>x.LineNo).ToListAsync();
            Assert.Equal(2,lines.Count);
            Assert.Equal(10m,lines.Sum(x=>x.DebitAmount));
            Assert.Equal(10m,lines.Sum(x=>x.CreditAmount));
            var traceService = new V3EnterpriseSupplierPayableTraceService(
                verify,new Actor(tenantId,makerId));
            var settlementTrace = await traceService.GetAsync(second.Reference);
            Assert.True(settlementTrace.Success,settlementTrace.Message);
            Assert.Equal(10m,settlementTrace.Data!.SettlementReservedAmount);
            var evidence = Assert.Single(settlementTrace.Data.SettlementInstructions);
            Assert.Equal(instructionReference,evidence.Reference);
            Assert.Equal("Approved",evidence.State);
            Assert.Equal(10m,evidence.Amount);
            // Disposable SQL rollback: an unbalanced UNPOSTED instruction is
            // a conflict, even though the original payable GL is balanced.
            await using (var tamper = await verify.Database.BeginTransactionAsync())
            {
                await verify.JournalLines.Where(x=>x.JournalId==settlement.Id && x.LineNo==2)
                    .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreditAmount,9m));
                Assert.Equal(409,(await traceService.GetAsync(second.Reference)).StatusCode);
                await tamper.RollbackAsync();
            }
            var restored = await traceService.GetAsync(second.Reference);
            Assert.True(restored.Success,restored.Message);
            Assert.Equal(10m,restored.Data!.SettlementReservedAmount);
            var service=Invoices(verify,tenantId,makerId);
            var currentResponse=await service.GetAsync(second.Reference);
            Assert.True(currentResponse.Success,currentResponse.Message);
            var current=currentResponse.Data!;
            Assert.Equal(409,(await service.RequestReversalAsync(second.Reference,
                new RequestProcurementSupplierInvoiceReversal {ClientRequestId=Guid.NewGuid(),
                    RowVersion=current.RowVersion,ReversalDate=date,AccountingPeriodId=periodId,
                    Reason="Reversal must refuse an approved linked supplier payment"})).StatusCode);
            // Approval revocation must be independent, versioned, auditable and
            // release the reservation without touching the posted payable or stock.
            var revokeRequest=new ReviewSupplierSettlementInstructionRequest {
                RowVersion=Convert.ToBase64String(settlement.RowVersion),Approve=false,
                Reason="Independent Finance rescinds an unposted supplier settlement approval"
            };
            Assert.Equal(403,(await service.ReviewSettlementAsync(
                second.Reference,instructionReference,revokeRequest)).StatusCode);
            await using (var checkerRevoke=Open(options,tenantId,checkerId))
            {
                var revokeService=Invoices(checkerRevoke,tenantId,checkerId);
                var revoked=await revokeService.ReviewSettlementAsync(
                    second.Reference,instructionReference,revokeRequest);
                Assert.True(revoked.Success,revoked.Message);
                Assert.Equal("Rejected",revoked.Data!.State);
                Assert.Equal(409,(await revokeService.ReviewSettlementAsync(
                    second.Reference,instructionReference,revokeRequest)).StatusCode);
            }
            var recorded=await verify.Journals.AsNoTracking().SingleAsync(x=>x.PublicId==instructionReference);
            Assert.Equal(JournalState.Rejected,recorded.State);
            Assert.Equal(checkerId,recorded.ApprovedByUserId);
            Assert.NotNull(recorded.ApprovedAt);
            Assert.Null(recorded.PostedAt);
            Assert.Contains("Approved instruction revoked",recorded.Description);
            var released=(await service.SettlementOptionsAsync(second.Reference)).Data!;
            Assert.Equal(0m,released.ReservedAmount);
            Assert.Equal(18m,released.AvailableAmount);
            var releasedTrace=await traceService.GetAsync(second.Reference);
            Assert.True(releasedTrace.Success,releasedTrace.Message);
            Assert.Equal(0m,releasedTrace.Data!.SettlementReservedAmount);
            Assert.Equal("Rejected",Assert.Single(releasedTrace.Data.SettlementInstructions).State);
            // Reset the test's ambient HttpContext after nested checker contexts:
            // DbContext audit stamping reads the active HTTP identity, not the service Actor.
            await using var makerAuditContext=Open(options,tenantId,makerId);
            var makerSettlementService=Invoices(makerAuditContext,tenantId,makerId);
            var replacement=await makerSettlementService.RequestSettlementAsync(second.Reference,
                new CreateSupplierSettlementInstructionRequest {
                    ClientRequestId=Guid.NewGuid(),InvoiceRowVersion=current.RowVersion,Amount=18m,
                    BankAccountId=bankId,AccountingPeriodId=periodId,PlannedDate=date,
                    Reason="Replacement source-linked instruction after approval revocation"
                });
            Assert.True(replacement.Success,replacement.Message);
            Assert.Equal("Submitted",replacement.Data!.State);
            Assert.Equal(18m,(await service.SettlementOptionsAsync(second.Reference)).Data!.ReservedAmount);
            // Maker withdrawal of a submitted instruction never books cash or GL.
            var withdrawal = new WithdrawSupplierSettlementInstructionRequest {
                ClientRequestId=Guid.NewGuid(),RowVersion=replacement.Data.RowVersion,
                Reason="Supplier changed the proposed settlement date before approval"
            };
            var withdrawn=await makerSettlementService.WithdrawSettlementAsync(
                second.Reference,replacement.Data.Reference,withdrawal);
            Assert.True(withdrawn.Success,withdrawn.Message);
            Assert.Equal("Rejected",withdrawn.Data!.State);
            Assert.Equal(withdrawn.Data.Reference,(await makerSettlementService.WithdrawSettlementAsync(
                second.Reference,replacement.Data.Reference,withdrawal)).Data!.Reference);
            Assert.Equal(409,(await makerSettlementService.WithdrawSettlementAsync(
                second.Reference,replacement.Data.Reference,
                new WithdrawSupplierSettlementInstructionRequest {
                    ClientRequestId=withdrawal.ClientRequestId,RowVersion=withdrawal.RowVersion,
                    Reason="A different reason must not reuse an existing withdrawal key"
                })).StatusCode);
            Assert.Equal(403,(await service.ReviewSettlementAsync(
                second.Reference,replacement.Data.Reference,
                new ReviewSupplierSettlementInstructionRequest {
                    RowVersion=replacement.Data.RowVersion,Approve=true,
                    Reason="Cannot approve a maker-withdrawn supplier instruction"
                })).StatusCode);
            Assert.Equal(0m,(await service.SettlementOptionsAsync(second.Reference)).Data!.ReservedAmount);
            var withdrawnJournal=await verify.Journals.AsNoTracking().SingleAsync(
                x=>x.PublicId==replacement.Data.Reference);
            Assert.Equal(JournalState.Rejected,withdrawnJournal.State);
            Assert.Equal(makerId,withdrawnJournal.UpdatedBy);
            Assert.Null(withdrawnJournal.PostedAt);
            Assert.Null(withdrawnJournal.ApprovedByUserId);
            Assert.Contains(withdrawal.ClientRequestId.ToString("N"),withdrawnJournal.Description);
            var afterWithdrawal=await traceService.GetAsync(second.Reference);
            Assert.True(afterWithdrawal.Success,afterWithdrawal.Message);
            Assert.Equal(0m,afterWithdrawal.Data!.SettlementReservedAmount);
            Assert.All(afterWithdrawal.Data.SettlementInstructions,
                x=>Assert.Equal("Rejected",x.State));
            Assert.Equal(1,await verify.Journals.CountAsync(x=>
                x.SourceType=="ProcurementSupplierInvoice" && x.SourceId==settlement.SourceId &&
                x.State==JournalState.Posted));
            Assert.Single(await verify.InventoryMovements.AsNoTracking()
                .Where(x=>x.SourceType=="ProcurementGoodsReceipt").ToListAsync());
            // Run non-maker denial only after the maker's writes; a new test
            // HttpContext would otherwise poison subsequent audit stamping.
            await using (var otherMaker=Open(options,tenantId,checkerId))
            {
                var checkerService=Invoices(otherMaker,tenantId,checkerId);
                Assert.Equal(403,(await checkerService.WithdrawSettlementAsync(
                    second.Reference,replacement.Data.Reference,withdrawal)).StatusCode);
                Assert.Equal(409,(await checkerService.ReviewSettlementAsync(
                    second.Reference,replacement.Data.Reference,
                    new ReviewSupplierSettlementInstructionRequest {
                        RowVersion=replacement.Data.RowVersion,Approve=true,
                        Reason="Independent checker cannot approve withdrawn instruction"
                    })).StatusCode);
            }
            Assert.Equal(1,await verify.Journals.CountAsync(x=>
                x.SourceType=="ProcurementSupplierInvoice" && x.SourceId==settlement.SourceId &&
                x.State==JournalState.Posted));
            Assert.Equal(403,(await new V3EnterpriseSupplierPayableTraceService(
                verify,new ReadOnlyActor(tenantId,makerId)).GetAsync(second.Reference)).StatusCode);
            // Two DIFFERENT maker instructions compete for the same BDT 18 unreserved AP.
            // Each BDT 12 request is valid alone, but both must never reserve BDT 24.
            // This is distinct from racing confirmation of one already approved journal.
            string reservationInvoiceVersion;
            await using (var reservationReader=Open(options,tenantId,makerId))
                reservationInvoiceVersion=(await Invoices(reservationReader,tenantId,makerId)
                    .GetAsync(second.Reference)).Data!.RowVersion;
            var reservationKeys=new[] {Guid.NewGuid(),Guid.NewGuid()};
            var reservationAttempts=await Task.WhenAll(reservationKeys.Select(async key =>
            {
                await using var competingMaker=Open(options,tenantId,makerId);
                return await Invoices(competingMaker,tenantId,makerId).RequestSettlementAsync(
                    second.Reference,new CreateSupplierSettlementInstructionRequest {
                        ClientRequestId=key,InvoiceRowVersion=reservationInvoiceVersion,
                        BankAccountId=bankId,AccountingPeriodId=periodId,PlannedDate=date,
                        Amount=12m,Reason="Competing source-linked supplier payable reservation"
                    });
            }));
            var acceptedReservation=Assert.Single(reservationAttempts,x=>x.Success);
            var winningReservation=acceptedReservation.Data!;
            Assert.Single(reservationAttempts,x=>x.StatusCode==409);
            var rejectedReservationKey=reservationKeys[reservationAttempts[0].Success?1:0];
            // Open AFTER parallel tasks: ambient tenant identity in the SQL fixture
            // must be refreshed before auditing a new DbContext.
            await using (var reservationVerifier=Open(options,tenantId,makerId))
            {
                var reservationService=Invoices(reservationVerifier,tenantId,makerId);
                var winningJournal=await reservationVerifier.Journals.AsNoTracking()
                    .SingleAsync(x=>x.PublicId==winningReservation.Reference);
                Assert.Equal(JournalState.Submitted,winningJournal.State);
                Assert.Null(winningJournal.PostedAt);
                Assert.False(await reservationVerifier.Journals.AsNoTracking()
                    .AnyAsync(x=>x.IdempotencyKey==rejectedReservationKey));
                Assert.Equal(1,await reservationVerifier.Journals.AsNoTracking()
                    .CountAsync(x=>x.SourceType==winningJournal.SourceType &&
                        x.SourceId==winningJournal.SourceId && x.State==JournalState.Submitted));
                Assert.Equal(0,await reservationVerifier.Journals.AsNoTracking()
                    .CountAsync(x=>x.SourceType==winningJournal.SourceType &&
                        x.SourceId==winningJournal.SourceId && x.State==JournalState.Posted));
                var reserved=(await reservationService.SettlementOptionsAsync(second.Reference)).Data!;
                Assert.Equal(12m,reserved.ReservedAmount);
                Assert.Equal(6m,reserved.AvailableAmount);
                var withdraw=await reservationService.WithdrawSettlementAsync(
                    second.Reference,winningReservation.Reference,
                    new WithdrawSupplierSettlementInstructionRequest {
                        ClientRequestId=Guid.NewGuid(),
                        RowVersion=winningReservation.RowVersion,
                        Reason="Concurrent reservation test release without bank execution"
                    });
                Assert.True(withdraw.Success,withdraw.Message);
                Assert.Equal("Rejected",withdraw.Data!.State);
                var reservationReleased=(await reservationService.SettlementOptionsAsync(second.Reference)).Data!;
                Assert.Equal(0m,reservationReleased.ReservedAmount);
                Assert.Equal(18m,reservationReleased.AvailableAmount);
                Assert.Equal(0,await reservationVerifier.Journals.AsNoTracking()
                    .CountAsync(x=>x.SourceType==winningJournal.SourceType &&
                        x.SourceId==winningJournal.SourceId && x.State==JournalState.Posted));
                var trace=await new V3EnterpriseSupplierPayableTraceService(
                    reservationVerifier,new Actor(tenantId,makerId)).GetAsync(second.Reference);
                Assert.True(trace.Success,trace.Message);
                Assert.Equal(0m,trace.Data!.SettlementReservedAmount);
                Assert.Equal(18m,trace.Data.SourceLinkedOutstandingAmount);
            }
            // Source-linked actual GL settlement: an independent reviewer first approves,
            // then an externally confirmed bank transaction posts the same canonical journal.
            // The source campus inherits Asia/Dhaka from its institution. At 17:59 UTC
            // the next business date is still future; at 18:40 UTC it is already today.
            // This exercises persisted GL posting and contra, not a mocked DOM contract.
            var simulatedUtcDay = date.AddDays(1);
            var postedDate = simulatedUtcDay.AddDays(1);
            var beforeCampusMidnight = new FrozenClock(new DateTimeOffset(
                simulatedUtcDay.ToDateTime(new TimeOnly(17, 59), DateTimeKind.Utc)));
            var atCampusMidnight = new FrozenClock(new DateTimeOffset(
                simulatedUtcDay.ToDateTime(new TimeOnly(18, 40), DateTimeKind.Utc)));
            Guid postedReference;
            string postedVersion;
            await using (var settlementMaker = Open(options,tenantId,makerId))
            {
                var paymentMakerService=Invoices(settlementMaker,tenantId,makerId);
                var currentInvoice=(await paymentMakerService.GetAsync(second.Reference)).Data!;
                var draftPayment=await paymentMakerService.RequestSettlementAsync(second.Reference,
                    new CreateSupplierSettlementInstructionRequest {
                        ClientRequestId=Guid.NewGuid(),InvoiceRowVersion=currentInvoice.RowVersion,
                        BankAccountId=bankId,AccountingPeriodId=periodId,PlannedDate=postedDate,Amount=6m,
                        Reason="Pay six units of posted payable after bank confirmation" });
                Assert.True(draftPayment.Success,draftPayment.Message);
                postedReference=draftPayment.Data!.Reference;
                postedVersion=draftPayment.Data.RowVersion;
                Assert.Equal(409,(await paymentMakerService.PostSettlementAsync(second.Reference,postedReference,
                    new PostSupplierSettlementRequest {ClientRequestId=Guid.NewGuid(),RowVersion=postedVersion,
                        ExecutedDate=postedDate,BankTransactionReference="BANK-SQL-"+suffix})).StatusCode);
            }
            await using (var settlementChecker=Open(options,tenantId,checkerId))
            {
                var paymentCheckerService=Invoices(settlementChecker,tenantId,checkerId,atCampusMidnight);
                var approved=await paymentCheckerService.ReviewSettlementAsync(second.Reference,postedReference,
                    new ReviewSupplierSettlementInstructionRequest {RowVersion=postedVersion,Approve=true,
                        Reason="Independent approval against confirmed payable and bank account"});
                Assert.True(approved.Success,approved.Message);
                var confirmation=new PostSupplierSettlementRequest {
                    ClientRequestId=Guid.NewGuid(),RowVersion=approved.Data!.RowVersion,
                    ExecutedDate=postedDate,BankTransactionReference="BANK-SQL-"+suffix };
                var premature=await Invoices(settlementChecker,tenantId,checkerId,beforeCampusMidnight)
                    .PostSettlementAsync(second.Reference,postedReference,confirmation);
                Assert.Equal(409,premature.StatusCode);
                Assert.Equal(JournalState.Approved,(await settlementChecker.Journals.AsNoTracking()
                    .SingleAsync(x=>x.PublicId==postedReference)).State);
                // A campus override takes precedence over the institution zone, and a
                // malformed configured zone fails closed without creating a bank/GL posting.
                var sourceCampusId=await settlementChecker.ProcurementSupplierInvoices.AsNoTracking()
                    .Where(x=>x.PublicId==second.Reference).Select(x=>x.CampusId).SingleAsync();
                await settlementChecker.Campuses.Where(x=>x.Id==sourceCampusId)
                    .ExecuteUpdateAsync(x=>x.SetProperty(c=>c.TimeZoneId,"UTC"));
                Assert.Equal(409,(await paymentCheckerService.PostSettlementAsync(
                    second.Reference,postedReference,confirmation)).StatusCode);
                await settlementChecker.Campuses.Where(x=>x.Id==sourceCampusId)
                    .ExecuteUpdateAsync(x=>x.SetProperty(c=>c.TimeZoneId,"Invalid/EduOS"));
                Assert.Equal(409,(await paymentCheckerService.PostSettlementAsync(
                    second.Reference,postedReference,confirmation)).StatusCode);
                await settlementChecker.Campuses.Where(x=>x.Id==sourceCampusId)
                    .ExecuteUpdateAsync(x=>x.SetProperty(c=>c.TimeZoneId,(string?)null));
                // Two independent SQL contexts compete to confirm the SAME approved
                // bank-evidenced instruction. Exactly one confirmation may post;
                // the losing request must not create a second GL settlement.
                var competingKey=Guid.NewGuid();
                var attempts=await Task.WhenAll(
                    new[] {confirmation.ClientRequestId,competingKey}.Select(async key =>
                    {
                        await using var competingDb=Open(options,tenantId,checkerId);
                        return await Invoices(competingDb,tenantId,checkerId,atCampusMidnight)
                            .PostSettlementAsync(second.Reference,postedReference,
                                new PostSupplierSettlementRequest {
                                    ClientRequestId=key,RowVersion=approved.Data.RowVersion,
                                    ExecutedDate=postedDate,BankTransactionReference="BANK-SQL-"+suffix });
                    }));
                var posted=Assert.Single(attempts,x=>x.Success);
                Assert.Single(attempts,x=>x.StatusCode==409);
                Assert.Equal("Posted",posted.Data!.State);
                postedVersion=posted.Data.RowVersion;
                await using (var replayDb=Open(options,tenantId,checkerId))
                {
                    var replayService=Invoices(replayDb,tenantId,checkerId,atCampusMidnight);
                    var winningKey=attempts[0].Success?confirmation.ClientRequestId:competingKey;
                    Assert.Equal(postedReference,(await replayService.PostSettlementAsync(
                        second.Reference,postedReference,new PostSupplierSettlementRequest {
                            ClientRequestId=winningKey,RowVersion=approved.Data.RowVersion,
                            ExecutedDate=postedDate,BankTransactionReference="BANK-SQL-"+suffix })).Data!.Reference);
                    Assert.Equal(409,(await replayService.PostSettlementAsync(second.Reference,postedReference,
                        new PostSupplierSettlementRequest {ClientRequestId=Guid.NewGuid(),
                            RowVersion=approved.Data.RowVersion,ExecutedDate=postedDate,
                            BankTransactionReference="DIFFERENT-BANK-"+suffix})).StatusCode);
                    var oneJournal=Assert.Single(await replayDb.Journals.AsNoTracking()
                        .Where(x=>x.PublicId==postedReference && x.State==JournalState.Posted).ToListAsync());
                    var oneSetOfLines=await replayDb.JournalLines.AsNoTracking()
                        .Where(x=>x.JournalId==oneJournal.Id).ToListAsync();
                    Assert.Equal(2,oneSetOfLines.Count);
                    Assert.Equal(6m,oneSetOfLines.Sum(x=>x.DebitAmount));
                    Assert.Equal(6m,oneSetOfLines.Sum(x=>x.CreditAmount));
                }
            }
            await using (var reconciler=Open(options,tenantId,makerId))
            {
                var paymentReportService=Invoices(reconciler,tenantId,makerId,atCampusMidnight);
                var report=(await paymentReportService.AgingAsync(date,null,false,1,100)).Data!;
                var row=Assert.Single(report.Items,x=>x.InvoiceReference==second.Reference);
                Assert.Equal(18m,row.GrossPostedAmount);
                Assert.Equal(6m,row.PostedSettlementAmount);
                Assert.Equal(12m,row.SourceLinkedOutstandingAmount);
                Assert.Equal("SourceLinked",row.SettlementIntegrityStatus);
                // Actual SQL-backed financial export: corrupt one posted GL line
                // inside a disposable rollback transaction. The aging service
                // must expose EvidenceConflict, but CSV must not publish a
                // misleading blank or stale outstanding amount.
                var settlementJournalId = await reconciler.Journals.AsNoTracking()
                    .Where(x => x.PublicId == postedReference).Select(x => x.Id).SingleAsync();
                await using (var corruption = await reconciler.Database.BeginTransactionAsync())
                {
                    Assert.Equal(1, await reconciler.JournalLines
                        .Where(x => x.JournalId == settlementJournalId && x.LineNo == 2)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreditAmount, 5m)));
                    var damaged = (await paymentReportService.AgingAsync(date,null,false,1,100)).Data!;
                    var damagedRow = Assert.Single(damaged.Items,x=>x.InvoiceReference==second.Reference);
                    Assert.Equal("EvidenceConflict",damagedRow.SettlementIntegrityStatus);
                    Assert.Null(damagedRow.SourceLinkedOutstandingAmount);
                    var denied = Assert.IsType<ObjectResult>(
                        await new ProcurementSupplierInvoicesController(paymentReportService)
                            .AgingExport(date,null,false,CancellationToken.None));
                    Assert.Equal(409,denied.StatusCode);
                    await corruption.RollbackAsync();
                }
                var restoredAging = (await paymentReportService.AgingAsync(date,null,false,1,100)).Data!;
                var restoredRow = Assert.Single(restoredAging.Items,x=>x.InvoiceReference==second.Reference);
                Assert.Equal("SourceLinked",restoredRow.SettlementIntegrityStatus);
                Assert.Equal(12m,restoredRow.SourceLinkedOutstandingAmount);
                var traced=await new V3EnterpriseSupplierPayableTraceService(
                    reconciler,new Actor(tenantId,makerId)).GetAsync(second.Reference);
                Assert.True(traced.Success,traced.Message);
                Assert.Equal(6m,traced.Data!.SettlementPostedNetAmount);
                Assert.Equal(12m,traced.Data.SourceLinkedOutstandingAmount);
                var reversalKey=Guid.NewGuid();
                var reversalReason="Incorrect bank instruction requires full accounting contra";
                var reversalCommand=new RequestSupplierSettlementReversal {
                    ClientRequestId=reversalKey,OriginalRowVersion=postedVersion,
                    AccountingPeriodId=periodId,ReversalDate=postedDate,Reason=reversalReason };
                var contra=await paymentReportService.RequestSettlementReversalAsync(
                    second.Reference,postedReference,reversalCommand);
                Assert.True(contra.Success,contra.Message);
                Assert.Equal("Submitted",contra.Data!.ReversalState);
                var pendingReplay=await paymentReportService.RequestSettlementReversalAsync(
                    second.Reference,postedReference,reversalCommand);
                Assert.True(pendingReplay.Success,pendingReplay.Message);
                Assert.Equal(contra.Data.ReversalReference,pendingReplay.Data!.ReversalReference);
                Assert.Equal(409,(await paymentReportService.RequestSettlementReversalAsync(
                    second.Reference,postedReference,new RequestSupplierSettlementReversal {
                        ClientRequestId=reversalKey,OriginalRowVersion=postedVersion,
                        AccountingPeriodId=periodId,ReversalDate=postedDate,
                        Reason="A different reason cannot reuse the same reversal command key" })).StatusCode);
                Assert.Equal(409,(await paymentReportService.RequestSettlementReversalAsync(second.Reference,postedReference,
                    new RequestSupplierSettlementReversal {ClientRequestId=Guid.NewGuid(),
                        OriginalRowVersion=postedVersion,AccountingPeriodId=periodId,
                        ReversalDate=postedDate,Reason="Duplicate pending contra must not be recorded"})).StatusCode);
                await using var reversalChecker=Open(options,tenantId,checkerId);
                var decision=await Invoices(reversalChecker,tenantId,checkerId)
                    .ReviewSettlementReversalAsync(second.Reference,postedReference,
                        contra.Data.ReversalReference!.Value,new ReviewSupplierSettlementReversal {
                            RowVersion=contra.Data.ReversalRowVersion!,Approve=true,
                            Reason="Independent auditor verifies full original GL line mirror" });
                Assert.True(decision.Success,decision.Message);
                Assert.Equal("Posted",decision.Data!.ReversalState);
                // An independent checker cannot claim the original maker's key,
                // even when all source-linked values match.
                Assert.Equal(409,(await Invoices(reversalChecker,tenantId,checkerId)
                    .RequestSettlementReversalAsync(second.Reference,postedReference,
                        reversalCommand)).StatusCode);
                await using var makerReplay=Open(options,tenantId,makerId);
                var postedReplay=await Invoices(makerReplay,tenantId,makerId,atCampusMidnight)
                    .RequestSettlementReversalAsync(second.Reference,postedReference,
                        reversalCommand);
                Assert.True(postedReplay.Success,postedReplay.Message);
                Assert.Equal("Posted",postedReplay.Data!.ReversalState);
                Assert.Equal(contra.Data.ReversalReference,postedReplay.Data.ReversalReference);
                Assert.Equal(409,(await Invoices(makerReplay,tenantId,makerId,atCampusMidnight)
                    .RequestSettlementReversalAsync(second.Reference,postedReference,
                        new RequestSupplierSettlementReversal {
                            ClientRequestId=reversalKey,OriginalRowVersion=postedVersion,
                            AccountingPeriodId=periodId,ReversalDate=postedDate,
                            Reason="A changed reason after approval must not replay" })).StatusCode);
                var contraJournal=Assert.Single(await makerReplay.Journals.AsNoTracking()
                    .Where(x=>x.SourceType=="ProcurementSupplierPaymentReversal" &&
                        x.ReversalOfJournalId.HasValue &&
                        x.PublicId==contra.Data.ReversalReference).ToListAsync());
                Assert.Equal(JournalState.Posted,contraJournal.State);
                Assert.Equal(reversalReason+" | Independent reversal decision: "+
                    "Independent auditor verifies full original GL line mirror",
                    contraJournal.Description);
                var sourceJournal=Assert.Single(await makerReplay.Journals.AsNoTracking()
                    .Where(x=>x.PublicId==postedReference).ToListAsync());
                Assert.Equal("BANK-SQL-"+suffix,sourceJournal.ReferenceNumber);
            }
            await using (var reconciled=Open(options,tenantId,makerId))
            {
                var finalPaymentService=Invoices(reconciled,tenantId,makerId);
                var report=(await finalPaymentService.AgingAsync(date,null,false,1,100)).Data!;
                var row=Assert.Single(report.Items,x=>x.InvoiceReference==second.Reference);
                Assert.Equal(0m,row.PostedSettlementAmount);
                Assert.Equal(18m,row.SourceLinkedOutstandingAmount);
                Assert.Equal(0m,(await finalPaymentService.SettlementOptionsAsync(second.Reference)).Data!.ReservedAmount);
                var traced=await new V3EnterpriseSupplierPayableTraceService(
                    reconciled,new Actor(tenantId,makerId)).GetAsync(second.Reference);
                Assert.True(traced.Success,traced.Message);
                Assert.Equal(0m,traced.Data!.SettlementPostedNetAmount);
                Assert.Contains(traced.Data.SettlementInstructions,x=>x.PostedReversalReference.HasValue);
            }
            // Physical return: a separate maker/checker credit note must emit
            // exactly one stock issue and one balanced BDT journal, then flow
            // into outstanding AP and the canonical enterprise trace.
            Guid returnReference;
            string returnVersion;
            await using (var returnMaker=Open(options,tenantId,makerId))
            {
                var returnService=Invoices(returnMaker,tenantId,makerId);
                var currentInvoice=(await returnService.GetAsync(second.Reference)).Data!;
                var line=Assert.Single(currentInvoice.Lines);
                var request=new CreateSupplierReturnCreditRequest {
                    ClientRequestId=Guid.NewGuid(),InvoiceRowVersion=currentInvoice.RowVersion,
                    SupplierInvoiceLineId=line.Id,Quantity=1m,Mode="Automatic",
                    ReturnDate=date,AccountingPeriodId=periodId,
                    VarianceAccountId=varianceAccountId,
                    SupplierReturnReference="RTN-SQL-"+suffix,
                    Reason="One damaged stock unit physically sent back to vendor" };
                var draft=await returnService.RequestReturnCreditAsync(second.Reference,request);
                Assert.True(draft.Success,draft.Message);
                Assert.Equal("Submitted",draft.Data!.State);
                Assert.Equal(9m,draft.Data.CreditAmount);
                Assert.Equal(10m,draft.Data.StockValue);
                Assert.Equal(draft.Data.Reference,(await returnService.RequestReturnCreditAsync(
                    second.Reference,request)).Data!.Reference);
                returnReference=draft.Data.Reference;returnVersion=draft.Data.RowVersion;
            }
            await using (var returnChecker=Open(options,tenantId,checkerId))
            {
                var returnService=Invoices(returnChecker,tenantId,checkerId);
                var posted=await returnService.ReviewReturnCreditAsync(second.Reference,returnReference,
                    new ReviewSupplierReturnCreditRequest {RowVersion=returnVersion,Approve=true,
                        Reason="Independent review of supplier delivery, inventory and Finance contra"});
                Assert.True(posted.Success,posted.Message);
                Assert.Equal("Posted",posted.Data!.State);
                Assert.Equal(9m,posted.Data.UnpaidCredit);
                Assert.Equal(0m,posted.Data.RefundReceivable);
                Assert.NotNull(posted.Data.StockMovementReference);
                Assert.NotNull(posted.Data.FinanceJournalReference);
                Assert.Equal(409,(await returnService.ReviewReturnCreditAsync(
                    second.Reference,returnReference,new ReviewSupplierReturnCreditRequest {
                        RowVersion=returnVersion,Approve=true,
                        Reason="Attempt to double post a single approved supplier stock return" })).StatusCode);
            }
            await using (var creditReport=Open(options,tenantId,makerId))
            {
                var returnService=Invoices(creditReport,tenantId,makerId);
                var report=(await returnService.AgingAsync(date,null,false,1,100)).Data!;
                var row=Assert.Single(report.Items,x=>x.InvoiceReference==second.Reference);
                Assert.Equal(9m,row.PostedSupplierCreditAmount);
                Assert.Equal(9m,row.SourceLinkedOutstandingAmount);
                var issue=Assert.Single(await creditReport.InventoryMovements.Where(x=>
                    x.SourceType=="ProcurementSupplierReturn" && x.StateCode=="Posted")
                    .ToListAsync());
                Assert.Equal("Issue",issue.MovementTypeCode);
                var journal=Assert.Single(await creditReport.Journals.Where(x=>
                    x.SourceType=="ProcurementSupplierReturnCredit" &&
                    x.State==JournalState.Posted).ToListAsync());
                var returnJournalLines=await creditReport.JournalLines.Where(x=>x.JournalId==journal.Id).ToListAsync();
                Assert.Equal(10m,returnJournalLines.Sum(x=>x.DebitAmount));
                Assert.Equal(10m,returnJournalLines.Sum(x=>x.CreditAmount));
                Assert.Equal(409,(await returnService.RequestReversalAsync(second.Reference,
                    new RequestProcurementSupplierInvoiceReversal {ClientRequestId=Guid.NewGuid(),
                        RowVersion=(await returnService.GetAsync(second.Reference)).Data!.RowVersion,
                        AccountingPeriodId=periodId,ReversalDate=date,
                        Reason="Partial supplier credit cannot disappear in whole invoice reversal"})).StatusCode);
            }
            // Cross-domain acceptance on this SAME disposable receipt/invoice/settlement/return:
            // one remaining physical unit is recognized as a serialized fixed asset.
            // This is not a new stock/GL implementation or a second fabricated receipt.
            Guid assetReference;
            long[] capitalizationAccounts;
            await using (var assetSetup=Open(options,tenantId,makerId))
            {
                var receiptLine=await assetSetup.ProcurementGoodsReceiptLines.AsNoTracking()
                    .SingleAsync(x=>x.GoodsReceiptId==assetSetup.ProcurementGoodsReceipts
                        .Where(r=>r.PublicId==receiptReference).Select(r=>r.Id).Single());
                var campusId=await assetSetup.InventoryLocations.AsNoTracking()
                    .Where(x=>x.Id==locationId).Select(x=>x.CampusId).SingleAsync();
                var category=new AssetCategory {TenantId=tenantId,Code="SI-AC-"+suffix,
                    Name="Serialized equipment from paid supplier"};
                assetSetup.AssetCategories.Add(category);
                // This campus-scoped Accountant has access to a different campus,
                // not the source campus of the serialized fixed asset.
                var otherCampus = new Campus { TenantId=tenantId,
                    Code="SI-OTHER-"+suffix, Name="Other campus", IsActive=true };
                assetSetup.Campuses.Add(otherCampus);
                await assetSetup.SaveChangesAsync();
                assetSetup.UserCampusAccesses.Add(new UserCampusAccess {
                    TenantId=tenantId, UserId=makerId, CampusId=otherCampus.Id,
                    IsActive=true, IsDefault=true });
                await assetSetup.SaveChangesAsync();
                var asset=new Asset {TenantId=tenantId,CampusId=campusId,
                    AssetCategoryId=category.Id,InventoryItemId=receiptLine.InventoryItemId,
                    InventoryLocationId=locationId,AssetTag="SI-A-"+suffix,
                    SerialNumber="SI-SERIAL-"+suffix,Name="Receipt-linked equipment",
                    PurchaseCost=10m,PurchaseDate=date,StateCode="Available"};
                assetSetup.Assets.Add(asset);
                var gl=new[]
                {
                    ("Fixed",AccountType.Asset),("Accumulated",AccountType.Asset),
                    ("Depreciation",AccountType.Expense),("Impairment",AccountType.Expense),
                    ("Disposal gain",AccountType.Income),("Disposal loss",AccountType.Expense),
                    ("Proceeds clearing",AccountType.Asset)
                }.Select((x,i)=>new Account {TenantId=tenantId,Code="SI-CAP-"+i+"-"+suffix,
                    Name=x.Item1,AccountType=x.Item2,IsActive=true,AllowsPosting=true}).ToArray();
                assetSetup.Accounts.AddRange(gl);
                await assetSetup.SaveChangesAsync();
                assetReference=asset.PublicId;
                capitalizationAccounts=[gl[0].Id,assetAccountId,gl[1].Id,gl[2].Id,
                    gl[3].Id,gl[4].Id,gl[5].Id,gl[6].Id];
            }
            Guid capitalizationReference;
            string capitalizationVersion;
            await using (var assetMaker=Open(options,tenantId,makerId))
            {
                var finance=AssetFinance(assetMaker,tenantId,makerId);
                var readiness=await new AssetLifecycleService(assetMaker,
                    new Actor(tenantId,makerId),TimeProvider.System,
                    NullLogger<AssetLifecycleService>.Instance)
                    .GetCapitalizationReadinessAsync(assetReference);
                Assert.True(readiness.Success,readiness.Message);
                Assert.Contains(readiness.Data!.Candidates,x=>
                    x.ReceiptReference==receiptReference && x.LineNo==1);
                var request=new CreateAssetCapitalizationRequest {
                    ClientRequestId=Guid.NewGuid(),AssetRowVersion=readiness.Data.AssetRowVersion,
                    ReceiptReference=receiptReference,ReceiptLineNo=1,ReceiptUnitOrdinal=1,
                    AccountingPeriodId=periodId,CapitalizationDate=date,InServiceDate=date,
                    PolicyMode="Automatic",ProposedCostBasis=0m,SalvageValue=0m,
                    DepreciationMethod="StraightLine",UsefulLifeMonths=36,PolicyVersion=1,
                    FixedAssetAccountId=capitalizationAccounts[0],
                    InventoryAccountId=capitalizationAccounts[1],
                    AccumulatedDepreciationAccountId=capitalizationAccounts[2],
                    DepreciationExpenseAccountId=capitalizationAccounts[3],
                    ImpairmentExpenseAccountId=capitalizationAccounts[4],
                    DisposalGainAccountId=capitalizationAccounts[5],
                    DisposalLossAccountId=capitalizationAccounts[6],
                    Reason="Independent serialized unit and source-linked supplier GL reconciliation"
                };
                var submitted=await finance.RequestCapitalizationAsync(assetReference,request);
                Assert.True(submitted.Success,submitted.Message);
                Assert.Equal(10m,submitted.Data!.ProposedCostBasis);
                capitalizationReference=submitted.Data.Reference;
                capitalizationVersion=submitted.Data.RowVersion;
                Assert.Equal(capitalizationReference,(await finance.RequestCapitalizationAsync(
                    assetReference,request)).Data!.Reference);
                var limitedActor=new CampusAccountantActor(tenantId,makerId);
                var limitedFinance=new AssetAccountingService(assetMaker,limitedActor,TimeProvider.System,
                    new InventoryMovementService(assetMaker,limitedActor,TimeProvider.System,
                        NullLogger<InventoryMovementService>.Instance),
                    new AssetLifecycleService(assetMaker,limitedActor,TimeProvider.System,
                        NullLogger<AssetLifecycleService>.Instance),
                    NullLogger<AssetAccountingService>.Instance);
                Assert.Equal(404,(await limitedFinance.GetCapitalizationAsync(assetReference)).StatusCode);
                Assert.Equal(404,(await limitedFinance.RequestCapitalizationAsync(
                    assetReference,request)).StatusCode);
                // Real SQL campus revocation acceptance for Supplier AP read
                // surfaces, using the same physical source campus as this asset.
                var limitedInvoices = new ProcurementSupplierInvoiceService(assetMaker,limitedActor,
                    new InventoryMovementService(assetMaker,limitedActor,TimeProvider.System,
                        NullLogger<InventoryMovementService>.Instance),
                    TimeProvider.System,NullLogger<ProcurementSupplierInvoiceService>.Instance);
                Assert.Equal(404,(await limitedInvoices.GetAsync(second.Reference)).StatusCode);
                Assert.Empty((await limitedInvoices.ListAsync(null,null,1,25)).Data!.Items);
                Assert.Empty((await limitedInvoices.AgingAsync(date,null,false,1,25)).Data!.Items);
                assetMaker.UserCampusAccesses.Add(new UserCampusAccess {
                    TenantId=tenantId,UserId=makerId,CampusId=campusId,
                    IsActive=true,IsDefault=false });
                await assetMaker.SaveChangesAsync();
                Assert.Equal(200,(await limitedInvoices.GetAsync(second.Reference)).StatusCode);
                Assert.Contains((await limitedInvoices.ListAsync(null,null,1,25)).Data!.Items,
                    x=>x.Reference==second.Reference);
                Assert.Contains((await limitedInvoices.AgingAsync(date,null,false,1,25)).Data!.Items,
                    x=>x.InvoiceReference==second.Reference);
                Assert.Equal(1,await assetMaker.UserCampusAccesses
                    .Where(x=>x.TenantId==tenantId && x.UserId==makerId && x.CampusId==campusId)
                    .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsActive,false)));
                Assert.Equal(404,(await limitedInvoices.GetAsync(second.Reference)).StatusCode);
                Assert.Empty((await limitedInvoices.ListAsync(null,null,1,25)).Data!.Items);
                Assert.Empty((await limitedInvoices.AgingAsync(date,null,false,1,25)).Data!.Items);
                Assert.Single(await assetMaker.AssetCapitalizationCommands.AsNoTracking()
                    .Where(x=>x.PublicId==capitalizationReference).ToListAsync());
                Assert.Equal(403,(await finance.ReviewCapitalizationAsync(capitalizationReference,
                    new ReviewAssetCapitalizationRequest {RowVersion=capitalizationVersion,
                        Approve=true,Reason="Maker must not capitalize own supplier equipment"})).StatusCode);
            }
            {
                // Competing independent approvals use separate SQL connections.
                // Only one may bind this receipt ordinal and post its stock/GL.
                var competingReviews=await Task.WhenAll(Enumerable.Range(0,2).Select(async _ =>
                {
                    await using var competingDb=Open(options,tenantId,checkerId);
                    return await AssetFinance(competingDb,tenantId,checkerId)
                        .ReviewCapitalizationAsync(capitalizationReference,
                            new ReviewAssetCapitalizationRequest {RowVersion=capitalizationVersion,
                                Approve=true,Reason="Independent fixed asset GL and physical stock approval"});
                }));
                var posted=Assert.Single(competingReviews,x=>x.Success);
                Assert.Single(competingReviews,x=>x.StatusCode==409);
                Assert.True(posted.Success,posted.Message);
                Assert.Equal("Posted",posted.Data!.State);
                Assert.Equal(10m,posted.Data.PostedStockCost);
                // Open a NEW verifier after both parallel contexts complete.
                // HttpContextAccessor clears prior ambient holders on replacement;
                // a pre-opened verifier would silently lose its tenant filter.
                await using var assetChecker=Open(options,tenantId,checkerId);
                var book=Assert.Single(await assetChecker.AssetBooks.AsNoTracking()
                    .Where(x=>x.AssetId==assetChecker.Assets.Where(a=>a.PublicId==assetReference)
                        .Select(a=>a.Id).Single()).ToListAsync());
                Assert.Equal(10m,book.CostBasis);
                var movement=Assert.Single(await assetChecker.InventoryMovements.AsNoTracking()
                    .Where(x=>x.SourceType=="AssetCapitalization" && x.StateCode=="Posted")
                    .ToListAsync());
                Assert.Equal("Issue",movement.MovementTypeCode);
                var journal=Assert.Single(await assetChecker.Journals.AsNoTracking()
                    .Where(x=>x.SourceType=="AssetCapitalization" &&
                        x.State==JournalState.Posted).ToListAsync());
                var capitalizationJournalLines=await assetChecker.JournalLines.AsNoTracking()
                    .Where(x=>x.JournalId==journal.Id).ToListAsync();
                Assert.Equal(10m,capitalizationJournalLines.Sum(x=>x.DebitAmount));
                Assert.Equal(10m,capitalizationJournalLines.Sum(x=>x.CreditAmount));
                Assert.Equal(409,(await AssetFinance(assetChecker,tenantId,checkerId)
                    .ReviewCapitalizationAsync(capitalizationReference,
                        new ReviewAssetCapitalizationRequest {RowVersion=capitalizationVersion,
                            Approve=true,Reason="Repeat must not create a second stock or GL posting"})).StatusCode);
            }

            // Competing different asset registrations claim the same receipt ordinal.
            // The existing original asset has already consumed ordinal 1; these two
            // independent makers race for ordinal 2 of the SAME posted GR line.
            Guid[] competingAssetReferences;
            await using (var candidateSetup=Open(options,tenantId,makerId))
            {
                var originalAsset=await candidateSetup.Assets.AsNoTracking()
                    .SingleAsync(x=>x.PublicId==assetReference);
                var candidates=Enumerable.Range(0,2).Select(i=>new Asset {
                    TenantId=tenantId,PublicId=Guid.NewGuid(),CampusId=originalAsset.CampusId,
                    AssetCategoryId=originalAsset.AssetCategoryId,
                    InventoryItemId=originalAsset.InventoryItemId,
                    InventoryLocationId=originalAsset.InventoryLocationId,
                    AssetTag="SI-RACE-"+i+"-"+suffix,
                    SerialNumber="SI-RACE-SERIAL-"+i+"-"+suffix,
                    Name="Competing receipt unit registration "+i,
                    PurchaseCost=10m,PurchaseDate=date,StateCode="Available"
                }).ToArray();
                candidateSetup.Assets.AddRange(candidates);
                await candidateSetup.SaveChangesAsync();
                competingAssetReferences=candidates.Select(x=>x.PublicId).ToArray();
            }
            var ordinalRace=await Task.WhenAll(competingAssetReferences.Select(async candidateReference =>
            {
                await using var contender=Open(options,tenantId,makerId);
                var readiness=await new AssetLifecycleService(contender,
                    new Actor(tenantId,makerId),TimeProvider.System,
                    NullLogger<AssetLifecycleService>.Instance)
                    .GetCapitalizationReadinessAsync(candidateReference);
                Assert.True(readiness.Success,readiness.Message);
                Assert.Contains(readiness.Data!.Candidates,x=>
                    x.ReceiptReference==receiptReference && x.LineNo==1);
                var input=new CreateAssetCapitalizationRequest {
                    ClientRequestId=Guid.NewGuid(),
                    AssetRowVersion=readiness.Data.AssetRowVersion,
                    ReceiptReference=receiptReference,ReceiptLineNo=1,ReceiptUnitOrdinal=2,
                    AccountingPeriodId=periodId,CapitalizationDate=date,InServiceDate=date,
                    PolicyMode="Automatic",ProposedCostBasis=0m,SalvageValue=0m,
                    DepreciationMethod="StraightLine",UsefulLifeMonths=36,PolicyVersion=1,
                    FixedAssetAccountId=capitalizationAccounts[0],
                    InventoryAccountId=capitalizationAccounts[1],
                    AccumulatedDepreciationAccountId=capitalizationAccounts[2],
                    DepreciationExpenseAccountId=capitalizationAccounts[3],
                    ImpairmentExpenseAccountId=capitalizationAccounts[4],
                    DisposalGainAccountId=capitalizationAccounts[5],
                    DisposalLossAccountId=capitalizationAccounts[6],
                    Reason="Distinct registered assets must not reserve the same receipt unit"
                };
                var response=await AssetFinance(contender,tenantId,makerId)
                    .RequestCapitalizationAsync(candidateReference,input);
                return (AssetReference:candidateReference,Input:input,Result:response);
            }));
            var ordinalWinner=Assert.Single(ordinalRace,x=>x.Result.Success);
            var ordinalLoser=Assert.Single(ordinalRace,x=>x.Result.StatusCode==409);
            Assert.NotEqual(ordinalWinner.AssetReference,ordinalLoser.AssetReference);
            await using (var ordinalVerifier=Open(options,tenantId,checkerId))
            {
                var receiptLineId=await ordinalVerifier.ProcurementGoodsReceiptLines.AsNoTracking()
                    .Where(x=>x.GoodsReceiptId==ordinalVerifier.ProcurementGoodsReceipts
                        .Where(r=>r.PublicId==receiptReference).Select(r=>r.Id).Single())
                    .Select(x=>x.Id).SingleAsync();
                Assert.Single(await ordinalVerifier.AssetCapitalizationCommands.AsNoTracking()
                    .Where(x=>x.GoodsReceiptLineId==receiptLineId &&
                        x.ReceiptUnitOrdinal==2 && x.StateCode=="Submitted").ToListAsync());
                Assert.Equal(1,await ordinalVerifier.AssetBooks.CountAsync());
                Assert.Equal(1,await ordinalVerifier.InventoryMovements.CountAsync(x=>
                    x.SourceType=="AssetCapitalization" && x.StateCode=="Posted"));
                Assert.Equal(1,await ordinalVerifier.Journals.CountAsync(x=>
                    x.SourceType=="AssetCapitalization" && x.State==JournalState.Posted));
            }
            await using (var independentChecker=Open(options,tenantId,checkerId))
            {
                var pending=ordinalWinner.Result.Data!;
                var rejected=await AssetFinance(independentChecker,tenantId,checkerId)
                    .ReviewCapitalizationAsync(pending.Reference,new ReviewAssetCapitalizationRequest {
                        RowVersion=pending.RowVersion,Approve=false,
                        Reason="Reject competing unit reservation without issuing stock or GL"
                    });
                Assert.True(rejected.Success,rejected.Message);
                Assert.Equal("Rejected",rejected.Data!.State);
            }
            // A rejected unposted reservation must release ordinal 2. The former
            // losing asset can then request it, and independent rejection again
            // must leave canonical stock, asset books and posted GL unchanged.
            Guid releasedOrdinalReference;
            string releasedOrdinalVersion;
            await using (var releasedMaker=Open(options,tenantId,makerId))
            {
                var retried=await AssetFinance(releasedMaker,tenantId,makerId)
                    .RequestCapitalizationAsync(ordinalLoser.AssetReference,ordinalLoser.Input);
                Assert.True(retried.Success,retried.Message);
                releasedOrdinalReference=retried.Data!.Reference;
                releasedOrdinalVersion=retried.Data.RowVersion;
            }
            await using (var releasedChecker=Open(options,tenantId,checkerId))
            {
                var rejected=await AssetFinance(releasedChecker,tenantId,checkerId)
                    .ReviewCapitalizationAsync(releasedOrdinalReference,new ReviewAssetCapitalizationRequest {
                        RowVersion=releasedOrdinalVersion,Approve=false,
                        Reason="Release unposted alternate serialized unit without stock issue"
                    });
                Assert.True(rejected.Success,rejected.Message);
            }
            await using (var finalOrdinalVerifier=Open(options,tenantId,checkerId))
            {
                Assert.Equal(2,await finalOrdinalVerifier.AssetCapitalizationCommands.CountAsync(x=>
                    x.ReceiptUnitOrdinal==2 && x.StateCode=="Rejected"));
                Assert.Equal(1,await finalOrdinalVerifier.AssetBooks.CountAsync());
                Assert.Equal(1,await finalOrdinalVerifier.InventoryMovements.CountAsync(x=>
                    x.SourceType=="AssetCapitalization" && x.StateCode=="Posted"));
                Assert.Equal(1,await finalOrdinalVerifier.Journals.CountAsync(x=>
                    x.SourceType=="AssetCapitalization" && x.State==JournalState.Posted));
            }
            // Inject a deterministic failure AFTER Inventory has written the
            // draft/line/posted asset issue inside the review transaction.
            // Manual BDT 11 valuation of BDT 10 stock requires a gain account;
            // deliberately omit it. The service must roll back the stock issue,
            // AssetBook and Finance journal, not leave a partial capitalization.
            Guid failedValuationReference;
            string failedValuationVersion;
            Guid failedValuationKey;
            await using (var faultMaker=Open(options,tenantId,makerId))
            {
                var faultInput=ordinalLoser.Input;
                failedValuationKey=Guid.NewGuid();
                faultInput.ClientRequestId=failedValuationKey;
                faultInput.PolicyMode="ManualFinal";
                faultInput.ProposedCostBasis=11m;
                faultInput.VarianceGainAccountId=null;
                faultInput.Reason="Missing valuation gain account after trusted stock issue";
                var submitted=await AssetFinance(faultMaker,tenantId,makerId)
                    .RequestCapitalizationAsync(ordinalLoser.AssetReference,faultInput);
                Assert.True(submitted.Success,submitted.Message);
                Assert.Equal("Submitted",submitted.Data!.State);
                failedValuationReference=submitted.Data.Reference;
                failedValuationVersion=submitted.Data.RowVersion;
            }
            await using (var faultChecker=Open(options,tenantId,checkerId))
            {
                var denied=await AssetFinance(faultChecker,tenantId,checkerId)
                    .ReviewCapitalizationAsync(failedValuationReference,
                        new ReviewAssetCapitalizationRequest {
                            RowVersion=failedValuationVersion,Approve=true,
                            Reason="Verify atomic stock and Finance rollback after valuation failure"
                        });
                Assert.Equal(409,denied.StatusCode);
                Assert.Contains("Manual cost difference requires",denied.Message);
            }
            // A NEW SQL context is essential: the failed review's EF change
            // tracker may still contain rolled-back stock movement entities.
            await using (var rollbackVerifier=Open(options,tenantId,makerId))
            {
                var pending=await rollbackVerifier.AssetCapitalizationCommands.AsNoTracking()
                    .SingleAsync(x=>x.PublicId==failedValuationReference);
                Assert.Equal("Submitted",pending.StateCode);
                Assert.Null(pending.AssetBookId);
                Assert.Null(pending.StockMovementId);
                Assert.Null(pending.JournalId);
                Assert.False(await rollbackVerifier.InventoryMovements.AsNoTracking()
                    .AnyAsync(x=>x.ClientRequestId==failedValuationKey));
                Assert.False(await rollbackVerifier.Journals.AsNoTracking()
                    .AnyAsync(x=>x.IdempotencyKey==failedValuationKey));
                Assert.Equal(1,await rollbackVerifier.AssetBooks.CountAsync());
                Assert.Equal(1,await rollbackVerifier.InventoryMovements.CountAsync(x=>
                    x.SourceType=="AssetCapitalization" && x.StateCode=="Posted"));
                Assert.Equal(1,await rollbackVerifier.Journals.CountAsync(x=>
                    x.SourceType=="AssetCapitalization" && x.State==JournalState.Posted));
            }
            // Release the failed, still-unposted reservation via independent
            // review; its history remains immutable and cannot be posted later.
            await using (var faultRejector=Open(options,tenantId,checkerId))
            {
                var rejected=await AssetFinance(faultRejector,tenantId,checkerId)
                    .ReviewCapitalizationAsync(failedValuationReference,
                        new ReviewAssetCapitalizationRequest {
                            RowVersion=failedValuationVersion,Approve=false,
                            Reason="Release failed valuation request without stock or GL"
                        });
                Assert.True(rejected.Success,rejected.Message);
                Assert.Equal("Rejected",rejected.Data!.State);
            }
            await using (var afterFault=Open(options,tenantId,makerId))
            {
                Assert.Equal(3,await afterFault.AssetCapitalizationCommands.CountAsync(x=>
                    x.ReceiptUnitOrdinal==2 && x.StateCode=="Rejected"));
                Assert.Equal(1,await afterFault.AssetBooks.CountAsync());
                Assert.Equal(1,await afterFault.InventoryMovements.CountAsync(x=>
                    x.SourceType=="AssetCapitalization" && x.StateCode=="Posted"));
                Assert.Equal(1,await afterFault.Journals.CountAsync(x=>
                    x.SourceType=="AssetCapitalization" && x.State==JournalState.Posted));
            }

            // Continue this SAME PO/GR/invoice/return/asset-unit fixture through
            // real month-end depreciation, disposal and balanced GL compensation.
            // Synthetic time avoids a test that only passes on the last day of a month.
            var valuationDate=new DateOnly(date.Year,date.Month,1).AddMonths(2).AddDays(-1);
            var valuationClock=new FrozenClock(new DateTimeOffset(
                valuationDate.ToDateTime(new TimeOnly(12,0),DateTimeKind.Utc)));
            var depreciationInput=new AssetValuationRequest {
                ClientRequestId=Guid.NewGuid(),AccountingPeriodId=periodId,
                EffectiveDate=valuationDate,ValuationMode="Automatic",Amount=0m,
                Reason="Source-linked first month-end straight-line depreciation" };
            long depreciationId;
            string depreciationVersion;
            await using (var closed=Open(options,tenantId,makerId))
            {
                await closed.AccountingPeriods.Where(x=>x.Id==periodId)
                    .ExecuteUpdateAsync(x=>x.SetProperty(p=>p.IsClosed,true));
                Assert.Equal(409,(await AssetFinanceAt(closed,tenantId,makerId,valuationClock)
                    .RequestDepreciationAsync(assetReference,depreciationInput)).StatusCode);
                Assert.Empty(await closed.AssetDepreciationRuns.AsNoTracking().ToListAsync());
                await closed.AccountingPeriods.Where(x=>x.Id==periodId)
                    .ExecuteUpdateAsync(x=>x.SetProperty(p=>p.IsClosed,false));
            }
            await using (var maker=Open(options,tenantId,makerId))
            {
                var finance=AssetFinanceAt(maker,tenantId,makerId,valuationClock);
                var draft=await finance.RequestDepreciationAsync(assetReference,depreciationInput);
                Assert.True(draft.Success,draft.Message);
                Assert.Equal(0.28m,draft.Data!.Amount);
                depreciationId=draft.Data.Id;depreciationVersion=draft.Data.RowVersion;
                Assert.Equal(depreciationId,(await finance.RequestDepreciationAsync(
                    assetReference,depreciationInput)).Data!.Id);
                Assert.Equal(409,(await finance.ReviewDepreciationAsync(depreciationId,
                    new AssetReviewRequest {RowVersion=depreciationVersion,Approve=true,
                        Reason="Maker cannot approve source-linked depreciation"})).StatusCode);
            }
            await using (var checker=Open(options,tenantId,checkerId))
            {
                var finance=AssetFinanceAt(checker,tenantId,checkerId,valuationClock);
                var posted=await finance.ReviewDepreciationAsync(depreciationId,
                    new AssetReviewRequest {RowVersion=depreciationVersion,Approve=true,
                        Reason="Independent month-end Finance depreciation approval"});
                Assert.True(posted.Success,posted.Message);
                Assert.Equal("Posted",posted.Data!.State);
                Assert.Equal(409,(await finance.ReviewDepreciationAsync(depreciationId,
                    new AssetReviewRequest {RowVersion=depreciationVersion,Approve=true,
                        Reason="Prevent duplicate month-end depreciation journal"})).StatusCode);
                var journal=Assert.Single(await checker.Journals.AsNoTracking()
                    .Where(x=>x.SourceType=="AssetDepreciation" && x.State==JournalState.Posted)
                    .ToListAsync());
                var depreciationLines=await checker.JournalLines.AsNoTracking()
                    .Where(x=>x.JournalId==journal.Id).ToListAsync();
                Assert.Equal(0.28m,depreciationLines.Sum(x=>x.DebitAmount));
                Assert.Equal(0.28m,depreciationLines.Sum(x=>x.CreditAmount));
                var statement=(await finance.StatementAsync(assetReference)).Data!;
                Assert.Equal(10m,statement.CostBasis);
                Assert.Equal(0.28m,statement.PostedDepreciation);
                Assert.Equal(9.72m,statement.CarryingValue);
            }
            long disposalId;
            string disposalVersion;
            await using (var locked=Open(options,tenantId,makerId))
            {
                await locked.AccountingPeriods.Where(x=>x.Id==periodId)
                    .ExecuteUpdateAsync(x=>x.SetProperty(p=>p.IsClosed,true));
                var denied=await AssetFinanceAt(locked,tenantId,makerId,valuationClock)
                    .RequestDisposalAsync(assetReference,new AssetValuationRequest {
                        ClientRequestId=Guid.NewGuid(),AccountingPeriodId=periodId,
                        EffectiveDate=valuationDate,ValuationMode="ManualFinal",Amount=5m,
                        ProceedsClearingAccountId=capitalizationAccounts[7],
                        Reason="Closed period must block source-linked disposal"});
                Assert.Equal(409,denied.StatusCode);
                Assert.Empty(await locked.AssetDisposals.AsNoTracking().ToListAsync());
                await locked.AccountingPeriods.Where(x=>x.Id==periodId)
                    .ExecuteUpdateAsync(x=>x.SetProperty(p=>p.IsClosed,false));
            }
            await using (var maker=Open(options,tenantId,makerId))
            {
                var finance=AssetFinanceAt(maker,tenantId,makerId,valuationClock);
                var requested=await finance.RequestDisposalAsync(assetReference,
                    new AssetValuationRequest {ClientRequestId=Guid.NewGuid(),
                        AccountingPeriodId=periodId,EffectiveDate=valuationDate,
                        ValuationMode="ManualFinal",Amount=5m,
                        ProceedsClearingAccountId=capitalizationAccounts[7],
                        Reason="Source-linked approved disposal with clearing evidence"});
                Assert.True(requested.Success,requested.Message);
                disposalId=requested.Data!.Id;disposalVersion=requested.Data.RowVersion;
                Assert.Equal(409,(await finance.ReviewDisposalAsync(disposalId,
                    new AssetReviewRequest {RowVersion=disposalVersion,Approve=true,
                        Reason="Maker cannot independently approve asset disposal"})).StatusCode);
            }
            await using (var checker=Open(options,tenantId,checkerId))
            {
                var finance=AssetFinanceAt(checker,tenantId,checkerId,valuationClock);
                await checker.AccountingPeriods.Where(x=>x.Id==periodId)
                    .ExecuteUpdateAsync(x=>x.SetProperty(p=>p.IsClosed,true));
                var blocked=await finance.ReviewDisposalAsync(disposalId,
                    new AssetReviewRequest {RowVersion=disposalVersion,Approve=true,
                        Reason="Closed period must prevent approval and GL posting"});
                Assert.Equal(409,blocked.StatusCode);
                Assert.Empty(await checker.Journals.AsNoTracking()
                    .Where(x=>x.SourceType=="AssetDisposal").ToListAsync());
                Assert.Equal("Submitted",await checker.AssetDisposals.AsNoTracking()
                    .Where(x=>x.Id==disposalId).Select(x=>x.StateCode).SingleAsync());
                Assert.True(Assert.Single(await checker.AssetBooks.AsNoTracking()
                    .ToListAsync()).IsActive);
                await checker.AccountingPeriods.Where(x=>x.Id==periodId)
                    .ExecuteUpdateAsync(x=>x.SetProperty(p=>p.IsClosed,false));
                var posted=await finance.ReviewDisposalAsync(disposalId,
                    new AssetReviewRequest {RowVersion=disposalVersion,Approve=true,
                        Reason="Independent source-linked disposal and GL clearance"});
                Assert.True(posted.Success,posted.Message);
                Assert.Equal("Posted",posted.Data!.State);
                Assert.Equal(409,(await finance.ReviewDisposalAsync(disposalId,
                    new AssetReviewRequest {RowVersion=disposalVersion,Approve=true,
                        Reason="Prevent duplicate posted disposal journal"})).StatusCode);
                Assert.False(Assert.Single(await checker.AssetBooks.AsNoTracking()
                    .ToListAsync()).IsActive);
                var journal=Assert.Single(await checker.Journals.AsNoTracking()
                    .Where(x=>x.SourceType=="AssetDisposal" && x.State==JournalState.Posted)
                    .ToListAsync());
                var disposalLines=await checker.JournalLines.AsNoTracking()
                    .Where(x=>x.JournalId==journal.Id).ToListAsync();
                Assert.Equal(disposalLines.Sum(x=>x.DebitAmount),
                    disposalLines.Sum(x=>x.CreditAmount));
                Assert.True((await finance.StatementAsync(assetReference)).Data!.Disposed);
            }
            Guid reversalReference;
            string reversalVersion;
            await using (var maker=Open(options,tenantId,makerId))
            {
                var finance=AssetFinanceAt(maker,tenantId,makerId,valuationClock);
                var evt=Assert.Single((await finance.StatementAsync(assetReference)).Data!
                    .Events.Where(x=>x.Kind=="Disposal"));
                var request=await finance.RequestEventReversalAsync("Disposal",disposalId,
                    new AssetReversalRequest {ClientRequestId=Guid.NewGuid(),
                        OriginalRowVersion=evt.RowVersion,AccountingPeriodId=periodId,
                        ReversalDate=valuationDate,
                        Reason="Audited reversal of source-linked disposal evidence"});
                Assert.True(request.Success,request.Message);
                reversalReference=request.Data!.PendingReversalReference!.Value;
                reversalVersion=request.Data.PendingReversalRowVersion!;
            }
            await using (var checker=Open(options,tenantId,checkerId))
            {
                var finance=AssetFinanceAt(checker,tenantId,checkerId,valuationClock);
                var posted=await finance.ReviewEventReversalAsync("Disposal",disposalId,
                    reversalReference,new AssetReviewRequest {RowVersion=reversalVersion,
                        Approve=true,Reason="Independent disposal contra preserving history"});
                Assert.True(posted.Success,posted.Message);
                var original=Assert.Single(await checker.Journals.AsNoTracking()
                    .Where(x=>x.SourceType=="AssetDisposal").ToListAsync());
                var contra=Assert.Single(await checker.Journals.AsNoTracking()
                    .Where(x=>x.PublicId==reversalReference).ToListAsync());
                Assert.Equal(original.Id,contra.ReversalOfJournalId);
                var oldLines=await checker.JournalLines.AsNoTracking()
                    .Where(x=>x.JournalId==original.Id).OrderBy(x=>x.LineNo).ToListAsync();
                var newLines=await checker.JournalLines.AsNoTracking()
                    .Where(x=>x.JournalId==contra.Id).OrderBy(x=>x.LineNo).ToListAsync();
                Assert.Equal(oldLines.Count,newLines.Count);
                for(var i=0;i<oldLines.Count;i++)
                {
                    Assert.Equal(oldLines[i].DebitAmount,newLines[i].CreditAmount);
                    Assert.Equal(oldLines[i].CreditAmount,newLines[i].DebitAmount);
                }
                var statement=(await finance.StatementAsync(assetReference)).Data!;
                Assert.False(statement.Disposed);
                Assert.Equal(0.28m,statement.PostedDepreciation);
                Assert.Equal(9.72m,statement.CarryingValue);
                Assert.True(Assert.Single(await checker.AssetBooks.AsNoTracking()
                    .ToListAsync()).IsActive);
            }
            await using (var combined=Open(options,tenantId,makerId))
            {
                // Supplier return, payable and capitalization are three distinct
                // source-linked postings, not direct mutations of either ledger.
                Assert.Equal(3,await combined.InventoryMovements.AsNoTracking().CountAsync(x=>
                    x.StateCode=="Posted" && (x.SourceType=="ProcurementGoodsReceipt" ||
                    x.SourceType=="ProcurementSupplierReturn" || x.SourceType=="AssetCapitalization")));
                var payable=(await Invoices(combined,tenantId,makerId)
                    .AgingAsync(date,null,false,1,100)).Data!;
                Assert.Equal(9m,Assert.Single(payable.Items,
                    x=>x.InvoiceReference==second.Reference).SourceLinkedOutstandingAmount);
            }
            // Run the foreign-tenant request LAST: the test fixture uses ambient
            // tenant identity and must not poison the original maker context.
            await using (var foreign = Open(options,tenantId+999999,checkerId))
                Assert.Equal(404,(await new V3EnterpriseSupplierPayableTraceService(
                    foreign,new Actor(tenantId+999999,checkerId)).GetAsync(second.Reference)).StatusCode);
        }

    }
}
