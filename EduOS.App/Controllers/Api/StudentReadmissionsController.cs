using EduOS.App.Authorization;
using EduOS.Core.DTOs.Student;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace EduOS.App.Controllers.Api;
[ApiController]
[Route("api/students/{studentReference:guid}/readmission")]
[Authorize(Roles="TenantAdmin")]
[RequireModule("STUDENT")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class StudentReadmissionsController : ControllerBase
{
    private readonly IStudentReadmissionService _service;
    public StudentReadmissionsController(IStudentReadmissionService service)=>_service=service;
    [HttpGet("options")]
    public async Task<IActionResult> Options(Guid studentReference,CancellationToken ct)
    {
        var result=await _service.GetOptionsAsync(studentReference,ct);
        return StatusCode(result.StatusCode,result);
    }
    [HttpPost]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Process(Guid studentReference,[FromBody] ProcessStudentReadmissionDto request,CancellationToken ct)
    {
        if(!ModelState.IsValid)return ValidationProblem(ModelState);
        var result=await _service.ProcessAsync(studentReference,request,ct);
        return StatusCode(result.StatusCode,result);
    }
}
