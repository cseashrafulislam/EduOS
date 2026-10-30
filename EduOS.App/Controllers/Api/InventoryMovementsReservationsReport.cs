using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api;

public sealed partial class InventoryMovementsController
{
    [HttpGet("reservations"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> ReservationRegister(long? locationId, string? search,
        bool includeClosed = false, bool overdueOnly = false, int page = 1, int pageSize = 25, CancellationToken ct = default)
        => Reply(await service.GetReservationRegisterAsync(locationId, search, page, pageSize, ct, includeClosed, overdueOnly));

    [HttpGet("reservations/export"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> ExportReservations(long? locationId, string? search,
        bool includeClosed = false, bool overdueOnly = false, CancellationToken ct = default)
    {
        // Refetch and authorize at export time; never trust a cached browser page.
        var result = await service.GetReservationRegisterAsync(locationId, search, 1, 250, ct, includeClosed, overdueOnly);
        if (!result.Success || result.Data == null) return Reply(result);
        if (result.Data.TotalCount > 250)
            return UnprocessableEntity(new { success = false, message = "Narrow the filters to 250 reservation lines or fewer before export." });

        static string Cell(object? value)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            text = text.Replace("\r", " ").Replace("\n", " ");
            if (text.Length > 0 && "=+-@\t".Contains(text[0])) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        var csv = new StringBuilder("\uFEFFMovement,Type,State,Source,From,To,Held UTC,Hold Start Estimated,Age Days,Overdue,Deadline Scheduled UTC,Scheduled By User ID,Deadline UTC,Expired UTC,Expiry Reason,Line,Item Code,Item,Unit,Lot,Requested Quantity,Fulfilled Quantity,Reserved Quantity,Released Quantity\r\n");
        foreach (var row in result.Data.Items)
            csv.AppendJoin(",", new[]
            {
                Cell(row.MovementNumber), Cell(row.MovementTypeCode), Cell(row.StateCode), Cell(row.SourceType),
                Cell(row.FromLocationId), Cell(row.ToLocationId),
                Cell(row.HeldSinceUtc.ToString("O", CultureInfo.InvariantCulture)),
                Cell(row.HoldStartIsEstimated), Cell(row.HoldAgeDays), Cell(row.IsOverdue),
                Cell(row.ReservationDeadlineScheduledAtUtc?.ToString("O", CultureInfo.InvariantCulture)),
                Cell(row.ReservationDeadlineScheduledByUserId),
                Cell(row.ReservationExpiresAtUtc?.ToString("O", CultureInfo.InvariantCulture)),
                Cell(row.ReservationExpiredAtUtc?.ToString("O", CultureInfo.InvariantCulture)),
                Cell(row.ReservationExpiryReason),
                Cell(row.LineNo), Cell(row.ItemCode), Cell(row.ItemName),
                Cell(row.UnitCode), Cell(row.LotOrBatchNo), Cell(row.RequestedQuantity),
                Cell(row.FulfilledQuantity), Cell(row.ReservedQuantity), Cell(row.ReleasedQuantity)
            }).Append("\r\n");
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            includeClosed ? "inventory-reservation-history.csv" : "inventory-active-reservations.csv");
    }
}
