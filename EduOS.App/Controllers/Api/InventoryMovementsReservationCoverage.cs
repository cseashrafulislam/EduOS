using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api;

public sealed partial class InventoryMovementsController
{
    [HttpGet("reservations/coverage")]
    [Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> ReservationCoverage(long locationId, string? search,
        bool overdueOnly = false, int page = 1, int pageSize = 25, CancellationToken ct = default)
        => Reply(await service.GetReservationCoverageAsync(locationId, search, overdueOnly,
            page, pageSize, ct));

    [HttpGet("reservations/coverage/export")]
    [Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> ExportReservationCoverage(long locationId, string? search,
        bool overdueOnly = false, CancellationToken ct = default)
    {
        var result = await service.GetReservationCoverageAsync(locationId, search, overdueOnly,
            1, 250, ct);
        if (!result.Success || result.Data == null) return Reply(result);
        if (result.Data.TotalCount > 250)
            return UnprocessableEntity(new
            {
                success = false,
                message = "Narrow the filters to 250 item/lot groups or fewer."
            });

        static string Cell(object? value)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            text = text.Replace("\r", " ").Replace("\n", " ");
            if (text.Length > 0 && "=+-@\t".Contains(text[0])) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        var csv = new StringBuilder("\uFEFFItem Code,Item,Unit,Lot,On Hand,Active Reserved,Available,Overdue Reserved,Shortfall,Hold Lines,Oldest Hold UTC,Age Days,Status,As Of UTC\r\n");
        foreach (var row in result.Data.Items)
            csv.AppendJoin(",", new[]
            {
                Cell(row.ItemCode), Cell(row.ItemName), Cell(row.UnitCode),
                Cell(row.LotOrBatchNo), Cell(row.QuantityOnHand),
                Cell(row.ReservedQuantity), Cell(row.AvailableQuantity),
                Cell(row.OverdueReservedQuantity), Cell(row.ShortfallQuantity),
                Cell(row.ActiveHoldLines),
                Cell(row.OldestHoldUtc.ToString("O", CultureInfo.InvariantCulture)),
                Cell(row.OldestHoldAgeDays), Cell(row.StatusCode),
                Cell(row.AsOfUtc.ToString("O", CultureInfo.InvariantCulture))
            }).Append("\r\n");
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            "inventory-reservation-coverage.csv");
    }
}
