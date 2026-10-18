using EduOS.Core.Common;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Authorize(Roles = "TenantAdmin,Principal")]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/import-history")]
public sealed class ImportHistoryController : ControllerBase
{
    private readonly IImportHistoryService _service;
    public ImportHistoryController(IImportHistoryService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? importType, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        Reply(await _service.GetImportsAsync(importType, page, pageSize, cancellationToken));

    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Details(Guid reference, CancellationToken cancellationToken) =>
        Reply(await _service.GetImportAsync(reference, cancellationToken));

    [HttpGet("{reference:guid}/rows")]
    public async Task<IActionResult> Rows(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        Reply(await _service.GetImportRowsAsync(reference, page, pageSize, cancellationToken));

    [HttpGet("{reference:guid}/failures/export")]
    public async Task<IActionResult> ExportFailures(Guid reference, CancellationToken cancellationToken)
    {
        var result = await _service.ExportFailedRowsCsvAsync(reference, cancellationToken);
        if (!result.Success) return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(System.Text.Encoding.UTF8.GetBytes(result.Data ?? string.Empty),
            "text/csv; charset=utf-8", $"import-failures-{reference:N}.csv");
    }

    private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
