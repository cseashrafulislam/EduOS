using EduOS.App.Authorization;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
namespace EduOS.App.Controllers.Api;
[ApiController]
[Route("api/library/branches")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("LIBRARY")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LibraryBranchesController : ControllerBase
{
    private readonly ILibraryBranchService _service;
    // Activate from registered generic repositories without editing Platform-owned global DI.
    public LibraryBranchesController(IServiceProvider provider) =>
        _service = ActivatorUtilities.CreateInstance<LibraryBranchService>(provider);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] long? campusId, CancellationToken ct) =>
        Reply(await _service.ListAsync(campusId, ct));

    [HttpGet("campuses")]
    public async Task<IActionResult> Campuses(CancellationToken ct) =>
        Reply(await _service.CampusesAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveLibraryBranchRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Reply(await _service.SaveAsync(null, request, ct));
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] SaveLibraryBranchRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Reply(await _service.SaveAsync(id, request, ct));
    }

    private IActionResult Reply<T>(EduOS.Core.Common.ApiResponse<T> result) => StatusCode(result.StatusCode, result);
}