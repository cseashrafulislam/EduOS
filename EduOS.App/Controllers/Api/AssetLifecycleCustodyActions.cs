using EduOS.Core.DTOs.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduOS.App.Controllers.Api;
public sealed partial class AssetLifecycleController
{
    [HttpPost("assignments"), Authorize(Roles="TenantAdmin,Principal,InventoryManager")]
    public async Task<IActionResult> Assign([FromBody] AssignAssetRequestDto r, CancellationToken ct)
        => Reply(await service.AssignAssetAsync(r, ct));
    [HttpPost("assignments/{id:long}/return"), Authorize(Roles="TenantAdmin,Principal,InventoryManager")]
    public async Task<IActionResult> Return(long id, [FromBody] ReturnAssetRequestDto r, CancellationToken ct)
        => Reply(await service.ReturnAssetAsync(id, r, ct));
    [HttpPost("assignments/{id:long}/transfer"), Authorize(Roles="TenantAdmin,Principal,InventoryManager")]
    public async Task<IActionResult> Transfer(long id, [FromBody] TransferAssetRequestDto r, CancellationToken ct)
        => Reply(await service.TransferAssetAsync(id, r, ct));
    [HttpPost("maintenance"), Authorize(Roles="TenantAdmin,Principal,InventoryManager")]
    public async Task<IActionResult> Maintenance([FromBody] RecordAssetMaintenanceRequestDto r, CancellationToken ct)
        => Reply(await service.RecordMaintenanceAsync(r, ct));
}
