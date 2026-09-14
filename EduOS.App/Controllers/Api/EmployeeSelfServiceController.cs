using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "Teacher,Staff")]
[ApiController]
[Route("api/employee-portal")]
public sealed class EmployeeSelfServiceController : ControllerBase
{
    private readonly IEmployeeSelfServiceService _service;

    public EmployeeSelfServiceController(IEmployeeSelfServiceService service) => _service = service;

    [HttpGet("profile")]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        var result = await _service.GetProfileAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
