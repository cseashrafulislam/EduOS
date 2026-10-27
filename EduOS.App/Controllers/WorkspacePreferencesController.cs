using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("workspace")]
public sealed class WorkspacePreferencesController(IWorkspacePreferencesService service) : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("/api/workspace-preferences")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("/api/workspace-preferences")]
    public async Task<IActionResult> Save([FromBody] SaveWorkspacePreferencesRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await service.SaveAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("/api/workspace-preferences/reset")]
    public async Task<IActionResult> Reset([FromBody] ResetWorkspacePreferencesRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await service.ResetAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
