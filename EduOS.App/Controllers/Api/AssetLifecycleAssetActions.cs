using EduOS.Core.DTOs.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduOS.App.Controllers.Api;
public sealed partial class AssetLifecycleController
{
    [HttpPost, Authorize(Roles="TenantAdmin,Principal,InventoryManager")]
    public async Task<IActionResult> CreateAsset([FromBody] SaveAssetRequestDto r, CancellationToken ct)
        => Reply(await service.SaveAssetAsync(null, r, ct));
    [HttpPut("{reference:guid}"), Authorize(Roles="TenantAdmin,Principal,InventoryManager")]
    public async Task<IActionResult> UpdateAsset(Guid reference, [FromBody] SaveAssetRequestDto r, CancellationToken ct)
        => Reply(await service.SaveAssetAsync(reference, r, ct));
}
