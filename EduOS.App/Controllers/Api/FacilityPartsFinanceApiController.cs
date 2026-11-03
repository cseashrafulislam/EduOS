using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Service.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/inventory/facility-parts-finance"), Authorize,
 RequireModule("INVENTORY"), RequireModule("FACILITIES"), RequireModule("FINANCE"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FacilityPartsFinanceApiController(FacilityPartsFinanceService service) : ControllerBase
{
    private IActionResult Reply<T>(ApiResponse<T> result) => StatusCode(result.StatusCode, result);

    [HttpGet("options"), Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Options(string? expenseSearch, string? stockSearch, CancellationToken ct) =>
        Reply(await service.OptionsAsync(expenseSearch, stockSearch, ct));

    [HttpGet("movements"), Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Movements(string? search, int page = 1, int pageSize = 25,
        CancellationToken ct = default) => Reply(await service.ListAsync(search, page, pageSize, ct));

    [HttpGet("{movement:guid}"), Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Get(Guid movement, CancellationToken ct) =>
        Reply(await service.GetAsync(movement, ct));

    [HttpPost("{movement:guid}/recognition"), Authorize(Roles = "TenantAdmin,Accountant")]
    public async Task<IActionResult> Request(Guid movement, [FromBody] FacilityPartsFinanceRequestDto input,
        CancellationToken ct) => Reply(await service.RequestAsync(movement, input, ct));

    [HttpPost("recognitions/{journal:guid}/review"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> Review(Guid journal, [FromBody] FacilityPartsFinanceReviewDto input,
        CancellationToken ct) => Reply(await service.ReviewAsync(journal, input, ct));

    [HttpPost("recognitions/{journal:guid}/reversal"), Authorize(Roles = "TenantAdmin,Accountant")]
    public async Task<IActionResult> RequestReversal(Guid journal,
        [FromBody] FacilityPartsFinanceReversalRequestDto input, CancellationToken ct) =>
        Reply(await service.RequestReversalAsync(journal, input, ct));

    [HttpPost("reversals/{journal:guid}/review"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> ReviewReversal(Guid journal, [FromBody] FacilityPartsFinanceReviewDto input,
        CancellationToken ct) => Reply(await service.ReviewReversalAsync(journal, input, ct));
}
