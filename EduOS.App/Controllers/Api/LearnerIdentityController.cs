using EduOS.Core.DTOs.Student;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,AdmissionOfficer")]
[AutoValidateAntiforgeryToken]
[ApiController]
[Route("api/learner-identities")]
public class LearnerIdentityController : ControllerBase
{
    private readonly ILearnerIdentityService _service;

    public LearnerIdentityController(ILearnerIdentityService service)
    {
        _service = service;
    }

    /// <summary>
    /// Creates an identity for an institution student, reuses the student's own
    /// existing identity, or creates a consent request without returning matched PII.
    /// </summary>
    [HttpPost("register-or-request")]
    [EnableRateLimiting("LearnerIdentityPolicy")]
    public async Task<IActionResult> RegisterOrRequest(
        [FromBody] RegisterLearnerIdentityRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "Invalid learner identity request." });

        var result = await _service.RegisterOrRequestAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
