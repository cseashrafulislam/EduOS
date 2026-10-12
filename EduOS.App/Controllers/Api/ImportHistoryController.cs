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

    private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
