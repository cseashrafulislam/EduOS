using EduOS.Core.DTOs.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduOS.App.Controllers.Api;
public sealed partial class InventoryMovementsController
{
    [HttpGet, Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> List(DateTime? fromUtc, DateTime? toUtc, int page = 1, int pageSize = 25, CancellationToken ct = default)
        => Reply(await service.GetMovementsAsync(fromUtc, toUtc, page, pageSize, ct));
    [HttpGet("{reference:guid}"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Detail(Guid reference, CancellationToken ct) => Reply(await service.GetMovementAsync(reference, ct));
    [HttpGet("balance"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Balance(long itemId, long locationId, DateTime? asOfUtc, CancellationToken ct)
        => Reply(await service.GetItemBalanceAsync(itemId, locationId, asOfUtc, ct));
    [HttpPost, Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> Create([FromBody] CreateInventoryMovementRequestDto request, CancellationToken ct)
        => Reply(await service.CreateMovementAsync(request, ct));
    [HttpPost("{reference:guid}/post"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager")]
    public async Task<IActionResult> Post(Guid reference, [FromBody] PostInventoryMovementRequestDto request, CancellationToken ct)
        => Reply(await service.PostMovementAsync(reference, request, ct));
    [HttpPost("{reference:guid}/reverse"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> Reverse(Guid reference, [FromBody] ReverseInventoryMovementRequestDto request, CancellationToken ct)
        => Reply(await service.ReverseMovementAsync(reference, request, ct));
}