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

    [HttpGet("circulation")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> Circulation([FromQuery] string? search = null,
        [FromQuery] string? status = "issued", [FromQuery] long? campusId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        ToAction(await _service.GetCirculationRegisterAsync(search, status, campusId, page, pageSize, ct));

    [HttpGet("my-issues")]
    public async Task<IActionResult> MyIssues([FromQuery]int page=1,[FromQuery]int pageSize=20,CancellationToken ct=default)=>ToAction(await _service.GetMyIssuesAsync(page,pageSize,ct));

    [HttpPost("issues")]
    [Authorize(Roles="TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> Issue([FromBody]IssueBookRequestDto request,CancellationToken ct)=>ToAction(await _service.IssueAsync(request,ct));

    [HttpPost("issues/{reference:guid}/close")]
    [Authorize(Roles="TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> Close(Guid reference,[FromBody]ReturnBookRequestDto request,CancellationToken ct)=>ToAction(await _service.CloseAsync(reference,request,ct));

    private IActionResult ToAction<T>(EduOS.Core.Common.ApiResponse<T> response)=>StatusCode(response.StatusCode,response);
}