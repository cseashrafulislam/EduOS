using EduOS.App.Authorization;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/library")]
[Authorize]
[RequireModule("LIBRARY")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LibraryController : ControllerBase
{
    private readonly ILibraryService _service;
    public LibraryController(ILibraryService service)=>_service=service;

    [HttpGet("categories")]
    public async Task<IActionResult> Categories(CancellationToken ct) =>
        ToAction(await _service.GetCategoriesAsync(ct));

    [HttpPost("categories")]
    [HttpPut("categories/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> SaveCategory(long? id, [FromBody] SaveBookCategoryRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return ToAction(await _service.SaveCategoryAsync(id, request, ct));
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog([FromQuery]string? search,[FromQuery]int page=1,[FromQuery]int pageSize=20,CancellationToken ct=default)=>ToAction(await _service.GetCatalogAsync(search,page,pageSize,ct));

    [HttpGet("copy-branches")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> CopyBranches([FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        ToAction(await _service.GetAssignableBranchesAsync(search, page, pageSize, ct));

    [HttpGet("books/{reference:guid}/copies")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> Copies(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        ToAction(await _service.GetBookCopiesAsync(reference, page, pageSize, ct));

    [HttpGet("books/{reference:guid}/available-copies")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> AvailableCopies(Guid reference, [FromQuery] Guid studentReference,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        ToAction(await _service.GetAvailableCopiesForStudentAsync(reference, studentReference, page, pageSize, ct));

    [HttpPost("copies")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> CreateCopy([FromBody] SaveBookCopyRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return ToAction(await _service.SaveBookCopyAsync(null, request, ct));
    }

    [HttpPut("copies/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> UpdateCopy(long id, [FromBody] SaveBookCopyRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return ToAction(await _service.SaveBookCopyAsync(id, request, ct));
    }

    [HttpPost("books")]
    [Authorize(Roles="TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> SaveBook([FromBody]SaveBookRequestDto request,CancellationToken ct)=>ToAction(await _service.SaveBookAsync(request,ct));

    [HttpDelete("books/{reference:guid}")]
    [Authorize(Roles="TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> ArchiveBook(Guid reference,[FromQuery]string rowVersion,CancellationToken ct)=>ToAction(await _service.ArchiveBookAsync(reference,rowVersion,ct));

    [HttpGet("reports/copy-integrity")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> CopyIntegrity([FromQuery] string? accession = null,
        [FromQuery] long? campusId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        ToAction(await _service.GetCopyIntegrityAsync(accession, campusId, page, pageSize, ct));

    [HttpGet("reports/copy-integrity/export")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> ExportCopyIntegrity([FromQuery] string? accession = null,
        [FromQuery] long? campusId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] string language = "en-BD",
        CancellationToken ct = default)
    {
        if (language is not ("en-BD" or "bn-BD"))
            return BadRequest("Unsupported report language.");
        var result = await _service.GetCopyIntegrityAsync(accession, campusId, page, pageSize, ct);
        if (!result.Success || result.Data == null) return ToAction(result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        return File(LibraryCopyIntegrityCsvWriter.Write(result.Data.Items, language == "bn-BD"),
            "text/csv; charset=utf-8", "library-copy-integrity-page.csv");
    }

    [HttpGet("reports/overdue")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> OverdueReport([FromQuery] string? search = null,
        [FromQuery] long? campusId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        ToAction(await _service.GetOverdueBranchesAsync(search, campusId, page, pageSize, ct));

    [HttpGet("reports/overdue/export")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> ExportOverdueReport([FromQuery] string? search = null,
        [FromQuery] long? campusId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] string language = "en-BD",
        CancellationToken ct = default)
    {
        if (language is not ("en-BD" or "bn-BD"))
            return BadRequest("Unsupported report language.");
        var result = await _service.GetOverdueBranchesAsync(search, campusId, page, pageSize, ct);
        if (!result.Success || result.Data == null) return ToAction(result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        return File(LibraryOverdueCsvWriter.Write(result.Data.Items, language == "bn-BD"),
            "text/csv; charset=utf-8", "library-overdue-page.csv");
    }

    [HttpGet("circulation")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> Circulation([FromQuery] string? search = null,
        [FromQuery] string? status = "issued", [FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        ToAction(await _service.GetCirculationRegisterAsync(search, status, campusId, page, pageSize, ct));

    // Fresh tenant, campus and role checks on every export; no browser-held borrower rows.
    [HttpGet("circulation/export")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> ExportCirculation(
        [FromQuery] string? search = null, [FromQuery] string? status = "issued",
        [FromQuery] long? campusId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] string language = "en-BD",
        CancellationToken ct = default)
    {
        if (language is not ("en-BD" or "bn-BD"))
            return BadRequest("Unsupported report language.");
        var result = await _service.GetCirculationRegisterAsync(search, status, campusId, page, pageSize, ct);
        if (!result.Success || result.Data == null) return ToAction(result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        return File(LibraryCirculationCsvWriter.Write(result.Data.Items, language == "bn-BD"),
            "text/csv; charset=utf-8", "library-circulation-page.csv");
    }

    [HttpGet("my-issues")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> MyIssues([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null, CancellationToken ct = default) =>
        ToAction(await _service.GetMyIssuesAsync(page, pageSize, ct, status));

    // Re-query on every download; only the authenticated student's own tenant loans.
    [HttpGet("my-issues/export")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> ExportMyIssues([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] string? status = null,
        [FromQuery] string language = "en-BD", CancellationToken ct = default)
    {
        if (language is not ("en-BD" or "bn-BD"))
            return BadRequest("Unsupported report language.");
        var result = await _service.GetMyIssuesAsync(page, pageSize, ct, status);
        if (!result.Success || result.Data == null) return ToAction(result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        return File(LibraryMyLoanCsvWriter.Write(result.Data.Items, language == "bn-BD"),
            "text/csv; charset=utf-8", "my-library-loans-page.csv");
    }

    [HttpPost("issues")]
    [Authorize(Roles="TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> Issue([FromBody]IssueBookRequestDto request,CancellationToken ct)=>ToAction(await _service.IssueAsync(request,ct));

    [HttpPost("issues/{reference:guid}/renew")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> Renew(Guid reference, [FromBody] RenewBookRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return ToAction(await _service.RenewAsync(reference, request, ct));
    }

    [HttpGet("issues/{reference:guid}/renewals")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> Renewals(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        ToAction(await _service.GetRenewalHistoryAsync(reference, page, pageSize, ct));

    [HttpPost("issues/{reference:guid}/close")]
    [Authorize(Roles="TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> Close(Guid reference,[FromBody]ReturnBookRequestDto request,CancellationToken ct)=>ToAction(await _service.CloseAsync(reference,request,ct));

    private IActionResult ToAction<T>(EduOS.Core.Common.ApiResponse<T> response)=>StatusCode(response.StatusCode,response);
}

// Self-service exports never contain global Person IDs or another borrower's identity.
public static class LibraryMyLoanCsvWriter
{
    public static byte[] Write(IEnumerable<BookIssueDto> rows, bool bangla)
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine(bangla
            ? "বই,অ্যাকসেশন,ইস্যুর তারিখ,ফেরতের শেষ তারিখ,ফেরতের তারিখ,অবস্থা,নথিভুক্ত জরিমানা"
            : "Book,Accession,Issued,Due,Returned,Status,Recorded fine");
        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(",", new[] {
                Cell(row.BookTitle), Cell(row.AccessionNumber),
                Cell(row.IssueDate.ToString("yyyy-MM-dd")),
                Cell(row.DueDate.ToString("yyyy-MM-dd")),
                Cell(row.ReturnDate?.ToString("yyyy-MM-dd") ?? ""),
                Cell(row.State.ToString()),
                Cell(row.FineAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
            }));
        }
        var bytes = new System.Text.UTF8Encoding(false).GetBytes(csv.ToString());
        var preamble = new System.Text.UTF8Encoding(true).GetPreamble();
        var result = new byte[preamble.Length + bytes.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(bytes, 0, result, preamble.Length, bytes.Length);
        return result;
    }

    private static string Cell(string? value)
    {
        var text = value ?? string.Empty;
        var trimmed = text.TrimStart(' ', '\t', '\r', '\n');
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
