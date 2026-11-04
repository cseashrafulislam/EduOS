using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/finance/accounting/budgets"), Authorize(Roles = "TenantAdmin,Principal"),
 RequireModule("FINANCE"), EnableRateLimiting("ApiPolicy"),
 ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InstitutionBudgetsController : ControllerBase
{
    private readonly IInstitutionBudgetService service;
    public InstitutionBudgetsController(IInstitutionBudgetService service) => this.service = service;
    private IActionResult Reply<T>(ApiResponse<T> result) => StatusCode(result.StatusCode, result);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Reply(await service.ListAsync(page, pageSize, ct));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
        => Reply(await service.GetAsync(id, ct));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit([FromBody] SubmitInstitutionBudgetRequestDto request, CancellationToken ct)
        => Reply(await service.SubmitAsync(request, ct));

    [HttpPost("{id:long}/review"), Authorize(Roles = "TenantAdmin"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(long id, [FromBody] ReviewInstitutionBudgetRequestDto request, CancellationToken ct)
        => Reply(await service.ReviewAsync(id, request, ct));

    [HttpGet("{id:long}/actual"), Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Actual(long id, CancellationToken ct)
        => Reply(await service.ActualAsync(id, ct));
}
