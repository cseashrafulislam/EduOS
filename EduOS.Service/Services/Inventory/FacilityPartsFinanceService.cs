using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

/// <summary>Facility consumables use the existing stock ledger and Finance GL.
/// A submitted journal is the approval command; no editable parallel ledger exists.</summary>
public sealed partial class FacilityPartsFinanceService(
    EduOSDbContext db, ICurrentUserService user, TimeProvider clock,
    InventoryMovementService inventory, ILogger<FacilityPartsFinanceService> logger)
{
    private bool Reader => user.IsAuthenticated && user.TenantId > 0 && db.CurrentTenantId == user.TenantId &&
        (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("Accountant"));
    private bool Maker => Reader && (user.IsTenantAdmin || user.IsInRole("Accountant"));
    private bool Reviewer => Reader && (user.IsTenantAdmin || user.IsInRole("Principal"));
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(Now);
    private static ApiResponse<T> Error<T>(string message, int status = 409) =>
        ApiResponse<T>.ErrorResponse(message, status);
    private static bool Version(byte[] actual, string? supplied)
    { try { return !string.IsNullOrWhiteSpace(supplied) && actual.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
      catch (FormatException) { return false; } }
    private static bool Active(Journal j) => !j.IsDeleted && j.State is not (JournalState.Rejected or JournalState.Cancelled);
    private static bool Conflict(Exception e)
    {
        for (Exception? x = e; x != null; x = x.InnerException)
            if (x is SqlException sql && sql.Number is 1205 or 1222 or 2601 or 2627 or 3960) return true;
        return e is DbUpdateException or DbUpdateConcurrencyException;
    }
    private bool AllCampuses => user.IsTenantAdmin || user.IsInRole("Principal");
    private async Task<bool> CampusAllowed(long campusId, CancellationToken ct) =>
        await db.Campuses.AsNoTracking().AnyAsync(c => c.TenantId == user.TenantId && c.Id == campusId &&
            !c.IsDeleted && c.IsActive && (AllCampuses || db.UserCampusAccesses.Any(a =>
                a.TenantId == user.TenantId && a.UserId == user.UserId && a.CampusId == campusId &&
                a.IsActive && !a.IsDeleted)), ct);
    private Task<bool> OpenPeriod(long id, DateOnly day, CancellationToken ct) =>
        db.AccountingPeriods.AsNoTracking().AnyAsync(p => p.TenantId == user.TenantId &&
            p.Id == id && !p.IsDeleted && !p.IsClosed && p.StartDate <= day && p.EndDate >= day, ct);

    private async Task<bool> AccountsValid(long expenseId, long stockId, CancellationToken ct)
    {
        if (expenseId <= 0 || stockId <= 0 || expenseId == stockId) return false;
        var rows = await db.Accounts.AsNoTracking().Where(a => a.TenantId == user.TenantId &&
            (a.Id == expenseId || a.Id == stockId) && !a.IsDeleted && a.IsActive &&
            !a.IsGroup && a.AllowsPosting && a.CurrencyCode == "BDT")
            .Select(a => new { a.Id, a.AccountType }).ToListAsync(ct);
        return rows.Count == 2 && rows.Any(a => a.Id == expenseId && a.AccountType == AccountType.Expense) &&
            rows.Any(a => a.Id == stockId && a.AccountType == AccountType.Asset);
    }

    private async Task<(InventoryMovement? Movement, long RequestId, long CampusId)> Source(
        Guid reference, CancellationToken ct)
    {
        var source = await (from m in db.InventoryMovements.AsNoTracking()
            join w in db.FacilityWorkOrders.AsNoTracking() on m.SourceId equals (long?)w.Id
            join r in db.FacilityServiceRequests.AsNoTracking() on w.FacilityServiceRequestId equals r.Id
            where m.TenantId == user.TenantId && w.TenantId == user.TenantId && r.TenantId == user.TenantId &&
                m.PublicId == reference && m.SourceType == "FacilityWorkOrder" &&
                m.MovementTypeCode == "Issue" && !m.IsDeleted && !w.IsDeleted && !r.IsDeleted
            select new { Movement = m, RequestId = r.Id, r.CampusId }).FirstOrDefaultAsync(ct);
        if (source == null || source.Movement.FromLocationId is null or <= 0 ||
            !await CampusAllowed(source.CampusId, ct) ||
            !await db.InventoryLocations.AsNoTracking().AnyAsync(l => l.TenantId == user.TenantId &&
                l.Id == source.Movement.FromLocationId && !l.IsDeleted &&
                (l.CampusId == null || l.CampusId == source.CampusId), ct))
            return (null, 0, 0);
        return (source.Movement, source.RequestId, source.CampusId);
    }

    private async Task<decimal?> StockCost(long movementId, CancellationToken ct)
    {
        var lines = await db.InventoryMovementLines.AsNoTracking().Where(l =>
            l.TenantId == user.TenantId && l.InventoryMovementId == movementId && !l.IsDeleted)
            .Select(l => new { l.Quantity, l.UnitCost }).ToListAsync(ct);
        if (lines.Count == 0 || lines.Any(l => l.Quantity <= 0m || l.UnitCost < 0m)) return null;
        try
        {
            var value = decimal.Round(lines.Sum(l => l.Quantity * l.UnitCost), 2, MidpointRounding.AwayFromZero);
            return value is > 0m and <= 999999999999.99m ? value : null;
        }
        catch (OverflowException) { return null; }
    }

    private async Task<bool> StockReversed(long movementId, CancellationToken ct) =>
        await db.InventoryMovements.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            x.ReversalOfMovementId == movementId && x.StateCode == "Posted" && !x.IsDeleted, ct);

    private async Task<FacilityPartsFinanceRecordDto> Map(Journal journal, InventoryMovement movement,
        long requestId, long campusId, CancellationToken ct)
    {
        var lines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.JournalId == journal.Id && !x.IsDeleted).ToListAsync(ct);
        var reversal = await db.Journals.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.ReversalOfJournalId == journal.Id && x.SourceType == "FacilityPartsExpenseReversal" &&
            !x.IsDeleted && x.State != JournalState.Rejected && x.State != JournalState.Cancelled)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        var stockReversal = await db.InventoryMovements.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.ReversalOfMovementId == movement.Id && x.StateCode == "Posted" && !x.IsDeleted)
            .Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync(ct);
        return new FacilityPartsFinanceRecordDto {
            JournalReference = journal.PublicId, MovementReference = movement.PublicId,
            CampusId = campusId, FacilityRequestId = requestId,
            State = (stockReversal.HasValue != (reversal?.State == JournalState.Posted)) &&
                journal.State == JournalState.Posted ? "OutOfSync" :
                reversal?.State == JournalState.Posted ? "Reversed" : journal.State.ToString(),
            ReversalState = reversal?.State.ToString() ?? "None",
            FinanceReversalReference = reversal?.PublicId,
            StockReversalReference = stockReversal,
            RepairsExpenseAccountId = lines.FirstOrDefault(x => x.DebitAmount > 0m)?.AccountId ?? 0,
            InventoryAssetAccountId = lines.FirstOrDefault(x => x.CreditAmount > 0m)?.AccountId ?? 0,
            AmountBdt = lines.Sum(x => x.DebitAmount), JournalDate = journal.JournalDate,
            RowVersion = Convert.ToBase64String(journal.RowVersion),
            ReversalRowVersion = reversal == null ? null : Convert.ToBase64String(reversal.RowVersion)
        };
    }

    public async Task<ApiResponse<FacilityPartsFinanceRecordDto>> GetAsync(Guid movementReference,
        CancellationToken ct = default)
    {
        if (!Reader) return Error<FacilityPartsFinanceRecordDto>("Finance access denied.", 403);
        var (movement, requestId, campusId) = await Source(movementReference, ct);
        if (movement == null) return Error<FacilityPartsFinanceRecordDto>("Facility movement not found.", 404);
        var journal = await db.Journals.AsNoTracking().Where(j => j.TenantId == user.TenantId &&
            j.SourceType == "InventoryMovement" && j.SourceId == movement.Id && !j.IsDeleted)
            .OrderByDescending(j => j.State != JournalState.Rejected && j.State != JournalState.Cancelled)
            .ThenByDescending(j => j.Id).FirstOrDefaultAsync(ct);
        return journal == null ? Error<FacilityPartsFinanceRecordDto>("No Finance recognition request yet.", 404) :
            ApiResponse<FacilityPartsFinanceRecordDto>.SuccessResponse(await Map(journal, movement, requestId, campusId, ct));
    }

    public async Task<ApiResponse<FacilityPartsFinanceOptionsDto>> OptionsAsync(string? expenseSearch, string? stockSearch,
        CancellationToken ct = default)
    {
        if (!Reader) return Error<FacilityPartsFinanceOptionsDto>("Finance access denied.", 403);
        expenseSearch = expenseSearch?.Trim(); stockSearch = stockSearch?.Trim();
        if ((expenseSearch?.Length ?? 0) > 80 || (stockSearch?.Length ?? 0) > 80)
            return Error<FacilityPartsFinanceOptionsDto>("Search too long.", 400);
        var periods = await db.AccountingPeriods.AsNoTracking().Where(p => p.TenantId == user.TenantId &&
            !p.IsDeleted && !p.IsClosed).OrderByDescending(p => p.EndDate).Take(101)
            .Select(p => new FacilityPartsFinanceOptionDto { Id = p.Id, Label = p.Name, Type = "Period" }).ToListAsync(ct);
        var eligible = db.Accounts.AsNoTracking().Where(a => a.TenantId == user.TenantId &&
            !a.IsDeleted && a.IsActive && !a.IsGroup && a.AllowsPosting && a.CurrencyCode == "BDT" &&
            (a.AccountType == AccountType.Expense || a.AccountType == AccountType.Asset));
        var expenses = await eligible.Where(a => a.AccountType == AccountType.Expense &&
            (string.IsNullOrEmpty(expenseSearch) || a.Code.StartsWith(expenseSearch) || a.Name.StartsWith(expenseSearch)))
            .OrderBy(a => a.Code).Take(101)
            .Select(a => new FacilityPartsFinanceOptionDto { Id = a.Id, Label = a.Code + " — " + a.Name,
                Type = a.AccountType.ToString() }).ToListAsync(ct);
        var stockAssets = await eligible.Where(a => a.AccountType == AccountType.Asset &&
            (string.IsNullOrEmpty(stockSearch) || a.Code.StartsWith(stockSearch) || a.Name.StartsWith(stockSearch)))
            .OrderBy(a => a.Code).Take(101)
            .Select(a => new FacilityPartsFinanceOptionDto { Id = a.Id, Label = a.Code + " — " + a.Name,
                Type = a.AccountType.ToString() }).ToListAsync(ct);
        if (periods.Count > 100)
            return Error<FacilityPartsFinanceOptionsDto>("Too many open accounting periods; close obsolete periods.", 409);
        return ApiResponse<FacilityPartsFinanceOptionsDto>.SuccessResponse(new() {
            Periods = periods, Accounts = expenses.Take(100).Concat(stockAssets.Take(100)).ToArray(),
            MoreAccountsAvailable = expenses.Count > 100 || stockAssets.Count > 100
        });
    }
}
