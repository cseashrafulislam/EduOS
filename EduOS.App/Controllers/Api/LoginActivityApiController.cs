using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
        [FromQuery] bool? success = null, CancellationToken cancellationToken = default)
    {
        var result = await service.GetMyHistoryAsync(page, pageSize, success, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
