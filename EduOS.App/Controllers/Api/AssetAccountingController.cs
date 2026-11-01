using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Service.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/inventory/asset-accounting"), Authorize,
 RequireModule("INVENTORY"), RequireModule("FINANCE"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssetAccountingController(AssetAccountingService service) : ControllerBase
{
    private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

    [HttpGet("options"), Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Options(CancellationToken ct) => Reply(await service.OptionsAsync(ct));

    [HttpGet("{asset:guid}/statement"), Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Statement(Guid asset, CancellationToken ct) =>
        Reply(await service.StatementAsync(asset, ct));

    [HttpGet("{asset:guid}/capitalization"), Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Capitalization(Guid asset, CancellationToken ct) =>
        Reply(await service.GetCapitalizationAsync(asset, ct));

    [HttpPost("{asset:guid}/capitalization"), Authorize(Roles = "TenantAdmin,Accountant")]
    public async Task<IActionResult> RequestCapitalization(Guid asset,
        [FromBody] CreateAssetCapitalizationRequest request, CancellationToken ct) =>
        Reply(await service.RequestCapitalizationAsync(asset, request, ct));

    [HttpPost("capitalizations/{reference:guid}/review"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> ReviewCapitalization(Guid reference,
        [FromBody] ReviewAssetCapitalizationRequest request, CancellationToken ct) =>
        Reply(await service.ReviewCapitalizationAsync(reference, request, ct));

    [HttpPost("capitalizations/{reference:guid}/reversal"), Authorize(Roles = "TenantAdmin,Accountant")]
    public async Task<IActionResult> RequestCapitalizationReversal(Guid reference,
        [FromBody] AssetReversalRequest request, CancellationToken ct) =>
        Reply(await service.RequestCapitalizationReversalAsync(reference, request, ct));

    [HttpPost("capitalizations/{reference:guid}/reversal/{reversal:guid}/review"),
     Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> ReviewCapitalizationReversal(Guid reference, Guid reversal,
        [FromBody] AssetReviewRequest request, CancellationToken ct) =>
        Reply(await service.ReviewCapitalizationReversalAsync(reference, reversal, request, ct));

    [HttpPost("{asset:guid}/depreciation-policy"), Authorize(Roles = "TenantAdmin,Accountant")]
    public async Task<IActionResult> RequestDepreciationPolicy(Guid asset,
        [FromBody] CreateAssetDepreciationPolicyRequest request, CancellationToken ct) =>
        Reply(await service.RequestDepreciationPolicyAsync(asset, request, ct));

    [HttpPost("depreciation-policy/{reference:guid}/review"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> ReviewDepreciationPolicy(Guid reference,
        [FromBody] AssetReviewRequest request, CancellationToken ct) =>
        Reply(await service.ReviewDepreciationPolicyAsync(reference, request, ct));

    [HttpPost("{asset:guid}/depreciation"), Authorize(Roles = "TenantAdmin,Accountant")]
    public async Task<IActionResult> RequestDepreciation(Guid asset,
        [FromBody] AssetValuationRequest request, CancellationToken ct) =>
        Reply(await service.RequestDepreciationAsync(asset, request, ct));

    [HttpPost("depreciation/{id:long}/review"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> ReviewDepreciation(long id,
        [FromBody] AssetReviewRequest request, CancellationToken ct) =>
        Reply(await service.ReviewDepreciationAsync(id, request, ct));

    [HttpPost("{asset:guid}/impairment"), Authorize(Roles = "TenantAdmin,Accountant")]
    public async Task<IActionResult> RequestImpairment(Guid asset,
        [FromBody] AssetValuationRequest request, CancellationToken ct) =>
        Reply(await service.RequestImpairmentAsync(asset, request, ct));

    [HttpPost("impairment/{id:long}/review"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> ReviewImpairment(long id,
        [FromBody] AssetReviewRequest request, CancellationToken ct) =>
        Reply(await service.ReviewImpairmentAsync(id, request, ct));

    [HttpPost("{asset:guid}/disposal"), Authorize(Roles = "TenantAdmin,Accountant")]
    public async Task<IActionResult> RequestDisposal(Guid asset,
        [FromBody] AssetValuationRequest request, CancellationToken ct) =>
        Reply(await service.RequestDisposalAsync(asset, request, ct));

    [HttpPost("disposal/{id:long}/review"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> ReviewDisposal(long id,
        [FromBody] AssetReviewRequest request, CancellationToken ct) =>
        Reply(await service.ReviewDisposalAsync(id, request, ct));

    [HttpPost("{kind:regex(^(Depreciation|Impairment|Disposal)$)}/{id:long}/reversal"),
     Authorize(Roles = "TenantAdmin,Accountant")]
    public async Task<IActionResult> RequestReversal(string kind, long id,
        [FromBody] AssetReversalRequest request, CancellationToken ct) =>
        Reply(await service.RequestEventReversalAsync(kind, id, request, ct));

    [HttpPost("{kind:regex(^(Depreciation|Impairment|Disposal)$)}/{id:long}/reversal/{reversal:guid}/review"),
     Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> ReviewReversal(string kind, long id, Guid reversal,
        [FromBody] AssetReviewRequest request, CancellationToken ct) =>
        Reply(await service.ReviewEventReversalAsync(kind, id, reversal, request, ct));
}
