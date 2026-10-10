using EduOS.App.Authorization;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Inventory;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace EduOS.App.Controllers.Api;
[ApiController, Route("api/inventory/catalog"), Authorize, RequireModule("INVENTORY"), AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InventoryCatalogController : ControllerBase
{
    private readonly IInventoryCatalogService service;
    public InventoryCatalogController(EduOSDbContext db, ICurrentUserService user, TimeProvider clock, ILogger<InventoryCatalogService> logger)
        => service = new InventoryCatalogService(db, user, clock, logger);
    [HttpGet("items"), Authorize(Roles="TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Items(int page=1,int pageSize=25,string? search=null,CancellationToken ct=default)
        => Reply(await service.GetItemsAsync(page,pageSize,search,ct));
    [HttpPost("items"), Authorize(Roles="TenantAdmin,Principal,InventoryManager")]
    public async Task<IActionResult> CreateItem([FromBody] SaveInventoryItemRequestDto request,CancellationToken ct)
        => Reply(await service.SaveItemAsync(null,request,ct));
    [HttpPut("items/{id:long}"), Authorize(Roles="TenantAdmin,Principal,InventoryManager")]
    public async Task<IActionResult> UpdateItem(long id,[FromBody] SaveInventoryItemRequestDto request,CancellationToken ct)
        => Reply(await service.SaveItemAsync(id,request,ct));
    [HttpGet("locations"), Authorize(Roles="TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Locations(int? page=null,int? pageSize=null,long? campusId=null,string? search=null,CancellationToken ct=default)
    {
        if (!page.HasValue && !pageSize.HasValue && search == null)
            return Reply(await service.GetLocationsAsync(campusId,ct));
        return Reply(await service.GetLocationsPageAsync(page ?? 1,pageSize ?? 25,campusId,search,ct));
    }
    [HttpPost("locations"), Authorize(Roles="TenantAdmin,Principal,InventoryManager")]
    public async Task<IActionResult> CreateLocation([FromBody] SaveInventoryLocationRequestDto request,CancellationToken ct)
        => Reply(await service.SaveLocationAsync(null,request,ct));
    [HttpPut("locations/{id:long}"), Authorize(Roles="TenantAdmin,Principal,InventoryManager")]
    public async Task<IActionResult> UpdateLocation(long id,[FromBody] SaveInventoryLocationRequestDto request,CancellationToken ct)
        => Reply(await service.SaveLocationAsync(id,request,ct));
    private IActionResult Reply<T>(EduOS.Core.Common.ApiResponse<T> response)=>StatusCode(response.StatusCode,response);
}
