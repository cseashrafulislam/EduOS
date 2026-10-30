using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api;

public sealed partial class InventoryMovementsController
{
    [HttpGet("reservations"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> ReservationRegister(long? locationId, string? search,
        int page = 1, int pageSize = 25, CancellationToken ct = default)
        => Reply(await service.GetReservationRegisterAsync(locationId, search, page, pageSize, ct));

    [HttpGet("reservations/export"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> ExportReservations(long? locationId, string? search, CancellationToken ct = default)
    {
        // Refetch and authorize at export time; never trust a cached browser page.
        var result = await service.GetReservationRegisterAsync(locationId, search, 1, 250, ct);
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

        var csv = new StringBuilder("\uFEFFMovement,Type,Source,From,To,Held UTC,Line,Item Code,Item,Unit,Lot,Reserved Quantity\r\n");
        foreach (var row in result.Data.Items)
            csv.AppendJoin(",", new[]
            {
                Cell(row.MovementNumber), Cell(row.MovementTypeCode), Cell(row.SourceType),
                Cell(row.FromLocationId), Cell(row.ToLocationId),
                Cell(row.HeldSinceUtc.ToString("O", CultureInfo.InvariantCulture)),
                Cell(row.LineNo), Cell(row.ItemCode), Cell(row.ItemName),
                Cell(row.UnitCode), Cell(row.LotOrBatchNo), Cell(row.ReservedQuantity)
            }).Append("\r\n");
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            "inventory-active-reservations.csv");
    }
}
