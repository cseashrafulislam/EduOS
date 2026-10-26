using EduOS.App.Authorization;
using EduOS.Core.DTOs.Learners;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("STUDENT")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/learner-identities/students/{studentReference:guid}/addresses")]
public sealed class StudentPersonAddressesController : ControllerBase
{
    private readonly IStudentPersonAddressService _service;
    public StudentPersonAddressesController(IStudentPersonAddressService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get(Guid studentReference, CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(studentReference, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("{addressType}")]
    public async Task<IActionResult> Save(
        Guid studentReference, string addressType, [FromBody] SaveStudentPersonAddressRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "Invalid structured address request." });
        var result = await _service.SaveAsync(studentReference, addressType, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
