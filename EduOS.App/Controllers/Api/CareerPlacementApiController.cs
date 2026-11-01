using System.Globalization;
using System.Text;
using EduOS.App.Authorization;
using EduOS.Core.DTOs.Alumni;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Authorize(Roles = "TenantAdmin,Student")]
[RequireModule("ALUMNI")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/alumni/career")]
public sealed class CareerPlacementApiController : ControllerBase
{
    private readonly IAlumniService _service;
    public CareerPlacementApiController(IAlumniService service) => _service = service;

    [HttpGet("opportunities")]
    public async Task<IActionResult> Opportunities([FromQuery] CareerOpportunityQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.OpportunitiesAsync(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("opportunities")]
    [Authorize(Roles = "TenantAdmin")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> CreateOpportunity([FromBody] CareerOpportunityDraftDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.CreateOpportunityAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("opportunities/{reference:guid}/publish")]
    [Authorize(Roles = "TenantAdmin")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Publish(Guid reference, [FromBody] CareerActionDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.ChangeOpportunityAsync(reference, request, 2, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("opportunities/{reference:guid}/close")]
    [Authorize(Roles = "TenantAdmin")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Close(Guid reference, [FromBody] CareerActionDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.ChangeOpportunityAsync(reference, request, 3, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("students/me")]
    public async Task<IActionResult> Students(CancellationToken ct)
    {
        var result = await _service.CareerStudentsAsync(ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("opportunities/{reference:guid}/apply")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Apply(Guid reference, [FromBody] CareerApplyDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.ApplyCareerAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("applications")]
    public async Task<IActionResult> Applications([FromQuery] CareerApplicationQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.CareerApplicationsAsync(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("applications/{reference:guid}/propose")]
    [Authorize(Roles = "TenantAdmin")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Propose(Guid reference, [FromBody] CareerDecisionDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.ChangeApplicationAsync(reference, request, 1, request.Outcome, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("applications/{reference:guid}/approve")]
    [Authorize(Roles = "TenantAdmin")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Approve(Guid reference, [FromBody] CareerActionDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.ChangeApplicationAsync(reference, new CareerActionDtoBase
        {
            ClientRequestId = request.ClientRequestId, RowVersion = request.RowVersion
        }, 2, 0, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("applications/{reference:guid}/withdraw")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Withdraw(Guid reference, [FromBody] CareerActionDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.ChangeApplicationAsync(reference, new CareerActionDtoBase
        {
            ClientRequestId = request.ClientRequestId, RowVersion = request.RowVersion
        }, 3, 0, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("reports")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Reports([FromQuery] CareerOpportunityQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.CareerReportAsync(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("reports.csv")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Export([FromQuery] CareerOpportunityQueryDto query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.CareerReportAsync(query, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        var bn = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "bn";
        var csv = new StringBuilder(bn
            ? "\uFEFFক্যাম্পাস,নিয়োগদাতা,পদ,আবেদন,বিচারাধীন,প্রত্যাখ্যাত,প্লেসমেন্ট,প্রত্যাহার\r\n"
            : "\uFEFFCampus,Employer,Opportunity,Applications,Pending,Rejected,Placed,Withdrawn\r\n");
        foreach (var x in result.Data.Items)
        {
            csv.Append(x.CampusId).Append(',').Append(Csv(x.EmployerName)).Append(',')
                .Append(Csv(x.OpportunityTitle)).Append(',').Append(x.Applications).Append(',')
                .Append(x.Pending).Append(',').Append(x.Rejected).Append(',').Append(x.Placed)
                .Append(',').Append(x.Withdrawn).Append("\r\n");
        }
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            $"career-placement-page-{query.Page}.csv");
    }

    private static string Csv(string? value)
    {
        var text = value ?? "";
        var index = 0;
        while (index < text.Length && (char.IsWhiteSpace(text[index]) || text[index] is '\uFEFF' or '\u200B')) index++;
        if (index < text.Length && text[index] is '=' or '+' or '-' or '@') text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
