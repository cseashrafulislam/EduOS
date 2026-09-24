using EduOS.App.Authorization;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace EduOS.App.Controllers.Api;
[Authorize][ApiController][Route("api/hr")][AutoValidateAntiforgeryToken][EnableRateLimiting("ApiPolicy")][ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)][RequireModule("HR")]
public sealed class HrAdminController:ControllerBase
{
 private readonly IHrAdminService _service;public HrAdminController(IHrAdminService service)=>_service=service;
 [HttpGet("employees")][Authorize(Roles="TenantAdmin,Principal,HR")]public async Task<IActionResult> Employees([FromQuery]HrEmployeeQueryDto r,CancellationToken ct){var x=await _service.GetEmployeesAsync(r,ct);return StatusCode(x.StatusCode,x);}
 [HttpGet("leaves")][Authorize(Roles="TenantAdmin,Principal,HR")]public async Task<IActionResult> Leaves([FromQuery]HrLeaveQueryDto r,CancellationToken ct){var x=await _service.GetEmployeeLeavesAsync(r,ct);return StatusCode(x.StatusCode,x);}
 [HttpPost("leaves/review")][Authorize(Roles="TenantAdmin,Principal,HR")]public async Task<IActionResult> Review([FromBody]ReviewEmployeeLeaveDto r,CancellationToken ct){if(!ModelState.IsValid)return ValidationProblem(ModelState);var x=await _service.ReviewEmployeeLeaveAsync(r,ct);return StatusCode(x.StatusCode,x);}
}
