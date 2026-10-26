using EduOS.App.Authorization;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.Academic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/academic-setup/prerequisites")]
[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,Teacher")]
[RequireModule("ACADEMIC")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SubjectPrerequisitesController : ControllerBase
{
    private readonly ISubjectPrerequisiteService _service;
    public SubjectPrerequisitesController(ISubjectPrerequisiteService implementation) => _service = implementation;

    [HttpGet("options")]
    public async Task<IActionResult> Options([FromQuery] long curriculumId, CancellationToken ct)
    {
        var result = await _service.OptionsAsync(curriculumId, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] long curriculumId, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _service.SearchAsync(curriculumId, search, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> Create([FromBody] SaveSubjectPrerequisiteRequestDto request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> Update(long id, [FromBody] SaveSubjectPrerequisiteRequestDto request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(id, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpDelete("{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> Archive(long id, [FromQuery] string rowVersion, CancellationToken ct)
    {
        var result = await _service.ArchiveAsync(id, rowVersion, ct);
        return StatusCode(result.StatusCode, result);
    }
}
