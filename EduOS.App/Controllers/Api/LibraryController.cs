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
public sealed class LibraryController : ControllerBase
{
    private readonly ILibraryService _service;
    public LibraryController(ILibraryService service)=>_service=service;

    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog([FromQuery]string? search,[FromQuery]int page=1,[FromQuery]int pageSize=20,CancellationToken ct=default)=>ToAction(await _service.GetCatalogAsync(search,page,pageSize,ct));

    [HttpPost("books")]
    [Authorize(Roles="TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> SaveBook([FromBody]SaveBookRequestDto request,CancellationToken ct)=>ToAction(await _service.SaveBookAsync(request,ct));

    [HttpDelete("books/{reference:guid}")]
    [Authorize(Roles="TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> ArchiveBook(Guid reference,[FromQuery]string rowVersion,CancellationToken ct)=>ToAction(await _service.ArchiveBookAsync(reference,rowVersion,ct));

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