using EduOS.App.Authorization;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/library/liabilities")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("LIBRARY")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LibraryLiabilitiesController(ILibraryLiabilityService service) : ControllerBase
{
    [HttpGet("posting-options")]
    public async Task<IActionResult> PostingOptions([FromQuery] string type, [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await service.PostingOptionsAsync(type, search, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpGet("incidents")]
    public async Task<IActionResult> Incidents([FromQuery] string? search = null,
        [FromQuery] long? campusId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.IncidentsAsync(search, campusId, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("overdue-candidates")]
    public async Task<IActionResult> OverdueCandidates([FromQuery] string? search = null,
        [FromQuery] long? campusId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.OverdueCandidatesAsync(search, campusId, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("fine-policies")]
    public async Task<IActionResult> FinePolicies([FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.FinePoliciesAsync(campusId, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("fine-policies")]
    public async Task<IActionResult> CreateFinePolicy([FromBody] SaveLibraryFinePolicyDto request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.CreateFinePolicyAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet]
    public async Task<IActionResult> Register([FromQuery] string? status = "all",
        [FromQuery] long? campusId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.RegisterAsync(status, campusId, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? status = "all",
        [FromQuery] long? campusId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] string language = "en-BD",
        CancellationToken ct = default)
    {
        if (language is not ("en-BD" or "bn-BD")) return BadRequest("Unsupported language.");
        var result = await service.RegisterAsync(status, campusId, page, pageSize, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        return File(LibraryLiabilityCsvWriter.Write(result.Data.Items, language == "bn-BD"),
            "text/csv; charset=utf-8", "library-liabilities-page.csv");
    }

    [HttpPost]
    public async Task<IActionResult> Assess([FromBody] AssessLibraryLiabilityRequestDto request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.AssessAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/review")]
    public async Task<IActionResult> Review(Guid reference, [FromBody] ReviewLibraryLiabilityRequestDto request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.ReviewAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/reversal")]
    public async Task<IActionResult> CurrentReversal(Guid reference, CancellationToken ct = default)
    {
        var result = await service.CurrentReversalAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/reversal")]
    public async Task<IActionResult> RequestReversal(Guid reference,
        [FromBody] RequestLibraryReversalDto request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.RequestReversalAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/reversal/{reversalReference:guid}/review")]
    public async Task<IActionResult> ReviewReversal(Guid reference, Guid reversalReference,
        [FromBody] ReviewLibraryReversalDto request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.ReviewReversalAsync(reference, reversalReference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
