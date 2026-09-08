using EduOS.App.Authorization;
using EduOS.Core.DTOs.Student;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,Principal")]
[RequireModule("STUDENT")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController]
[Route("api/students/{studentReference:guid}/promotions")]
public sealed class StudentPromotionsController : ControllerBase
{
    private readonly IStudentPromotionService _service;

    public StudentPromotionsController(IStudentPromotionService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetHistory(
        Guid studentReference,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetHistoryAsync(studentReference, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Promote(
        Guid studentReference,
        [FromBody] PromoteStudentRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "Promotion request is invalid." });

        var result = await _service.PromoteAsync(studentReference, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
