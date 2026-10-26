using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/finance/accounting/journals"),
 Authorize(Roles = "TenantAdmin,Principal,Accountant"), RequireModule("FINANCE"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
public sealed class AccountingJournalsController : ControllerBase
{
    private readonly IAccountingPostingService service;
    public AccountingJournalsController(IAccountingPostingService implementation) => service = implementation;
    private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Reply(await service.GetJournalsAsync(fromDate, toDate, page, pageSize, ct));

    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Detail(Guid reference, CancellationToken ct)
        => Reply(await service.GetJournalAsync(reference, ct));

    [HttpPost, Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> Create([FromBody] CreateJournalRequestDto request, CancellationToken ct)
        => Reply(await service.CreateJournalAsync(request, ct));

    [HttpPost("{reference:guid}/submit"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> Submit(Guid reference, [FromBody] SubmitJournalRequestDto request, CancellationToken ct)
        => Reply(await service.SubmitJournalAsync(reference, request, ct));

    [HttpPost("{reference:guid}/review"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> Review(Guid reference, [FromBody] ReviewJournalRequestDto request, CancellationToken ct)
        => Reply(await service.ReviewJournalAsync(reference, request, ct));

    [HttpPost("{reference:guid}/post"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> Post(Guid reference, [FromBody] PostJournalRequestDto request, CancellationToken ct)
        => Reply(await service.PostJournalAsync(reference, request, ct));

    [HttpPost("{reference:guid}/reverse"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> Reverse(Guid reference, [FromBody] ReverseJournalRequestDto request, CancellationToken ct)
        => Reply(await service.ReverseJournalAsync(reference, request, ct));
}
