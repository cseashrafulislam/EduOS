using EduOS.App.Authorization;
using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize]
[ApiController]
[Route("api/hr-payroll")]
[RequireModule("PAYROLL")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class HrPayrollController : ControllerBase
{
    private readonly IPayrollAdministrationService _service;
    public HrPayrollController(IPayrollAdministrationService service) => _service = service;

    [HttpGet("salary-components")]
    [Authorize(Roles = "TenantAdmin,Principal,HR,Accountant")]
    public async Task<IActionResult> Components(CancellationToken ct) =>
        Result(await _service.GetSalaryComponentsAsync(ct));

    [HttpPost("salary-components")]
    [HttpPut("salary-components/{componentId:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,HR,Accountant")]
    public async Task<IActionResult> SaveComponent(long? componentId,
        [FromBody] SaveSalaryComponentRequestDto request, CancellationToken ct) =>
        Result(await _service.SaveSalaryComponentAsync(componentId, request, ct));

    [HttpPost("salary-structures")]
    [Authorize(Roles = "TenantAdmin,Principal,HR,Accountant")]
    public async Task<IActionResult> SaveSalary([FromBody] SaveSalaryStructureRequestDto request,
        CancellationToken ct) =>
        Result(await _service.SaveSalaryStructureAsync(request, ct));

    [HttpPost("runs")]
    [Authorize(Roles = "TenantAdmin,Principal,HR,Accountant")]
    public async Task<IActionResult> CreateRun([FromBody] CreatePayrollRunRequestDto request,
        CancellationToken ct) =>
        Result(await _service.CreatePayrollRunAsync(request, ct));

    [HttpGet("runs")]
    [Authorize(Roles = "TenantAdmin,Principal,HR,Accountant")]
    public async Task<IActionResult> Runs([FromQuery] int? year, [FromQuery] int? month,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Result(await _service.GetPayrollRunsAsync(year, month, page, pageSize, ct));

    [HttpGet("runs/{reference:guid}")]
    [Authorize(Roles = "TenantAdmin,Principal,HR,Accountant")]
    public async Task<IActionResult> Run(Guid reference, CancellationToken ct) =>
        Result(await _service.GetPayrollRunAsync(reference, ct));

    [HttpGet("runs/{reference:guid}/employees")]
    [Authorize(Roles = "TenantAdmin,Principal,HR,Accountant")]
    public async Task<IActionResult> Employees(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Result(await _service.GetPayrollEmployeesAsync(reference, page, pageSize, ct));

    [HttpPost("runs/{reference:guid}/approve")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Approve(Guid reference,
        [FromBody] ApprovePayrollRunRequestDto request, CancellationToken ct) =>
        Result(await _service.ApprovePayrollRunAsync(reference, request, ct));

    [HttpPost("runs/{reference:guid}/post")]
    [Authorize(Roles = "TenantAdmin,Principal,Accountant")]
    public async Task<IActionResult> Post(Guid reference,
        [FromBody] PostPayrollRunRequestDto request, CancellationToken ct) =>
        Result(await _service.PostPayrollRunAsync(reference, request, ct));

    [HttpPost("bonuses")]
    [HttpPut("bonuses/{bonusId:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,HR,Accountant")]
    public async Task<IActionResult> Bonus(long? bonusId, [FromBody] SaveBonusRequestDto request,
        CancellationToken ct) =>
        Result(await _service.SaveBonusAsync(bonusId, request, ct));

    [HttpPost("loans")]
    [Authorize(Roles = "TenantAdmin,Principal,HR,Accountant")]
    public async Task<IActionResult> Loan([FromBody] CreateLoanAdvanceRequestDto request,
        CancellationToken ct) =>
        Result(await _service.CreateLoanAdvanceAsync(request, ct));

    [HttpGet("my-payslips")]
    public async Task<IActionResult> MyPayslips([FromQuery] int? year, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Result(await _service.GetMyPayslipsAsync(year, page, pageSize, ct));

    [HttpPost("payments")]
    [Authorize(Roles = "TenantAdmin,Principal,HR,Accountant")]
    public async Task<IActionResult> Payment([FromBody] RecordPayrollPaymentRequestDto request,
        CancellationToken ct) =>
        Result(await _service.RecordPayrollPaymentAsync(request, ct));

    private IActionResult Result<T>(EduOS.Core.Common.ApiResponse<T> response) =>
        StatusCode(response.StatusCode, response);
}
