using EduOS.App.Authorization;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/library")]
[Authorize]
[RequireModule("LIBRARY")]
[AutoValidateAntiforgeryToken]
public sealed class LibraryController : ControllerBase
{
    private readonly ILibraryService _service;
    public LibraryController(ILibraryService service) => _service = service;

    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog([FromQuery] string? search, CancellationToken ct) => ToAction(await _service.GetCatalogAsync(search, ct));

    [HttpGet("my-issues")]
    public async Task<IActionResult> MyIssues(CancellationToken ct) => ToAction(await _service.GetMyIssuesAsync(ct));

    [HttpPost("issues")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Issue([FromBody] IssueBookDto request, CancellationToken ct) => ToAction(await _service.IssueAsync(request, ct));

    [HttpPost("issues/{reference:guid}/close")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Close(Guid reference, [FromBody] ReturnBookDto request, CancellationToken ct) => ToAction(await _service.CloseAsync(reference, request, ct));

    private IActionResult ToAction<T>(EduOS.Core.Common.ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
