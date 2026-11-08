using EduOS.App.Authorization;
using EduOS.Core.DTOs.Finance;
using EduOS.Service.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/student-scholarships")]
[Authorize]
[RequireModule("STUDENT")]
[RequireModule("FINANCE")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ScholarshipApplicationsController(ScholarshipWorkflowService service,
    ScholarshipDisbursementService disbursements) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(int page = 1, int pageSize = 30,
        string? state = null, CancellationToken ct = default)
    {
        var result = await service.SearchAsync(page, pageSize, state, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Detail(Guid reference, CancellationToken ct)
    {
        var result = await service.GetAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitScholarshipRequest request, CancellationToken ct)
    {
        var result = await service.SubmitAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("evidence-upload")]
    [Consumes("multipart/form-data"), RequestSizeLimit(6291456)]
    public async Task<IActionResult> UploadEvidence([FromForm] Guid enrollmentReference,
        [FromForm] Guid requestId, [FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length is < 1 or > 5242880 || requestId == Guid.Empty)
            return BadRequest(new { success = false, message = "Choose a file up to 5 MB and an upload request identifier." });
        await using var stream = file.OpenReadStream();
        var upload = new EduOS.Core.DTOs.Files.PrivateFileUploadDto
        {
            FileName = file.FileName, ContentType = file.ContentType,
            Length = file.Length, Content = stream
        };
        var result = await service.UploadEvidenceAsync(enrollmentReference, requestId, upload, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{reference:guid}/decision")]
    public async Task<IActionResult> Decide(Guid reference,
        [FromBody] ScholarshipDecisionRequest request, CancellationToken ct)
    {
        var result = await service.DecideAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{reference:guid}/award")]
    public async Task<IActionResult> Award(Guid reference, CancellationToken ct)
    {
        var result = await service.AwardAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{reference:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid reference,
        [FromBody] ScholarshipCommandRequest request, CancellationToken ct)
    {
        var result = await service.CancelAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{reference:guid}/evidence-download")]
    public async Task<IActionResult> DownloadEvidence(Guid reference, CancellationToken ct)
    {
        var result = await service.DownloadEvidenceAsync(reference, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconciliation(int page = 1, int pageSize = 30,
        CancellationToken ct = default)
    {
        var result = await service.ReconciliationAsync(page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/invoice-credits")]
    public async Task<IActionResult> InvoiceCredits(Guid reference, int page = 1,
        int pageSize = 30, CancellationToken ct = default)
    {
        var result = await service.InvoiceCreditsAsync(reference, page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}/cash-disbursement")]
    public async Task<IActionResult> CashDisbursement(Guid reference, CancellationToken ct)
    {
        var result = await disbursements.GetAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpGet("{reference:guid}/cash-disbursement/payees")]
    public async Task<IActionResult> CashPayees(Guid reference, CancellationToken ct)
    {
        var result = await disbursements.PayeesAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{reference:guid}/cash-disbursement")]
    public async Task<IActionResult> PrepareCashDisbursement(Guid reference,
        [FromBody] PrepareScholarshipDisbursementRequest request, CancellationToken ct)
    {
        var result = await disbursements.PrepareAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{reference:guid}/cash-disbursement/review")]
    public async Task<IActionResult> ReviewCashDisbursement(Guid reference,
        [FromBody] ReviewScholarshipDisbursementRequest request, CancellationToken ct)
    {
        var result = await disbursements.ReviewAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{reference:guid}/cash-disbursement/reversal")]
    public async Task<IActionResult> RequestCashReversal(Guid reference,
        [FromBody] ReverseScholarshipDisbursementRequest request, CancellationToken ct)
    {
        var result = await disbursements.RequestReversalAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{reference:guid}/cash-disbursement/reversal/review")]
    public async Task<IActionResult> ReviewCashReversal(Guid reference,
        [FromBody] ReviewScholarshipReversalRequest request, CancellationToken ct)
    {
        var result = await disbursements.ReviewReversalAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
