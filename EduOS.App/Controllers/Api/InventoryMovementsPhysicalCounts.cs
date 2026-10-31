using EduOS.Core.DTOs.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api;

public sealed partial class InventoryMovementsController
{
    [HttpGet("counts"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> ListPhysicalCounts(long? locationId, string? state, int page = 1, int pageSize = 25,
        CancellationToken ct = default)
        => Reply(await service.GetPhysicalCountsAsync(locationId, state, page, pageSize, ct));

    [HttpGet("counts/{reference:guid}"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> PhysicalCount(Guid reference, CancellationToken ct)
        => Reply(await service.GetPhysicalCountAsync(reference, ct));

    [HttpPost("counts"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> StartPhysicalCount([FromBody] StartInventoryPhysicalCountRequestDto request, CancellationToken ct)
        => Reply(await service.StartPhysicalCountAsync(request, ct));

    [HttpPost("counts/{reference:guid}/submit"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> SubmitPhysicalCount(Guid reference, [FromBody] SubmitInventoryPhysicalCountRequestDto request, CancellationToken ct)
        => Reply(await service.SubmitPhysicalCountAsync(reference, request, ct));

    [HttpPost("counts/{reference:guid}/review"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager")]
    public async Task<IActionResult> ReviewPhysicalCount(Guid reference, [FromBody] ReviewInventoryPhysicalCountRequestDto request, CancellationToken ct)
        => Reply(await service.ReviewPhysicalCountAsync(reference, request, ct));
}
