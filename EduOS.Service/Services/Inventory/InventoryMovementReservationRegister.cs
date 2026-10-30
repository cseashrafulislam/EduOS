using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

public sealed partial class InventoryMovementService
{
    /// <summary>
    /// Read-only, line-level register of live holds from the canonical movement aggregate.
    /// The stock ledger remains the sole source of posted on-hand quantities.
    /// </summary>
    public async Task<ApiResponse<PagedResult<InventoryReservationRegisterRowDto>>> GetReservationRegisterAsync(
        long? locationId, string? search, int page, int pageSize, CancellationToken ct = default,
        bool includeClosed = false)
    {
        if (!Readable || db.CurrentTenantId != user.TenantId)
            return Denied<PagedResult<InventoryReservationRegisterRowDto>>();
        if (locationId.HasValue && locationId.Value <= 0)
            return ApiResponse<PagedResult<InventoryReservationRegisterRowDto>>.ErrorResponse("Invalid location.", 400);
        search = search?.Trim();
        if (search?.Length > 100)
            return ApiResponse<PagedResult<InventoryReservationRegisterRowDto>>.ErrorResponse("Search exceeds 100 characters.", 400);
        if (locationId.HasValue && !await Locations().AnyAsync(x => x.Id == locationId.Value, ct))
            return ApiResponse<PagedResult<InventoryReservationRegisterRowDto>>.ErrorResponse("Location not found.", 404);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 250);
        var query =
            from line in db.InventoryMovementLines.AsNoTracking()
            join movement in Visible() on line.InventoryMovementId equals movement.Id
            join item in db.InventoryItems.AsNoTracking() on line.InventoryItemId equals item.Id
            where line.TenantId == user.TenantId && !line.IsDeleted &&
                  item.TenantId == user.TenantId && !item.IsDeleted &&
                  (movement.StateCode == "Reserved" ||
                      (includeClosed && (movement.StateCode == "Expired" ||
                          movement.StateCode == "Cancelled" || movement.StateCode == "Fulfilled"))) &&
                  (movement.StateCode != "Reserved" || line.Quantity > line.FulfilledQuantity) &&
                  movement.FromLocationId.HasValue &&
                  (movement.MovementTypeCode == "Issue" || movement.MovementTypeCode == "Transfer")
            select new { line, movement, item };
        if (locationId.HasValue)
            query = query.Where(x => x.movement.FromLocationId == locationId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search;
            query = query.Where(x => x.movement.MovementNumber.Contains(term) ||
                x.item.Code.Contains(term) || x.item.Name.Contains(term) ||
                (x.line.LotOrBatchNo != null && x.line.LotOrBatchNo.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        var rows = offset > int.MaxValue
            ? new List<InventoryReservationRegisterRowDto>()
            : await query.OrderByDescending(x => x.movement.UpdatedAt)
                .ThenByDescending(x => x.movement.Id).ThenBy(x => x.line.LineNo)
                .Skip((int)offset).Take(pageSize)
                .Select(x => new InventoryReservationRegisterRowDto
                {
                    MovementReference = x.movement.PublicId,
                    MovementNumber = x.movement.MovementNumber,
                    MovementTypeCode = x.movement.MovementTypeCode,
                    FromLocationId = x.movement.FromLocationId!.Value,
                    ToLocationId = x.movement.ToLocationId,
                    SourceType = x.movement.SourceType,
                    SourceId = x.movement.SourceId,
                    HeldSinceUtc = x.movement.UpdatedAt ?? x.movement.CreatedAt,
                    StateCode = x.movement.StateCode,
                    RowVersion = Convert.ToBase64String(x.movement.RowVersion),
                    ReservationExpiresAtUtc = x.movement.ReservationExpiresAtUtc,
                    ReservationExpiredAtUtc = x.movement.ReservationExpiredAtUtc,
                    ReservationExpiryReason = x.movement.ReservationExpiryReason,
                    ReleasedQuantity = x.movement.StateCode == "Expired" ||
                        x.movement.StateCode == "Cancelled" ?
                        x.line.Quantity - x.line.FulfilledQuantity : 0m,
                    LineNo = x.line.LineNo,
                    InventoryItemId = x.item.Id,
                    ItemCode = x.item.Code,
                    ItemName = x.item.Name,
                    UnitCode = x.item.UnitCode,
                    LotOrBatchNo = x.line.LotOrBatchNo,
                    RequestedQuantity = x.line.Quantity,
                    FulfilledQuantity = x.line.FulfilledQuantity,
                    ReservedQuantity = x.movement.StateCode == "Reserved" ?
                        x.line.Quantity - x.line.FulfilledQuantity : 0m
                }).ToListAsync(ct);
        // SQL datetime2 does not retain DateTime.Kind. These instants are UTC
        // by contract; make the wire/CSV representation unambiguous.
        foreach (var row in rows)
        {
            row.HeldSinceUtc = DateTime.SpecifyKind(row.HeldSinceUtc, DateTimeKind.Utc);
            if (row.ReservationExpiresAtUtc.HasValue)
                row.ReservationExpiresAtUtc = DateTime.SpecifyKind(
                    row.ReservationExpiresAtUtc.Value, DateTimeKind.Utc);
            if (row.ReservationExpiredAtUtc.HasValue)
                row.ReservationExpiredAtUtc = DateTime.SpecifyKind(
                    row.ReservationExpiredAtUtc.Value, DateTimeKind.Utc);
        }
        return ApiResponse<PagedResult<InventoryReservationRegisterRowDto>>.SuccessResponse(
            new PagedResult<InventoryReservationRegisterRowDto>
            {
                Items = rows, Page = page, PageSize = pageSize, TotalCount = total
            });
    }
}
