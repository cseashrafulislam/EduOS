using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduOS.App.Controllers.Api;
public sealed partial class AssetLifecycleController
{
    [HttpGet("maintenance/due")]
    [Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> MaintenanceDue(
        [FromQuery] DateOnly? dueBy, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Reply(await service.GetMaintenanceDueAsync(dueBy, page, pageSize, ct));
}
