using EduOS.App.Authorization;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,AdmissionOfficer")]
[RequireModule("STUDENT")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController]
[Route("api/students")]
public sealed class StudentsController : ControllerBase
{
    private readonly IStudentDirectoryService _service;
    public StudentsController(IStudentDirectoryService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetPage([FromQuery] StudentDirectoryQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _service.GetPageAsync(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }


    // Exports only the currently authorized, server-paged directory rows.
    // No guardian, contact, global Person, historical or cross-tenant data.
    [HttpGet("export-page")]
    public async Task<IActionResult> ExportPage([FromQuery] StudentDirectoryQueryDto query,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetPageAsync(query, cancellationToken);
        if (!result.Success || result.Data == null)
            return StatusCode(result.StatusCode, result);
        var csv = EduOS.App.Reporting.StudentDirectoryCsvWriter.Write(result.Data.Items);
        return File(csv, "text/csv; charset=utf-8", $"students-page-{result.Data.Page}.csv");
    }

    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Get(Guid reference, CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(reference, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
