using EduOS.App.Authorization;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/guardian-communication-preferences")]
[Authorize(Roles = "Guardian,Parent")]
[RequireModule("STUDENT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GuardianCommunicationPreferencesController : ControllerBase
{
    private readonly IGuardianCommunicationPreferenceService _service;

    // Platform owns the global DI registrations; construct this scoped service from its dependencies.
    public GuardianCommunicationPreferencesController(IServiceProvider services) =>
        _service = ActivatorUtilities.CreateInstance<GuardianCommunicationPreferenceService>(services);

    [HttpGet]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var result = await _service.GetMineAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("{linkId:long}")]
    public async Task<IActionResult> Update(long linkId,
        [FromBody] UpdateGuardianCommunicationPreferenceDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.UpdateMineAsync(linkId, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
