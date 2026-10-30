using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace EduOS.Service.Services.Inventory;
public sealed partial class InventoryMovementService : IInventoryMovementService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    private readonly ILogger<InventoryMovementService> logger;
    public InventoryMovementService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock, ILogger<InventoryMovementService> logger)
    { this.db = db; this.user = user; this.clock = clock; this.logger = logger; }
    private bool Readable => user.IsAuthenticated && user.TenantId > 0 && (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("InventoryManager") || user.IsInRole("StoreKeeper") || user.IsInRole("Accountant"));
    private bool Writable => user.IsAuthenticated && user.TenantId > 0 && (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("InventoryManager") || user.IsInRole("StoreKeeper"));
    private bool Postable => user.IsAuthenticated && user.TenantId > 0 && (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("InventoryManager"));
    private bool Reversible => user.IsAuthenticated && user.TenantId > 0 && (user.IsTenantAdmin || user.IsInRole("Principal"));
    private bool AllCampuses => user.IsTenantAdmin || user.IsInRole("Principal");
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Inventory movement access denied.", 403);
    private static bool Version(byte[] actual, string? supplied)
    { try { return !string.IsNullOrWhiteSpace(supplied) && actual.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); } catch (FormatException) { return false; } }
    private static bool Precision(decimal value, int places) => value == decimal.Round(value, places);
    private IQueryable<InventoryLocation> Locations() => db.InventoryLocations.AsNoTracking().Where(x => x.TenantId == user.TenantId && x.IsActive && !x.IsDeleted &&
        (x.CampusId == null || db.Campuses.Any(c => c.TenantId == user.TenantId && c.Id == x.CampusId && c.IsActive && !c.IsDeleted)) &&
        (AllCampuses || (x.CampusId != null && db.UserCampusAccesses.Any(a => a.TenantId == user.TenantId && a.UserId == user.UserId &&
            a.CampusId == x.CampusId && a.IsActive && !a.IsDeleted))));
    private async Task<bool> LocationsAllowed(long? from, long? to, CancellationToken ct)
    { var ids = new[] { from, to }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
      return ids.Length > 0 && await Locations().CountAsync(x => ids.Contains(x.Id), ct) == ids.Length; }
    private IQueryable<InventoryMovement> Visible() { var ids = Locations().Select(x => x.Id);
        return db.InventoryMovements.AsNoTracking().Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
            (!x.FromLocationId.HasValue || ids.Contains(x.FromLocationId.Value)) &&
            (!x.ToLocationId.HasValue || ids.Contains(x.ToLocationId.Value))); }
    private sealed class Effect { public long ItemId { get; set; } public long LocationId { get; set; } public string Lot { get; set; } = ""; public decimal Quantity { get; set; } public decimal Value { get; set; } }
    private sealed class Balance { public long ItemId { get; set; } public long LocationId { get; set; } public string Lot { get; set; } = ""; public decimal Quantity { get; set; } public decimal Value { get; set; } }
    private IQueryable<Effect> Ledger(DateTime? asOf)
    {
        var rows = from l in db.InventoryMovementLines.AsNoTracking() join m in db.InventoryMovements.AsNoTracking() on l.InventoryMovementId equals m.Id
            where l.TenantId == user.TenantId && m.TenantId == user.TenantId && !l.IsDeleted && !m.IsDeleted &&
                m.StateCode == "Posted" && m.PostedAt != null select new { l, m };
        if (asOf.HasValue) rows = rows.Where(x => x.m.PostedAt <= asOf.Value);
        var plus = rows.Where(x => ((x.m.MovementTypeCode == "Receipt" || x.m.MovementTypeCode == "Transfer") && x.m.ToLocationId != null) ||
            ((x.m.MovementTypeCode == "ReversalIssue" || x.m.MovementTypeCode == "ReversalTransfer") && x.m.FromLocationId != null))
            .Select(x => new Effect { ItemId = x.l.InventoryItemId, Lot = x.l.LotOrBatchNo ?? "",
                LocationId = x.m.MovementTypeCode == "Receipt" || x.m.MovementTypeCode == "Transfer" ? x.m.ToLocationId!.Value : x.m.FromLocationId!.Value,
                Quantity = x.l.Quantity, Value = x.l.Quantity * x.l.UnitCost });
        var minus = rows.Where(x => ((x.m.MovementTypeCode == "Issue" || x.m.MovementTypeCode == "Transfer") && x.m.FromLocationId != null) ||
            ((x.m.MovementTypeCode == "ReversalReceipt" || x.m.MovementTypeCode == "ReversalTransfer") && x.m.ToLocationId != null))
            .Select(x => new Effect { ItemId = x.l.InventoryItemId, Lot = x.l.LotOrBatchNo ?? "",
                LocationId = x.m.MovementTypeCode == "Issue" || x.m.MovementTypeCode == "Transfer" ? x.m.FromLocationId!.Value : x.m.ToLocationId!.Value,
                Quantity = -x.l.Quantity, Value = -(x.l.Quantity * x.l.UnitCost) });
        return plus.Concat(minus);
    }
    private async Task<Dictionary<(long, long, string), Balance>> Balances(long[] items, long[] locations, DateTime? asOf, CancellationToken ct)
    {
        var values = await Ledger(asOf).Where(x => items.Contains(x.ItemId) && locations.Contains(x.LocationId))
            .GroupBy(x => new { x.ItemId, x.LocationId, x.Lot })
            .Select(g => new Balance { ItemId = g.Key.ItemId, LocationId = g.Key.LocationId, Lot = g.Key.Lot,
                Quantity = g.Sum(x => x.Quantity), Value = g.Sum(x => x.Value) }).ToListAsync(ct);
        return values.ToDictionary(x => (x.ItemId, x.LocationId, x.Lot));
    }
    private static Balance Get(Dictionary<(long, long, string), Balance> values, long item, long location, string lot)
    {
        if (!values.TryGetValue((item, location, lot), out var value))
        { value = new Balance { ItemId = item, LocationId = location, Lot = lot }; values[(item, location, lot)] = value; }
        return value;
    }
    private static void Add(Balance balance, decimal quantity, decimal cost) { balance.Quantity += quantity; balance.Value += quantity * cost; }
    private static bool Nonnegative(IEnumerable<Balance> balances) => balances.All(x => x.Quantity >= 0m && x.Value >= 0m &&
        (x.Quantity != 0m || Math.Abs(x.Value) <= 0.01m));
    private static InventoryMovementDto Map(InventoryMovement m, IEnumerable<InventoryMovementLine> lines, bool reversed = false, string? transferState = null) => new()
    {
        Reference = m.PublicId, MovementNumber = m.MovementNumber, MovementTypeCode = m.MovementTypeCode,
        SourceType = m.SourceType, SourceId = m.SourceId, Remarks = m.Remarks, TransferState = transferState,
        MovementAt = m.MovementAt, FromLocationId = m.FromLocationId, ToLocationId = m.ToLocationId,
        StateCode = reversed ? "Reversed" : m.StateCode, PostedAt = m.PostedAt,
        ReservationExpiresAtUtc = m.ReservationExpiresAtUtc,
        ReservationExpiredAtUtc = m.ReservationExpiredAtUtc,
        ReservationExpiredByUserId = m.ReservationExpiredByUserId,
        ReservationExpiryReason = m.ReservationExpiryReason,
        RowVersion = Convert.ToBase64String(m.RowVersion),
        Lines = lines.OrderBy(x => x.LineNo).Select(x => new InventoryMovementLineDto {
            InventoryItemId = x.InventoryItemId, LineNo = x.LineNo, Quantity = x.Quantity,
            FulfilledQuantity = x.FulfilledQuantity, UnitCost = x.UnitCost, LotOrBatchNo = x.LotOrBatchNo }).ToArray()
    };
    private async Task<InventoryMovementDto> MapAsync(InventoryMovement movement, CancellationToken ct)
    {
        var lines = await db.InventoryMovementLines.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.InventoryMovementId == movement.Id && !x.IsDeleted).OrderBy(x => x.LineNo).ToListAsync(ct);
        var reversed = await db.InventoryMovements.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            x.ReversalOfMovementId == movement.Id && x.StateCode == "Posted" && !x.IsDeleted, ct);
        string? transferState = null;
        if (movement.SourceType == "TransferDispatch")
        {
            var receipt = await db.InventoryMovements.AsNoTracking()
                .Where(x => x.TenantId == user.TenantId && x.SourceType == "TransferReceipt" &&
                    x.SourceId == movement.Id && !x.IsDeleted)
                .Select(x => new { x.Id }).FirstOrDefaultAsync(ct);
            var receiptReversed = receipt != null && await db.InventoryMovements.AsNoTracking()
                .AnyAsync(x => x.TenantId == user.TenantId && x.ReversalOfMovementId == receipt.Id &&
                    x.StateCode == "Posted" && !x.IsDeleted, ct);
            transferState = reversed ? "Cancelled" : receipt == null ? "InTransit" :
                receiptReversed ? "ReceiptReversed" : "Received";
        }
        return Map(movement, lines, reversed, transferState);
    }
}