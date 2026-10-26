using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api;

public sealed partial class InventoryMovementsController
{
    [HttpGet("stock-position"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> StockPosition(long locationId, DateTime? asOfUtc,
        string? search, bool includeZeroStock = true, int page = 1, int pageSize = 50,
        CancellationToken ct = default)
        => Reply(await service.GetStockPositionAsync(locationId, asOfUtc, search, includeZeroStock, page, pageSize, ct));
}
