using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/account/login-activity")]
[Authorize]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LoginActivityApiController(ILoginActivityService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] bool? success = null, [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null, CancellationToken cancellationToken = default)
    {
        var result = await service.GetMyHistoryAsync(page, pageSize, success, cancellationToken, from, to);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] bool? success = null,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var result = await service.ExportMyHistoryCsvAsync(success, cancellationToken, from, to);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store";
        return File(Encoding.UTF8.GetBytes(result.Data), "text/csv; charset=utf-8",
            "sign-in-activity-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".csv");
    }
}
