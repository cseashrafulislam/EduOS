using EduOS.Core.DTOs.System;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/custom-fields")]
[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomFieldsApiController(ICustomFieldService service, ICurrentUserService user) : ControllerBase
{
    [HttpGet("definitions")]
    public async Task<IActionResult> Definitions(int page = 1, int pageSize = 20, CancellationToken ct = default)
    { var r = await service.GetDefinitionsAsync("Tenant", page, pageSize, ct); return StatusCode(r.StatusCode, r); }
    [HttpPost("definitions")]
    public async Task<IActionResult> Create([FromBody] SaveCustomFieldDefinitionRequestDto dto, CancellationToken ct)
    { var r = await service.SaveDefinitionAsync(null, dto, ct); return StatusCode(r.StatusCode, r); }
    [HttpPut("definitions/{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] SaveCustomFieldDefinitionRequestDto dto, CancellationToken ct)
    { var r = await service.SaveDefinitionAsync(id, dto, ct); return StatusCode(r.StatusCode, r); }
    [HttpGet("tenant-values")]
    public async Task<IActionResult> Values(CancellationToken ct)
    { var r = await service.GetValuesAsync("Tenant", user.TenantId, ct); return StatusCode(r.StatusCode, r); }
    [HttpPut("tenant-values")]
    public async Task<IActionResult> SaveValues([FromBody] List<SaveCustomFieldValueRequestDto> dto, CancellationToken ct)
    { var r = await service.SaveValuesAsync("Tenant", user.TenantId, dto, ct); return StatusCode(r.StatusCode, r); }
}
