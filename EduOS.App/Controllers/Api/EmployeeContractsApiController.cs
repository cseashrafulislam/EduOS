using EduOS.App.Authorization;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/hr/employee-contracts")]
[Authorize(Roles = "TenantAdmin"), RequireModule("HR")]
[AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EmployeeContractsApiController : ControllerBase
{
    private readonly IEmployeeContractService service;
    public EmployeeContractsApiController(IEmployeeContractService service) => this.service = service;
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid employeeReference,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15, CancellationToken ct = default)
    {
        var r = await service.ListAsync(employeeReference, page, pageSize, ct);
        return StatusCode(r.StatusCode, r);
    }
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeContractRequestDto request, CancellationToken ct)
    {
        var r = await service.CreateAsync(request, ct);
        return StatusCode(r.StatusCode, r);
    }
    [HttpPost("{reference:guid}/approve")]
    public async Task<IActionResult> Approve(Guid reference,
        [FromBody] ReviewEmployeeContractRequestDto request, CancellationToken ct)
    {
        var r = await service.ReviewAsync(reference, request, true, ct);
        return StatusCode(r.StatusCode, r);
    }
    [HttpPost("{reference:guid}/reject")]
    public async Task<IActionResult> Reject(Guid reference,
        [FromBody] ReviewEmployeeContractRequestDto request, CancellationToken ct)
    {
        var r = await service.ReviewAsync(reference, request, false, ct);
        return StatusCode(r.StatusCode, r);
    }
}
