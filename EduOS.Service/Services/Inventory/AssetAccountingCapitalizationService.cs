using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

/// <summary>One tenant's explicit stock-to-fixed-asset and canonical GL workflow.
/// Inventory retains the only stock ledger and Finance retains the only GL.</summary>
public sealed partial class AssetAccountingService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    private readonly InventoryMovementService inventory;
    private readonly AssetLifecycleService assets;
    private readonly ILogger<AssetAccountingService> logger;

    public AssetAccountingService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock,
        InventoryMovementService inventory, AssetLifecycleService assets,
        ILogger<AssetAccountingService> logger)
    { this.db=db; this.user=user; this.clock=clock; this.inventory=inventory; this.assets=assets; this.logger=logger; }

    private bool Readable => user.IsAuthenticated && user.TenantId > 0 &&
        db.CurrentTenantId == user.TenantId &&
        (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("Accountant"));
    private bool Maker => Readable && (user.IsTenantAdmin || user.IsInRole("Accountant"));
    private bool Reviewer => Readable && (user.IsTenantAdmin || user.IsInRole("Principal"));
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(Now);
    private static ApiResponse<T> Conflict<T>(string message, int status=409) =>
        ApiResponse<T>.ErrorResponse(message,status);
    private static bool Money(decimal value) => value >= 0m && value <= 999999999999.99m &&
        decimal.Round(value,2)==value;
    private static bool Version(byte[] actual,string? supplied)
    { try { return !string.IsNullOrWhiteSpace(supplied) && actual.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
      catch (FormatException) { return false; } }
    private static bool Contention(Exception e)
    {
        for (Exception? ex=e;ex!=null;ex=ex.InnerException)
            if(ex is SqlException sql && sql.Number is 1205 or 1222 or 2601 or 2627 or 3960)
                return true;
        return false;
    }
    private async Task<bool> CampusAllowedAsync(long campusId,CancellationToken ct) =>
        await db.Campuses.AsNoTracking().AnyAsync(x=>x.TenantId==user.TenantId && x.Id==campusId &&
            x.IsActive && !x.IsDeleted &&
            (user.IsTenantAdmin || user.IsInRole("Principal") || db.UserCampusAccesses.Any(a=>
                a.TenantId==user.TenantId && a.UserId==user.UserId && a.CampusId==campusId &&
                a.IsActive && !a.IsDeleted)),ct);
    private Task<bool> OpenPeriodAsync(long id,DateOnly day,CancellationToken ct) =>
        db.AccountingPeriods.AsNoTracking().AnyAsync(x=>x.TenantId==user.TenantId &&
            x.Id==id && !x.IsDeleted && !x.IsClosed && x.StartDate<=day && x.EndDate>=day,ct);

    private async Task<bool> AccountsValidAsync(AssetCapitalizationCommand c,CancellationToken ct)
    {
        var required = new Dictionary<long, AccountType>();
        bool Add(long id,AccountType type)
        {
            if(id<=0 || required.ContainsKey(id))return false;
            required[id]=type;return true;
        }
        if(!Add(c.FixedAssetAccountId,AccountType.Asset) ||
            !Add(c.InventoryAccountId,AccountType.Asset) ||
            !Add(c.AccumulatedDepreciationAccountId,AccountType.Asset) ||
            !Add(c.DepreciationExpenseAccountId,AccountType.Expense) ||
            !Add(c.ImpairmentExpenseAccountId,AccountType.Expense) ||
            !Add(c.DisposalGainAccountId,AccountType.Income) ||
            !Add(c.DisposalLossAccountId,AccountType.Expense))return false;
        if(c.VarianceGainAccountId.HasValue &&
            !Add(c.VarianceGainAccountId.Value,AccountType.Income))return false;
        if(c.VarianceLossAccountId.HasValue &&
            !Add(c.VarianceLossAccountId.Value,AccountType.Expense))return false;
        var ids=required.Keys.ToArray();
        var rows=await db.Accounts.AsNoTracking().Where(x=>x.TenantId==user.TenantId &&
            ids.Contains(x.Id) && !x.IsDeleted && x.IsActive && !x.IsGroup &&
            x.AllowsPosting && x.CurrencyCode=="BDT")
            .Select(x=>new{x.Id,x.AccountType}).ToListAsync(ct);
        return rows.Count==ids.Length && rows.All(x=>required[x.Id]==x.AccountType);
    }

    private async Task<(Asset? Asset,ProcurementGoodsReceipt? Receipt,
        ProcurementGoodsReceiptLine? Line,string? Error)> SourceAsync(
        Guid assetReference,Guid receiptReference,int lineNo,int ordinal,CancellationToken ct)
    {
        var asset=await db.Assets.AsNoTracking().FirstOrDefaultAsync(x=>
            x.TenantId==user.TenantId && x.PublicId==assetReference && x.IsActive && !x.IsDeleted,ct);
        if(asset==null || asset.CampusId is null or <=0 || !await CampusAllowedAsync(asset.CampusId.Value,ct))
            return (null,null,null,"Active, campus-authorized asset required.");
        if(asset.InventoryItemId is null or <=0 || asset.InventoryLocationId is null or <=0 ||
            !await db.InventoryItems.AsNoTracking().AnyAsync(x=>x.TenantId==user.TenantId &&
                x.Id==asset.InventoryItemId && x.IsStockTracked && x.IsAssetTracked &&
                x.IsActive && !x.IsDeleted,ct))
            return (null,null,null,"Asset needs an active, tracked procurement item and store.");
        // Reuse the audited supplier/PO/receipt/stock movement evidence predicate rather
        // than guessing that an item match authorizes capitalization.
        var readiness=await assets.GetCapitalizationReadinessAsync(assetReference,ct);
        if(!readiness.Success || readiness.Data==null || readiness.Data.HasBook ||
            !readiness.Data.Candidates.Any(x=>x.ReceiptReference==receiptReference && x.LineNo==lineNo))
            return (asset,null,null,"Approved, posted receipt/PO/stock evidence is not in this asset's reviewed candidate list.");
        var receipt=await db.ProcurementGoodsReceipts.AsNoTracking().FirstOrDefaultAsync(x=>
            x.TenantId==user.TenantId && x.PublicId==receiptReference && x.StateCode=="Posted" &&
            x.CampusId==asset.CampusId && x.LocationId==asset.InventoryLocationId && !x.IsDeleted,ct);
        if(receipt==null)return (asset,null,null,"Receipt campus/store provenance conflicts.");
        var line=await db.ProcurementGoodsReceiptLines.AsNoTracking().FirstOrDefaultAsync(x=>
            x.TenantId==user.TenantId && x.GoodsReceiptId==receipt.Id && x.LineNo==lineNo &&
            x.InventoryItemId==asset.InventoryItemId && !x.IsDeleted,ct);
        if(line==null || line.Quantity<1m || line.Quantity!=decimal.Truncate(line.Quantity) ||
            ordinal<=0 || ordinal>line.Quantity || line.PostedUnitCost is null or <=0m)
            return (asset,receipt,null,"Selected serialized unit must be within an integral posted receipt quantity.");
        return (asset,receipt,line,null);
    }

    public async Task<ApiResponse<AssetAccountingOptionsDto>> OptionsAsync(CancellationToken ct=default)
    {
        if(!Readable)return Conflict<AssetAccountingOptionsDto>("Asset Finance access denied.",403);
        var periods=await db.AccountingPeriods.AsNoTracking().Where(x=>x.TenantId==user.TenantId &&
            !x.IsDeleted && !x.IsClosed).OrderByDescending(x=>x.EndDate).Take(201)
            .Select(x=>new ProcurementInvoicePeriodOptionDto{Id=x.Id,Name=x.Name,StartDate=x.StartDate,EndDate=x.EndDate}).ToListAsync(ct);
        var accounts=await db.Accounts.AsNoTracking().Where(x=>x.TenantId==user.TenantId &&
            !x.IsDeleted && x.IsActive && x.AllowsPosting && !x.IsGroup && x.CurrencyCode=="BDT")
            .OrderBy(x=>x.Code).Take(501).Select(x=>new ProcurementInvoiceAccountOptionDto
            {Id=x.Id,Code=x.Code,Name=x.Name,Type=x.AccountType.ToString()}).ToListAsync(ct);
        if(periods.Count>200 || accounts.Count>500)
            return Conflict<AssetAccountingOptionsDto>("Too many periods/accounts: narrow institutional Finance setup.");
        return ApiResponse<AssetAccountingOptionsDto>.SuccessResponse(new AssetAccountingOptionsDto
        {Periods=periods,Accounts=accounts});
    }

    private async Task<AssetCapitalizationRecordDto> MapAsync(AssetCapitalizationCommand c,CancellationToken ct)
    {
        var assetRef=await db.Assets.AsNoTracking().Where(x=>x.TenantId==user.TenantId && x.Id==c.AssetId)
            .Select(x=>x.PublicId).FirstAsync(ct);
        var stockRef=c.StockMovementId.HasValue?await db.InventoryMovements.AsNoTracking()
            .Where(x=>x.TenantId==user.TenantId && x.Id==c.StockMovementId.Value)
            .Select(x=>(Guid?)x.PublicId).FirstOrDefaultAsync(ct):null;
        var journalRef=c.JournalId.HasValue?await db.Journals.AsNoTracking()
            .Where(x=>x.TenantId==user.TenantId && x.Id==c.JournalId.Value)
            .Select(x=>(Guid?)x.PublicId).FirstOrDefaultAsync(ct):null;
        var postedCost=c.StockMovementId.HasValue?await db.InventoryMovementLines.AsNoTracking()
            .Where(x=>x.TenantId==user.TenantId && x.InventoryMovementId==c.StockMovementId.Value)
            .Select(x=>(decimal?)x.UnitCost).FirstOrDefaultAsync(ct):null;
        var reversal=c.JournalId.HasValue?await db.Journals.AsNoTracking()
            .Where(x=>x.TenantId==user.TenantId && !x.IsDeleted &&
                x.SourceType=="AssetCapitalizationReversal" && x.SourceId==c.Id &&
                x.ReversalOfJournalId==c.JournalId && x.State!=JournalState.Rejected)
            .OrderByDescending(x=>x.Id).Select(x=>new{x.PublicId,x.State,x.RowVersion})
            .FirstOrDefaultAsync(ct):null;
        var stockReversal=c.StockMovementId.HasValue?await db.InventoryMovements.AsNoTracking()
            .Where(x=>x.TenantId==user.TenantId && !x.IsDeleted && x.StateCode=="Posted" &&
                x.ReversalOfMovementId==c.StockMovementId.Value)
            .Select(x=>(Guid?)x.PublicId).FirstOrDefaultAsync(ct):null;
        return new AssetCapitalizationRecordDto
        {
            Reference=c.PublicId,AssetReference=assetRef,CampusId=c.CampusId,
            State=reversal?.State==JournalState.Posted?"Reversed":c.StateCode,
            ProposedCostBasis=c.ProposedCostBasis,PostedStockCost=postedCost,
            PolicyMode=c.PolicyMode,ReceiptLineId=c.GoodsReceiptLineId,
            ReceiptUnitOrdinal=c.ReceiptUnitOrdinal,AssetBookId=c.AssetBookId,
            StockMovementReference=stockRef,FinanceJournalReference=journalRef,
            StockReversalReference=stockReversal,
            FinanceReversalReference=reversal?.State==JournalState.Posted?reversal.PublicId:null,
            PendingReversalReference=reversal?.State==JournalState.Submitted?reversal.PublicId:null,
            PendingReversalRowVersion=reversal?.State==JournalState.Submitted?
                Convert.ToBase64String(reversal.RowVersion):null,
            MakerId=c.CreatedBy,ReviewerId=c.ReviewedByUserId,
            RowVersion=Convert.ToBase64String(c.RowVersion)
        };
    }

    public async Task<ApiResponse<AssetCapitalizationRecordDto>> GetCapitalizationAsync(
        Guid assetReference,CancellationToken ct=default)
    {
        if(!Readable)return Conflict<AssetCapitalizationRecordDto>("Asset Finance access denied.",403);
        var a=await db.Assets.AsNoTracking().FirstOrDefaultAsync(x=>x.TenantId==user.TenantId &&
            x.PublicId==assetReference && !x.IsDeleted,ct);
        if(a==null || a.CampusId is null or <=0 || !await CampusAllowedAsync(a.CampusId.Value,ct))
            return Conflict<AssetCapitalizationRecordDto>("Asset not found.",404);
        var c=await db.AssetCapitalizationCommands.AsNoTracking().Where(x=>x.TenantId==user.TenantId &&
            x.AssetId==a.Id && !x.IsDeleted).OrderByDescending(x=>x.CreatedAt)
            .ThenByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
        return c==null?Conflict<AssetCapitalizationRecordDto>("No capitalization command.",404):
            ApiResponse<AssetCapitalizationRecordDto>.SuccessResponse(await MapAsync(c,ct));
    }

    public async Task<ApiResponse<AssetCapitalizationRecordDto>> RequestCapitalizationAsync(
        Guid assetReference,CreateAssetCapitalizationRequest request,CancellationToken ct=default)
    {
        if(!Maker)return Conflict<AssetCapitalizationRecordDto>("Asset Finance maker denied.",403);
        if(request is null)return Conflict<AssetCapitalizationRecordDto>("Capitalization request required.",400);
        if(assetReference==Guid.Empty || request.ClientRequestId==Guid.Empty ||
            request.ReceiptReference==Guid.Empty || request.ReceiptLineNo<=0 || request.ReceiptUnitOrdinal<=0 ||
            request.AccountingPeriodId<=0 || request.CapitalizationDate==default ||
            request.CapitalizationDate>Today || request.InServiceDate==default ||
            request.InServiceDate>Today || request.InServiceDate>request.CapitalizationDate ||
            request.UsefulLifeMonths is <1 or >1200 || request.PolicyVersion<1 ||
            request.DepreciationMethod!="StraightLine" ||
            request.PolicyMode is not ("Automatic" or "ManualFinal" or "AutoPlusManual") ||
            !Money(request.ProposedCostBasis) || !Money(request.SalvageValue) ||
            request.Reason is null || request.Reason.Trim().Length is <10 or >800)
            return Conflict<AssetCapitalizationRecordDto>("Valid source unit, policy, accounts, period, dates and reason required.",400);
        if(request.PolicyMode!="Automatic" && request.ProposedCostBasis<=0m)
            return Conflict<AssetCapitalizationRecordDto>("Manual valuation must specify a positive BDT cost basis.",400);
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var prior=await db.AssetCapitalizationCommands.AsNoTracking().FirstOrDefaultAsync(x=>
                x.TenantId==user.TenantId && x.ClientRequestId==request.ClientRequestId,ct);
            if(prior!=null)
            {
                var sameAsset = await db.Assets.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.Id == prior.AssetId &&
                    x.PublicId == assetReference, ct);
                var sameReceipt = await (from candidateLine in db.ProcurementGoodsReceiptLines.AsNoTracking()
                    join candidateReceipt in db.ProcurementGoodsReceipts.AsNoTracking()
                        on candidateLine.GoodsReceiptId equals candidateReceipt.Id
                    where candidateLine.TenantId == user.TenantId && candidateReceipt.TenantId == user.TenantId &&
                        candidateLine.Id == prior.GoodsReceiptLineId && candidateLine.LineNo == request.ReceiptLineNo &&
                        candidateReceipt.PublicId == request.ReceiptReference select candidateLine.Id).AnyAsync(ct);
                var same = !prior.IsDeleted && sameAsset && sameReceipt &&
                    prior.Reason == request.Reason.Trim() &&
                    prior.PolicyMode == request.PolicyMode &&
                    prior.ReceiptUnitOrdinal == request.ReceiptUnitOrdinal &&
                    (request.PolicyMode == "Automatic"
                        ? request.ProposedCostBasis == 0m ||
                          request.ProposedCostBasis == prior.ProposedCostBasis
                        : prior.ProposedCostBasis == request.ProposedCostBasis) &&
                    prior.AccountingPeriodId == request.AccountingPeriodId &&
                    prior.CapitalizationDate == request.CapitalizationDate &&
                    prior.InServiceDate == request.InServiceDate &&
                    prior.DepreciationMethod == request.DepreciationMethod &&
                    prior.UsefulLifeMonths == request.UsefulLifeMonths &&
                    prior.PolicyVersion == request.PolicyVersion &&
                    prior.SalvageValue == request.SalvageValue &&
                    prior.FixedAssetAccountId == request.FixedAssetAccountId &&
                    prior.InventoryAccountId == request.InventoryAccountId &&
                    prior.AccumulatedDepreciationAccountId == request.AccumulatedDepreciationAccountId &&
                    prior.DepreciationExpenseAccountId == request.DepreciationExpenseAccountId &&
                    prior.ImpairmentExpenseAccountId == request.ImpairmentExpenseAccountId &&
                    prior.DisposalGainAccountId == request.DisposalGainAccountId &&
                    prior.DisposalLossAccountId == request.DisposalLossAccountId &&
                    prior.VarianceGainAccountId == request.VarianceGainAccountId &&
                    prior.VarianceLossAccountId == request.VarianceLossAccountId;
                return same ? ApiResponse<AssetCapitalizationRecordDto>.SuccessResponse(
                        await MapAsync(prior,ct),"Existing asset request returned.")
                    : Conflict<AssetCapitalizationRecordDto>("Request key belongs to a different asset command.");
            }
            var (asset,receipt,line,error)=await SourceAsync(assetReference,request.ReceiptReference,
                request.ReceiptLineNo,request.ReceiptUnitOrdinal,ct);
            if(error!=null || asset==null || receipt==null || line==null)
                return Conflict<AssetCapitalizationRecordDto>(error??"Asset source not found.");
            if(!Version(asset.RowVersion,request.AssetRowVersion))
                return Conflict<AssetCapitalizationRecordDto>("Asset version changed; reload.");
            if(!await OpenPeriodAsync(request.AccountingPeriodId,request.CapitalizationDate,ct))
                return Conflict<AssetCapitalizationRecordDto>("Open capitalization period required.");
            var proposed=request.PolicyMode=="Automatic"?
                decimal.Round(line.PostedUnitCost!.Value,2,MidpointRounding.AwayFromZero):request.ProposedCostBasis;
            if(proposed<=0m || request.SalvageValue>proposed)
                return Conflict<AssetCapitalizationRecordDto>("Salvage cannot exceed a positive approved asset cost.");
            var c=new AssetCapitalizationCommand
            {
                TenantId=user.TenantId,PublicId=Guid.NewGuid(),ClientRequestId=request.ClientRequestId,
                AssetId=asset.Id,GoodsReceiptLineId=line.Id,ReceiptUnitOrdinal=request.ReceiptUnitOrdinal,
                CampusId=asset.CampusId!.Value,AccountingPeriodId=request.AccountingPeriodId,
                CapitalizationDate=request.CapitalizationDate,InServiceDate=request.InServiceDate,
                PolicyMode=request.PolicyMode,ProposedCostBasis=proposed,SalvageValue=request.SalvageValue,
                DepreciationMethod=request.DepreciationMethod,UsefulLifeMonths=request.UsefulLifeMonths,
                PolicyVersion=request.PolicyVersion,FixedAssetAccountId=request.FixedAssetAccountId,
                InventoryAccountId=request.InventoryAccountId,
                AccumulatedDepreciationAccountId=request.AccumulatedDepreciationAccountId,
                DepreciationExpenseAccountId=request.DepreciationExpenseAccountId,
                ImpairmentExpenseAccountId=request.ImpairmentExpenseAccountId,
                DisposalGainAccountId=request.DisposalGainAccountId,
                DisposalLossAccountId=request.DisposalLossAccountId,
                VarianceGainAccountId=request.VarianceGainAccountId,
                VarianceLossAccountId=request.VarianceLossAccountId,Reason=request.Reason.Trim(),
                StateCode="Submitted",CreatedBy=user.UserId,CreatedAt=Now
            };
            if(!await AccountsValidAsync(c,ct))
                return Conflict<AssetCapitalizationRecordDto>("Distinct, typed, active BDT Finance GL accounts required.");
            if(await db.AssetBooks.AsNoTracking().AnyAsync(x=>x.TenantId==user.TenantId && x.AssetId==asset.Id && !x.IsDeleted,ct) ||
                await db.AssetCapitalizationCommands.AsNoTracking().AnyAsync(x=>x.TenantId==user.TenantId &&
                    !x.IsDeleted && x.StateCode!="Rejected" &&
                    (x.AssetId==asset.Id || (x.GoodsReceiptLineId==line.Id &&
                        x.ReceiptUnitOrdinal==request.ReceiptUnitOrdinal)),ct))
                return Conflict<AssetCapitalizationRecordDto>("Asset or receipt unit is already assigned for capitalization.");
            db.AssetCapitalizationCommands.Add(c);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<AssetCapitalizationRecordDto>.SuccessResponse(await MapAsync(c,ct),
                "Source-linked capitalization submitted; no stock or Finance posting yet.");
        }
        catch(DbUpdateConcurrencyException){return Conflict<AssetCapitalizationRecordDto>("Asset capitalization version conflict.");}
        catch(DbUpdateException ex){logger.LogWarning(ex,"Asset capitalization maker SQL conflict");return Conflict<AssetCapitalizationRecordDto>("Asset capitalization source conflict.");}
        catch(InvalidOperationException ex) when(Contention(ex)){return Conflict<AssetCapitalizationRecordDto>("Asset capitalization SQL contention.");}
        catch(SqlException ex) when(Contention(ex)){return Conflict<AssetCapitalizationRecordDto>("Asset capitalization SQL contention.");}
    }

    public async Task<ApiResponse<AssetCapitalizationRecordDto>> ReviewCapitalizationAsync(
        Guid commandReference,ReviewAssetCapitalizationRequest request,CancellationToken ct=default)
    {
        if(!Reviewer)return Conflict<AssetCapitalizationRecordDto>("Independent Asset Finance approval denied.",403);
        if(request is null)return Conflict<AssetCapitalizationRecordDto>("Review request required.",400);
        if(commandReference==Guid.Empty || request.Reason is null || request.Reason.Trim().Length is <10 or >800)
            return Conflict<AssetCapitalizationRecordDto>("Decision version and reason (10–800) required.",400);
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var c=await db.AssetCapitalizationCommands.FirstOrDefaultAsync(x=>x.TenantId==user.TenantId &&
                x.PublicId==commandReference && !x.IsDeleted,ct);
            if(c==null || !await CampusAllowedAsync(c.CampusId,ct))
                return Conflict<AssetCapitalizationRecordDto>("Asset command not found.",404);
            if(c.StateCode!="Submitted" || !Version(c.RowVersion,request.RowVersion))
                return Conflict<AssetCapitalizationRecordDto>("Capitalization changed or already reviewed; reload.");
            if(c.CreatedBy is null or <=0 || c.CreatedBy==user.UserId)
                return Conflict<AssetCapitalizationRecordDto>("Separate capital acquisition maker and reviewer required.",403);
            var now=Now;
            if(!request.Approve)
            {
                c.StateCode="Rejected";c.ReviewedByUserId=user.UserId;c.ReviewedAtUtc=now;
                c.ReviewReason=request.Reason.Trim();c.UpdatedAt=now;c.UpdatedBy=user.UserId;
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
                return ApiResponse<AssetCapitalizationRecordDto>.SuccessResponse(await MapAsync(c,ct),
                    "Capitalization rejected; no stock or Finance posting.");
            }
            var asset=await db.Assets.AsNoTracking().FirstOrDefaultAsync(x=>x.TenantId==user.TenantId &&
                x.Id==c.AssetId && !x.IsDeleted,ct);
            var source=await db.ProcurementGoodsReceiptLines.AsNoTracking().FirstOrDefaultAsync(x=>
                x.TenantId==user.TenantId && x.Id==c.GoodsReceiptLineId && !x.IsDeleted,ct);
            if(asset==null || source==null || asset.CampusId!=c.CampusId)
                return Conflict<AssetCapitalizationRecordDto>("Asset or posted receipt source changed.");
            var receipt=await db.ProcurementGoodsReceipts.AsNoTracking().FirstOrDefaultAsync(x=>
                x.TenantId==user.TenantId && x.Id==source.GoodsReceiptId && !x.IsDeleted,ct);
            if(receipt==null)return Conflict<AssetCapitalizationRecordDto>("Source receipt missing.");
            var (validatedAsset,_,validatedLine,sourceError)=await SourceAsync(asset.PublicId,
                receipt.PublicId,source.LineNo,c.ReceiptUnitOrdinal,ct);
            if(sourceError!=null || validatedAsset==null || validatedLine==null ||
                validatedLine.Id!=c.GoodsReceiptLineId ||
                !await OpenPeriodAsync(c.AccountingPeriodId,c.CapitalizationDate,ct) ||
                !await AccountsValidAsync(c,ct) || c.CapitalizationDate>Today)
                return Conflict<AssetCapitalizationRecordDto>(sourceError??"Capitalization evidence, accounts or period invalid.");
            if(await db.AssetBooks.AsNoTracking().AnyAsync(x=>x.TenantId==user.TenantId &&
                x.AssetId==asset.Id && !x.IsDeleted,ct) ||
                await db.AssetCapitalizationCommands.AsNoTracking().AnyAsync(x=>
                    x.TenantId==user.TenantId && x.Id!=c.Id && !x.IsDeleted && x.StateCode=="Posted" &&
                    x.GoodsReceiptLineId==source.Id && x.ReceiptUnitOrdinal==c.ReceiptUnitOrdinal,ct))
                return Conflict<AssetCapitalizationRecordDto>("Asset/unit has already been capitalized.");
            var (movement,stockLine,stockError)=await inventory.PostAssetIssueInTransactionAsync(c,receipt,source,ct);
            if(stockError!=null || movement==null || stockLine==null)
                return Conflict<AssetCapitalizationRecordDto>(stockError??"Canonical stock issue failed; no posting committed.");
            var consumedCost=decimal.Round(stockLine.UnitCost,2,MidpointRounding.AwayFromZero);
            if(consumedCost<=0m || !Money(consumedCost) ||
                (c.PolicyMode=="Automatic" && c.ProposedCostBasis!=consumedCost))
                return Conflict<AssetCapitalizationRecordDto>("Stock cost changed since approval request; reload and revalue.");
            var variance=c.ProposedCostBasis-consumedCost;
            if((variance>0m && !c.VarianceGainAccountId.HasValue) ||
                (variance<0m && !c.VarianceLossAccountId.HasValue))
                return Conflict<AssetCapitalizationRecordDto>("Manual cost difference requires a configured gain/loss account.");
            var book=new AssetBook
            {
                TenantId=user.TenantId,AssetId=asset.Id,CampusId=c.CampusId,
                InServiceDate=c.InServiceDate,MethodCode=c.DepreciationMethod,
                UsefulLifeMonths=c.UsefulLifeMonths,CostBasis=c.ProposedCostBasis,
                SalvageValue=c.SalvageValue,PolicyVersion=c.PolicyVersion,
                CostAccountId=c.FixedAssetAccountId,
                AccumulatedDepreciationAccountId=c.AccumulatedDepreciationAccountId,
                DepreciationExpenseAccountId=c.DepreciationExpenseAccountId,
                ImpairmentExpenseAccountId=c.ImpairmentExpenseAccountId,
                DisposalGainAccountId=c.DisposalGainAccountId,
                DisposalLossAccountId=c.DisposalLossAccountId,IsActive=true,
                CreatedAt=now,CreatedBy=user.UserId
            };
            db.AssetBooks.Add(book);await db.SaveChangesAsync(ct);
            var jRef=Guid.NewGuid();
            var journal=new Journal
            {
                TenantId=user.TenantId,PublicId=jRef,IdempotencyKey=c.ClientRequestId,
                JournalNumber="ACAP-"+jRef.ToString("N").ToUpperInvariant(),
                JournalDate=c.CapitalizationDate,AccountingPeriodId=c.AccountingPeriodId,
                CampusId=c.CampusId,CurrencyCode="BDT",SourceType="AssetCapitalization",
                SourceId=c.Id,ReferenceNumber=asset.AssetTag,
                Description="Posted receipt unit "+c.GoodsReceiptLineId+"/"+c.ReceiptUnitOrdinal+
                    "; valuation="+c.PolicyMode+"; "+c.Reason[..Math.Min(300,c.Reason.Length)],
                State=JournalState.Posted,CreatedAt=now,CreatedBy=c.CreatedBy,
                ApprovedByUserId=user.UserId,ApprovedAt=now,PostedByUserId=user.UserId,PostedAt=now
            };
            db.Journals.Add(journal);await db.SaveChangesAsync(ct);
            var entries=new List<JournalLine>
            {
                new(){TenantId=user.TenantId,JournalId=journal.Id,LineNo=1,
                    AccountId=c.FixedAssetAccountId,CampusId=c.CampusId,
                    DebitAmount=c.ProposedCostBasis,CreatedAt=now,CreatedBy=user.UserId,
                    Description="Serialized fixed asset cost"},
                new(){TenantId=user.TenantId,JournalId=journal.Id,LineNo=2,
                    AccountId=c.InventoryAccountId,CampusId=c.CampusId,
                    CreditAmount=consumedCost,CreatedAt=now,CreatedBy=user.UserId,
                    Description="Canonical stock issue cost"}
            };
            if(variance>0m)entries.Add(new JournalLine{TenantId=user.TenantId,JournalId=journal.Id,
                LineNo=3,AccountId=c.VarianceGainAccountId!.Value,CampusId=c.CampusId,
                CreditAmount=variance,CreatedAt=now,CreatedBy=user.UserId,
                Description="Approved manual capitalization valuation gain"});
            if(variance<0m)entries.Add(new JournalLine{TenantId=user.TenantId,JournalId=journal.Id,
                LineNo=3,AccountId=c.VarianceLossAccountId!.Value,CampusId=c.CampusId,
                DebitAmount=-variance,CreatedAt=now,CreatedBy=user.UserId,
                Description="Approved manual capitalization valuation loss"});
            if(entries.Sum(x=>x.DebitAmount)!=entries.Sum(x=>x.CreditAmount))
                return Conflict<AssetCapitalizationRecordDto>("Capitalization GL debits/credits do not reconcile.");
            db.JournalLines.AddRange(entries);
            c.StateCode="Posted";c.AssetBookId=book.Id;c.StockMovementId=movement.Id;
            c.JournalId=journal.Id;c.ReviewedByUserId=user.UserId;c.ReviewedAtUtc=now;
            c.ReviewReason=request.Reason.Trim();c.UpdatedAt=now;c.UpdatedBy=user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<AssetCapitalizationRecordDto>.SuccessResponse(await MapAsync(c,ct),
                "One serialized receipt unit issued from canonical stock and capitalized in the same posted Finance GL transaction.");
        }
        catch(DbUpdateConcurrencyException){return Conflict<AssetCapitalizationRecordDto>("Concurrent asset capitalization conflict.");}
        catch(DbUpdateException ex){logger.LogWarning(ex,"Asset capitalization SQL conflict");return Conflict<AssetCapitalizationRecordDto>("Asset capitalization transaction rolled back.");}
        catch(InvalidOperationException ex) when(Contention(ex)){return Conflict<AssetCapitalizationRecordDto>("Asset capitalization SQL contention.");}
        catch(SqlException ex) when(Contention(ex)){return Conflict<AssetCapitalizationRecordDto>("Asset capitalization SQL contention.");}
    }
}
