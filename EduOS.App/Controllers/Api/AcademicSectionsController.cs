using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Service.Services.Academic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/academic-sections")]
[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,AdmissionOfficer")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AcademicSectionsController : ControllerBase
{
    private readonly AcademicSectionService _service;
    public AcademicSectionsController(AcademicSectionService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] long? academicBatchId, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] long? campusId = null, [FromQuery] long? academicLevelId = null,
        CancellationToken ct = default) =>
        ToAction(await _service.ListAsync(academicBatchId, search, page, pageSize, ct,
            campusId, academicLevelId));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct) =>
        ToAction(await _service.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = "TenantAdmin")]
    [RequireModule("ACADEMIC")]
    public async Task<IActionResult> Create([FromBody] CreateAcademicSectionRequestDto request, CancellationToken ct) =>
        ToAction(await _service.CreateAsync(request, ct));

    [HttpPut("{id:long}")]
    [Authorize(Roles = "TenantAdmin")]
    [RequireModule("ACADEMIC")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateAcademicSectionRequestDto request, CancellationToken ct) =>
        ToAction(await _service.UpdateAsync(id, request, ct));

    [HttpDelete("{id:long}")]
    [Authorize(Roles = "TenantAdmin")]
    [RequireModule("ACADEMIC")]
    public async Task<IActionResult> Delete(long id, [FromBody] DeleteAcademicSectionRequestDto request, CancellationToken ct) =>
        ToAction(await _service.DeleteAsync(id, request, ct));

    private IActionResult ToAction<T>(ApiResponse<T> response) =>
        StatusCode(response.StatusCode <= 0 ? (response.Success ? 200 : 400) : response.StatusCode, response);
}
