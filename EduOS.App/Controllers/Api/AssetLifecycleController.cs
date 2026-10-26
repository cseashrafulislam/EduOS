using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
namespace EduOS.App.Controllers.Api;
[ApiController, Route("api/inventory/assets"), Authorize, RequireModule("INVENTORY"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed partial class AssetLifecycleController : ControllerBase
{
    private readonly IAssetLifecycleService service;
    public AssetLifecycleController(IAssetLifecycleService implementation) => service = implementation;
    private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
    [HttpGet("categories"), Authorize(Roles="TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Categories(CancellationToken ct) => Reply(await service.GetCategoriesAsync(ct));
    [HttpGet, Authorize(Roles="TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Assets(int page=1, int pageSize=25, string? search=null, CancellationToken ct=default)
        => Reply(await service.GetAssetsAsync(page, pageSize, search, ct));
    [HttpGet("{reference:guid}/assignments"), Authorize(Roles="TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> History(Guid reference, CancellationToken ct)
        => Reply(await service.GetAssignmentHistoryAsync(reference, ct));
}
