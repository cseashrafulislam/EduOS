using EduOS.App.Authorization;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace EduOS.App.Controllers.Api;
[ApiController, Route("api/finance/invoice-posting")]
[Authorize(Roles = "TenantAdmin,Principal,Accountant"), RequireModule("FINANCE")]
[AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StudentInvoicePostingController : ControllerBase
{
    private readonly IStudentInvoicePostingService service;
    public StudentInvoicePostingController(IStudentInvoicePostingService implementation) => service = implementation;
    private IActionResult Reply<T>(EduOS.Core.Common.ApiResponse<T> result) => StatusCode(result.StatusCode, result);
    [HttpGet]
    public async Task<IActionResult> Register([FromQuery] long? campusId, [FromQuery] string? search,
        [FromQuery] bool unpostedOnly = true, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Reply(await service.GetRegisterAsync(campusId, search, unpostedOnly, page, pageSize, ct));
    [HttpPost("{invoiceReference:guid}/post"), Authorize(Roles = "TenantAdmin,Principal")]
    public async Task<IActionResult> Post(Guid invoiceReference,
        [FromBody] PostStudentInvoiceJournalRequestDto request, CancellationToken ct) =>
        Reply(await service.PostAsync(invoiceReference, request, ct));
}
