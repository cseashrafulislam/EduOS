using EduOS.App.Authorization;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,Principal,HR")]
[ApiController]
[Route("api/hr")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequireModule("HR")]
public sealed class HrAdminController : ControllerBase
{
    private readonly IHrAdminService _service;
    public HrAdminController(IHrAdminService service) => _service = service;

    [HttpGet("employees")]
    public async Task<IActionResult> Employees([FromQuery] HrEmployeeQueryDto request, CancellationToken ct) =>
        Result(await _service.GetEmployeesAsync(request, ct));

    [HttpGet("employees/{reference:guid}")]
    public async Task<IActionResult> Employee(Guid reference, CancellationToken ct) =>
        Result(await _service.GetEmployeeAsync(reference, ct));

    [HttpPost("employees")]
    public async Task<IActionResult> CreateEmployee([FromBody] CreateEmployeeRequestDto request, CancellationToken ct) =>
        Result(await _service.CreateEmployeeAsync(request, ct));

    [HttpPut("employees/{reference:guid}")]
    public async Task<IActionResult> UpdateEmployee(Guid reference,
        [FromBody] UpdateEmployeeRequestDto request, CancellationToken ct) =>
        Result(await _service.UpdateEmployeeAsync(reference, request, ct));

    [HttpPost("employees/{reference:guid}/state")]
    public async Task<IActionResult> ChangeEmployeeState(Guid reference,
        [FromBody] ChangeEmployeeStateRequestDto request, CancellationToken ct) =>
        Result(await _service.ChangeEmployeeStateAsync(reference, request, ct));

    [HttpPost("employees/shifts")]
    public async Task<IActionResult> AssignShift([FromBody] AssignEmployeeShiftRequestDto request, CancellationToken ct) =>
        Result(await _service.AssignShiftAsync(request, ct));

    [HttpPost("employees/campuses")]
    public async Task<IActionResult> AssignCampus([FromBody] AssignEmployeeCampusRequestDto request, CancellationToken ct) =>
        Result(await _service.AssignCampusAsync(request, ct));

    [HttpGet("employees/{reference:guid}/assignment-history")]
    public async Task<IActionResult> AssignmentHistory(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await _service.GetAssignmentHistoryAsync(reference, page, pageSize, ct));

    [HttpGet("employees/{reference:guid}/bank-accounts")]
    public async Task<IActionResult> BankAccounts(Guid reference, CancellationToken ct) =>
        Result(await _service.GetBankAccountsAsync(reference, ct));

    [HttpPost("employees/bank-accounts")]
    [HttpPut("employees/bank-accounts/{bankAccountId:long}")]
    public async Task<IActionResult> SaveBankAccount(long? bankAccountId,
        [FromBody] SaveEmployeeBankAccountRequestDto request, CancellationToken ct) =>
        Result(await _service.SaveBankAccountAsync(bankAccountId, request, ct));

    [HttpGet("leaves")]
    public async Task<IActionResult> Leaves([FromQuery] HrLeaveQueryDto request, CancellationToken ct) =>
        Result(await _service.GetEmployeeLeavesAsync(request, ct));

    [HttpPost("leaves/review")]
    public async Task<IActionResult> Review([FromBody] ReviewEmployeeLeaveDto request, CancellationToken ct) =>
        Result(await _service.ReviewEmployeeLeaveAsync(request, ct));

    private IActionResult Result<T>(EduOS.Core.Common.ApiResponse<T> response) =>
        StatusCode(response.StatusCode, response);
}
