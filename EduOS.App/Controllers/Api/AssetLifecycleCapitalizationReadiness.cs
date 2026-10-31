using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api;

public sealed partial class AssetLifecycleController
{
    [HttpGet("{reference:guid}/capitalization-readiness")]
    [Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> CapitalizationReadiness(Guid reference, CancellationToken ct)
        => Reply(await service.GetCapitalizationReadinessAsync(reference, ct));
}
