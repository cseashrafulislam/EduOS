using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Enums.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

public sealed partial class ProcurementSupplierInvoiceService
{
    private const string ReturnCreditSource = "ProcurementSupplierReturnCredit";

    private async Task<decimal> CreditedAsync(long invoiceId, bool includePending,
        long excludedId, CancellationToken ct) =>
        await db.ProcurementSupplierReturnCredits.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.SupplierInvoiceId == invoiceId &&
            x.Id != excludedId && !x.IsDeleted &&
            (x.StateCode == "Posted" || (includePending && x.StateCode == "Submitted")))
            .SumAsync(x => (decimal?)x.CreditAmount, ct) ?? 0m;

    private async Task<decimal> PostedSupplierPaymentsAsync(long invoiceId, long payableId,
        CancellationToken ct)
    {
        return await (from line in db.JournalLines.AsNoTracking()
            join payment in db.Journals.AsNoTracking() on line.JournalId equals payment.Id
            where line.TenantId == user.TenantId && !line.IsDeleted && line.AccountId == payableId &&
                line.DebitAmount > 0m && line.CreditAmount == 0m &&
                payment.TenantId == user.TenantId && !payment.IsDeleted &&
                payment.SourceType == SettlementSource && payment.SourceId == invoiceId &&
                payment.State == JournalState.Posted &&
                !db.Journals.Any(r => r.TenantId == user.TenantId && !r.IsDeleted &&
                    r.SourceType == SettlementReversalSource && r.SourceId == payment.Id &&
                    r.ReversalOfJournalId == payment.Id && r.State == JournalState.Posted)
            select (decimal?)line.DebitAmount).SumAsync(ct) ?? 0m;
    }

    private async Task<bool> PendingSupplierSettlementAsync(long invoiceId, CancellationToken ct) =>
        await db.Journals.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && x.SourceType == SettlementSource && x.SourceId == invoiceId &&
            (x.State == JournalState.Submitted || x.State == JournalState.Approved), ct);

    private async Task<(long Stock, long Payable, string? Error)> ReturnGlSourceAsync(
        ProcurementSupplierInvoice invoice, CancellationToken ct)
    {
        var (source, journal, payable, error) = await SettlementSourceAsync(invoice.PublicId, ct);
        if (error != null || source == null || journal == null)
            return (0, 0, error ?? "Invoice has no valid posted payable.");
        var stock = await (from l in db.JournalLines.AsNoTracking()
            join a in db.Accounts.AsNoTracking() on l.AccountId equals a.Id
            where l.TenantId == user.TenantId && !l.IsDeleted && l.JournalId == journal.Id &&
                l.CampusId == invoice.CampusId && l.DebitAmount == invoice.StockValue &&
                l.CreditAmount == 0m && a.TenantId == user.TenantId && !a.IsDeleted &&
                a.AccountType == AccountType.Asset && a.IsActive && a.AllowsPosting && !a.IsGroup
            select l.AccountId).Take(2).ToListAsync(ct);
        return stock.Count == 1 ? (stock[0], payable, null) :
            (0, 0, "Exactly one original Inventory GL debit is required.");
    }

    private async Task<SupplierReturnCreditDto> ReturnDtoAsync(
        ProcurementSupplierReturnCredit c, CancellationToken ct)
    {
        var invoice = await db.ProcurementSupplierInvoices.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.Id == c.SupplierInvoiceId)
            .Select(x => x.PublicId).FirstAsync(ct);
        var stock = c.StockMovementId.HasValue ? await db.InventoryMovements.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.Id == c.StockMovementId.Value)
            .Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync(ct) : null;
        var gl = c.JournalId.HasValue ? await db.Journals.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.Id == c.JournalId.Value)
            .Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync(ct) : null;
        return new SupplierReturnCreditDto
        {
            Reference = c.PublicId, SupplierInvoiceReference = invoice,
            SupplierInvoiceLineId = c.SupplierInvoiceLineId, Quantity = c.Quantity,
            Mode = c.Mode, State = c.StateCode, ReturnDate = c.ReturnDate,
            SupplierReturnReference = c.SupplierReturnReference, StockValue = c.StockValue,
            AutomaticCredit = c.AutomaticCreditAmount, CreditAmount = c.CreditAmount,
            UnpaidCredit = c.UnpaidCreditAmount, RefundReceivable = c.RefundReceivableAmount,
            StockMovementReference = stock, FinanceJournalReference = gl,
            MakerId = c.CreatedBy, ReviewerId = c.ApprovedByUserId,
            RowVersion = Convert.ToBase64String(c.RowVersion)
        };
    }

    public async Task<ApiResponse<PagedResult<SupplierReturnCreditDto>>> ReturnCreditsAsync(
        Guid invoiceReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead) return Err<PagedResult<SupplierReturnCreditDto>>("Supplier credit-note access denied.", 403);
        if (page < 1 || pageSize is < 1 or > 50 || (long)(page - 1) * pageSize > int.MaxValue)
            return Err<PagedResult<SupplierReturnCreditDto>>("Invalid bounded page.");
        var invoice = await Visible().FirstOrDefaultAsync(x => x.PublicId == invoiceReference, ct);
        if (invoice == null) return Err<PagedResult<SupplierReturnCreditDto>>("Invoice not found.", 404);
        var query = db.ProcurementSupplierReturnCredits.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.SupplierInvoiceId == invoice.Id && !x.IsDeleted);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        var result = new List<SupplierReturnCreditDto>(rows.Count);
        foreach (var row in rows) result.Add(await ReturnDtoAsync(row, ct));
        return ApiResponse<PagedResult<SupplierReturnCreditDto>>.SuccessResponse(new PagedResult<SupplierReturnCreditDto>
        { Items = result, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<SupplierReturnCreditDto>> RequestReturnCreditAsync(
        Guid reference, CreateSupplierReturnCreditRequest request, CancellationToken ct = default)
    {
        if (!CanMake) return Err<SupplierReturnCreditDto>("Supplier return maker denied.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.SupplierInvoiceLineId <= 0 || !Quantity(request.Quantity) ||
            request.ReturnDate == default || request.AccountingPeriodId <= 0 ||
            request.Mode is not ("Automatic" or "ManualFinal" or "AutoPlusManual") ||
            !Money(request.ManualFinalCredit) ||
            request.ManualCreditCorrection is < -999999999999.99m or > 999999999999.99m ||
            decimal.Round(request.ManualCreditCorrection, 2) != request.ManualCreditCorrection ||
            string.IsNullOrWhiteSpace(request.SupplierReturnReference) ||
            request.SupplierReturnReference.Trim().Length is < 6 or > 100 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 800)
            return Err<SupplierReturnCreditDto>("Valid line, physical quantity, credit mode, return proof and reason required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var invoice = await Visible().FirstOrDefaultAsync(x => x.PublicId == reference, ct);
            if (invoice == null) return Err<SupplierReturnCreditDto>("Supplier invoice not found.", 404);
            var replay = await db.ProcurementSupplierReturnCredits.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, ct);
            if (replay != null)
            {
                var same = !replay.IsDeleted && replay.SupplierInvoiceId == invoice.Id &&
                    replay.SupplierInvoiceLineId == request.SupplierInvoiceLineId &&
                    replay.Quantity == request.Quantity && replay.Mode == request.Mode &&
                    replay.ReturnDate == request.ReturnDate &&
                    replay.AccountingPeriodId == request.AccountingPeriodId &&
                    replay.SupplierReturnReference == request.SupplierReturnReference.Trim() &&
                    replay.Reason == request.Reason.Trim() &&
                    replay.VarianceAccountId == request.VarianceAccountId &&
                    replay.RefundReceivableAccountId == request.RefundReceivableAccountId;
                var expected = replay.AutomaticCreditAmount;
                same &= request.Mode == "Automatic" ? request.ManualFinalCredit == 0m &&
                    request.ManualCreditCorrection == 0m :
                    request.Mode == "ManualFinal" ? request.ManualFinalCredit == replay.CreditAmount &&
                    request.ManualCreditCorrection == 0m :
                    request.ManualFinalCredit == 0m && expected + request.ManualCreditCorrection == replay.CreditAmount;
                return same ? ApiResponse<SupplierReturnCreditDto>.SuccessResponse(
                    await ReturnDtoAsync(replay, ct), "Existing credit-note request returned.") :
                    Err<SupplierReturnCreditDto>("Return key belongs to another command.", 409);
            }
            if (invoice.StateCode != "Posted" || invoice.ReversalJournalId.HasValue ||
                !Version(invoice.RowVersion, request.InvoiceRowVersion) ||
                request.ReturnDate < invoice.InvoiceDate ||
                request.ReturnDate > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) ||
                !await db.AccountingPeriods.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.Id == request.AccountingPeriodId &&
                    !x.IsDeleted && !x.IsClosed && x.StartDate <= request.ReturnDate &&
                    x.EndDate >= request.ReturnDate, ct))
                return Err<SupplierReturnCreditDto>("Posted payable, unchanged invoice and open return period required.", 409);
            var line = await db.ProcurementSupplierInvoiceLines.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == request.SupplierInvoiceLineId &&
                x.SupplierInvoiceId == invoice.Id && !x.IsDeleted, ct);
            if (line == null) return Err<SupplierReturnCreditDto>("Posted supplier invoice line not found.", 404);
            var stock = decimal.Round(line.StockUnitCostSnapshot * request.Quantity, 2,
                MidpointRounding.AwayFromZero);
            var automatic = decimal.Round(line.InvoiceUnitPrice * request.Quantity, 2,
                MidpointRounding.AwayFromZero);
            var credit = request.Mode switch
            {
                "Automatic" => automatic,
                "ManualFinal" => request.ManualFinalCredit,
                _ => automatic + request.ManualCreditCorrection
            };
            if (credit <= 0m || stock <= 0m || !Money(credit) || !Money(stock) ||
                (request.Mode == "Automatic" && (request.ManualFinalCredit != 0m ||
                    request.ManualCreditCorrection != 0m)) ||
                (request.Mode == "ManualFinal" && request.ManualCreditCorrection != 0m) ||
                (request.Mode == "AutoPlusManual" && request.ManualFinalCredit != 0m))
                return Err<SupplierReturnCreditDto>("Automatic/manual credit values must be exclusive and in BDT cents.", 409);
            var alreadyQuantity = await db.ProcurementSupplierReturnCredits.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && x.SupplierInvoiceLineId == line.Id && !x.IsDeleted &&
                (x.StateCode == "Submitted" || x.StateCode == "Posted"))
                .SumAsync(x => (decimal?)x.Quantity, ct) ?? 0m;
            if (request.Quantity > line.Quantity - alreadyQuantity ||
                credit > invoice.TotalAmount - await CreditedAsync(invoice.Id, true, 0, ct))
                return Err<SupplierReturnCreditDto>("Return exceeds still-unreturned quantity or uncredited invoice amount.", 409);
            var (stockId, payableId, sourceError) = await ReturnGlSourceAsync(invoice, ct);
            if (sourceError != null) return Err<SupplierReturnCreditDto>(sourceError, 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var record = new ProcurementSupplierReturnCredit
            {
                TenantId = user.TenantId, ClientRequestId = request.ClientRequestId,
                SupplierInvoiceId = invoice.Id, SupplierInvoiceLineId = line.Id,
                CampusId = invoice.CampusId, AccountingPeriodId = request.AccountingPeriodId,
                ReturnDate = request.ReturnDate, SupplierReturnReference = request.SupplierReturnReference.Trim(),
                Mode = request.Mode, Quantity = request.Quantity, StockValue = stock,
                CreditAmount = credit, AutomaticCreditAmount = automatic,
                StockAccountId = stockId, PayableAccountId = payableId,
                VarianceAccountId = request.VarianceAccountId,
                RefundReceivableAccountId = request.RefundReceivableAccountId,
                Reason = request.Reason.Trim(), StateCode = "Submitted",
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.ProcurementSupplierReturnCredits.Add(record);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<SupplierReturnCreditDto>.SuccessResponse(await ReturnDtoAsync(record, ct),
                "Source-linked return submitted; no stock, payable or bank mutation yet.");
        }
        catch (DbUpdateConcurrencyException) { return Err<SupplierReturnCreditDto>("Concurrent return conflict.", 409); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Supplier return SQL write conflict"); return Err<SupplierReturnCreditDto>("Return SQL conflict.", 409); }
        catch (SqlException ex) when (ConflictSql(ex)) { return Err<SupplierReturnCreditDto>("Return SQL contention.", 409); }
    }
}
