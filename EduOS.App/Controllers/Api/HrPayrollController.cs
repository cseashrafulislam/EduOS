using EduOS.App.Authorization;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace EduOS.App.Controllers.Api;
[Authorize][ApiController][Route("api/hr-payroll")][AutoValidateAntiforgeryToken]
public sealed class HrPayrollController:ControllerBase
{
 private readonly IHrPayrollService _service;public HrPayrollController(IHrPayrollService service)=>_service=service;
 [HttpPut("salary-structure")][Authorize(Roles="TenantAdmin,Principal,HRManager,Accountant")][RequireModule("PAYROLL")]
 public async Task<IActionResult> Salary([FromBody]SaveSalaryStructureDto r,CancellationToken ct){if(!ModelState.IsValid)return ValidationProblem(ModelState);var x=await _service.SaveSalaryStructureAsync(r,ct);return StatusCode(x.StatusCode,x);}
 [HttpPost("attendance")][Authorize(Roles="TenantAdmin,Principal,HRManager")][RequireModule("HR")][EnableRateLimiting("ApiPolicy")]
 public async Task<IActionResult> Attendance([FromBody]SaveEmployeeAttendanceDto r,CancellationToken ct){if(!ModelState.IsValid)return ValidationProblem(ModelState);var x=await _service.SaveAttendanceAsync(r,ct);return StatusCode(x.StatusCode,x);}
 [HttpPost("generate")][Authorize(Roles="TenantAdmin,Principal,HRManager,Accountant")][RequireModule("PAYROLL")][EnableRateLimiting("ApiPolicy")]
 public async Task<IActionResult> Generate([FromBody]GeneratePayrollDto r,CancellationToken ct){if(!ModelState.IsValid)return ValidationProblem(ModelState);var x=await _service.GeneratePayrollAsync(r,ct);return StatusCode(x.StatusCode,x);}
 [HttpPost("pay")][Authorize(Roles="TenantAdmin,Principal,HRManager,Accountant")][RequireModule("PAYROLL")][EnableRateLimiting("ApiPolicy")]
 public async Task<IActionResult> Pay([FromBody]PayPayrollDto r,CancellationToken ct){if(!ModelState.IsValid)return ValidationProblem(ModelState);var x=await _service.PayAsync(r,ct);return StatusCode(x.StatusCode,x);}
 [HttpGet("my")][Authorize(Roles="Employee,Teacher,HRManager,Principal,TenantAdmin")][RequireModule("PAYROLL")]
 public async Task<IActionResult> My(CancellationToken ct){var x=await _service.GetMyPayrollAsync(ct);return StatusCode(x.StatusCode,x);}
}
