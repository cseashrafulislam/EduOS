using EduOS.App.Authorization;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/hr/employee-contracts")]
[Authorize(Roles = "TenantAdmin"), RequireModule("HR")]
[AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EmployeeContractsApiController : ControllerBase
{
    private readonly IEmployeeContractService service;
    public EmployeeContractsApiController(IEmployeeContractService service) => this.service = service;
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid employeeReference,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15, CancellationToken ct = default)
    {
        var r = await service.ListAsync(employeeReference, page, pageSize, ct);
        return StatusCode(r.StatusCode, r);
    }
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeContractRequestDto request, CancellationToken ct)
    {
        var r = await service.CreateAsync(request, ct);
        return StatusCode(r.StatusCode, r);
    }
    [HttpPost("{reference:guid}/approve")]
    public async Task<IActionResult> Approve(Guid reference,
        [FromBody] ReviewEmployeeContractRequestDto request, CancellationToken ct)
    {
        var r = await service.ReviewAsync(reference, request, true, ct);
        return StatusCode(r.StatusCode, r);
    }
    [HttpPost("{reference:guid}/reject")]
    public async Task<IActionResult> Reject(Guid reference,
        [FromBody] ReviewEmployeeContractRequestDto request, CancellationToken ct)
    {
        var r = await service.ReviewAsync(reference, request, false, ct);
        return StatusCode(r.StatusCode, r);
    }
    [HttpPost("{reference:guid}/signed-pdf")]
    public async Task<IActionResult> AttachSignedPdf(Guid reference,
        [FromBody] AttachSignedEmployeeContractPdfRequestDto request, CancellationToken ct)
    {
        var result = await service.AttachSignedPdfAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{reference:guid}/signed-pdf/approve")]
    public async Task<IActionResult> ApproveSignedPdf(Guid reference,
        [FromBody] VerifySignedEmployeeContractPdfRequestDto request, CancellationToken ct)
    {
        var result = await service.VerifySignedPdfAsync(reference, request, true, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{reference:guid}/signed-pdf/reject")]
    public async Task<IActionResult> RejectSignedPdf(Guid reference,
        [FromBody] VerifySignedEmployeeContractPdfRequestDto request, CancellationToken ct)
    {
        var result = await service.VerifySignedPdfAsync(reference, request, false, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpGet("{reference:guid}/signed-pdf/download")]
    public async Task<IActionResult> DownloadSignedPdf(Guid reference,
        [FromQuery] bool reviewPreview = false, CancellationToken ct = default)
    {
        var result = await service.DownloadSignedPdfAsync(reference, reviewPreview, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        return File(result.Data.Content, "application/pdf", result.Data.FileName);
    }
}
