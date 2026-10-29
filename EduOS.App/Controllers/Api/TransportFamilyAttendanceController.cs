using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/transport/family-attendance")]
[Authorize(Roles = "Student,Guardian,Parent")]
[RequireModule("TRANSPORT")]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransportFamilyAttendanceController : ControllerBase
{
    private readonly ITransportTripService _service;
    public TransportFamilyAttendanceController(ITransportTripService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DateOnly date,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _service.GetFamilyAttendanceAsync(date, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }
}
