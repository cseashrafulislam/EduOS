using EduOS.Core.DTOs.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduOS.App.Controllers.Api;
public sealed partial class AssetLifecycleController
{
    [HttpPost("categories"), Authorize(Roles="TenantAdmin,Principal")]
    public async Task<IActionResult> CreateCategory([FromBody] SaveAssetCategoryRequestDto r, CancellationToken ct)
        => Reply(await service.SaveCategoryAsync(null, r, ct));
    [HttpPut("categories/{id:long}"), Authorize(Roles="TenantAdmin,Principal")]
    public async Task<IActionResult> UpdateCategory(long id, [FromBody] SaveAssetCategoryRequestDto r, CancellationToken ct)
        => Reply(await service.SaveCategoryAsync(id, r, ct));
}
