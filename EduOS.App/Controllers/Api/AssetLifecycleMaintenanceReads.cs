using EduOS.Core.DTOs.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api;

public sealed partial class AssetLifecycleController
{
    [HttpGet("{reference:guid}/maintenance")]
    [Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> MaintenanceHistory(
        Guid reference, [FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
        => Reply(await service.GetMaintenanceHistoryAsync(reference, page, pageSize, ct));
}
