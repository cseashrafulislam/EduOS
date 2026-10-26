using EduOS.App.Authorization;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
namespace EduOS.App.Controllers.Api;
[ApiController]
[Route("api/library/reservations")]
[Authorize]
[RequireModule("LIBRARY")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LibraryReservationsController : ControllerBase
{
    private readonly ILibraryReservationService _service;
    public LibraryReservationsController(ILibraryReservationService implementation) => _service = implementation;

    [HttpGet("eligible-students")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> EligibleStudents([FromQuery] string search, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Reply(await _service.SearchEligibleStudentsAsync(search, page, pageSize, ct));

    [HttpGet("mine")]
    public async Task<IActionResult> Mine([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => Reply(await _service.GetMyReservationsAsync(page, pageSize, ct));

    [HttpGet("books/{bookReference:guid}/queue")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> Queue(Guid bookReference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Reply(await _service.GetActiveReservationsForBookAsync(bookReference, page, pageSize, ct));

    [HttpPost]
    public async Task<IActionResult> Reserve([FromBody] ReserveBookRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Reply(await _service.ReserveBookAsync(request, ct));
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, [FromBody] ReservationVersionRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Reply(await _service.CancelReservationAsync(id, request.RowVersion, ct));
    }

    [HttpPost("{id:long}/fulfill")]
    [Authorize(Roles = "TenantAdmin,Principal,Librarian")]
    public async Task<IActionResult> Fulfill(long id, [FromBody] FulfillReservationRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Reply(await _service.FulfillReservationAsync(id, request.BookCopyId, request.RowVersion, ct));
    }
    private IActionResult Reply<T>(EduOS.Core.Common.ApiResponse<T> result) => StatusCode(result.StatusCode, result);
    public sealed class ReservationVersionRequest
    {
        [System.ComponentModel.DataAnnotations.Required] public string RowVersion { get; set; } = "";
    }
    public sealed class FulfillReservationRequest
    {
        [System.ComponentModel.DataAnnotations.Range(1, long.MaxValue)] public long BookCopyId { get; set; }
        [System.ComponentModel.DataAnnotations.Required] public string RowVersion { get; set; } = "";
    }
}
