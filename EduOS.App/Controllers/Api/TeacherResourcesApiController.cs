using EduOS.App.Authorization;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/lms/teacher-resources")]
[Authorize(Roles = "TenantAdmin,Teacher")]
[RequireModule("LMS")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherResourcesApiController(ITeacherResourceService service) : ControllerBase
{
    [HttpGet("public")]
    public async Task<IActionResult> PublicCatalogue([FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await service.ListPublicAsync(search, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("options")]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        var result = await service.OptionsAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search = null,
        [FromQuery] string? state = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await service.ListAsync(search, state, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    [Authorize(Roles = "Teacher")]
    [RequestSizeLimit(11534336)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload([FromForm] TeacherResourceUploadRequestDto request,
        [FromForm] IFormFile file, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (file == null || file.Length is < 1 or > 10485760)
            return BadRequest("A private resource file of at most 10 MB is required.");
        await using var stream = file.OpenReadStream();
        var upload = new PrivateFileUploadDto
        {
            FileName = file.FileName, ContentType = file.ContentType,
            Content = stream, Length = file.Length
        };
        var result = await service.UploadAsync(request, upload, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/submit")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> Submit(Guid reference,
        [FromBody] TeacherResourceTransitionRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.SubmitAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/review")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Review(Guid reference,
        [FromBody] TeacherResourceTransitionRequestDto request,
        [FromQuery] bool approve, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.ReviewAsync(reference, request, approve, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/withdraw")]
    public async Task<IActionResult> Withdraw(Guid reference,
        [FromBody] TeacherResourceTransitionRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.WithdrawAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/download")]
    public async Task<IActionResult> Download(Guid reference, CancellationToken ct)
    {
        var result = await service.DownloadAsync(reference, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }
}
