using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EduOS.AI;
using EduOS.App.Authorization;
using EduOS.Core.Constants;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EduOS.App.Controllers.Api;

public sealed class AttendanceVoicePreviewRequest
{
    [Required, StringLength(512, MinimumLength = 2)]
    public string Question { get; set; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long CampusId { get; set; }
}

/// <summary>
/// Authenticated, deterministic AI/voice read preview. This does not run a report,
/// infer a batch/session, make provider calls or write any business data.
/// </summary>
[ApiController]
[Route("api/ai/voice")]
[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
[RequireModule("ATTENDANCE")]
[RequirePermission(PermissionConstants.Attendance.View)]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AiVoiceAttendanceController(
    EduOSDbContext db, ICurrentUserService current,
    ReviewedCapabilityRegistry registry, TimeProvider clock) : ControllerBase
{
    [HttpGet("campuses")]
    public async Task<IActionResult> Campuses([FromQuery] string? search = null, CancellationToken ct = default)
    {
        if (!HasTrustedTenant()) return Forbid();
        if (search is { Length: > 80 }) return BadRequest(new { message = "Campus search is too long." });

        var query = db.Campuses.AsNoTracking().Where(x =>
            x.TenantId == current.TenantId && x.IsActive && !x.IsDeleted);
        if (!current.IsTenantAdmin)
            query = query.Where(c => db.UserCampusAccesses.AsNoTracking().Any(a =>
                a.TenantId == current.TenantId && a.UserId == current.UserId &&
                a.CampusId == c.Id && a.IsActive && !a.IsDeleted));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Name.StartsWith(term) || x.Code.StartsWith(term));
        }

        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new { id = x.Id, name = x.Name, code = x.Code })
            .Take(51).ToListAsync(ct);
        return Ok(new { campuses = rows.Take(50).ToArray(), hasMore = rows.Count > 50 });
    }

    [HttpPost("attendance/preview")]
    public async Task<IActionResult> PreviewAttendance(
        [FromBody] AttendanceVoicePreviewRequest request, CancellationToken ct)
    {
        if (!HasTrustedTenant()) return Forbid();
        if (!ModelState.IsValid || request is null ||
            string.IsNullOrWhiteSpace(request.Question) || request.CampusId <= 0)
            return BadRequest(new { message = "Provide an attendance question and an authorized campus." });

        var campus = await db.Campuses.AsNoTracking()
            .Where(x => x.TenantId == current.TenantId && x.Id == request.CampusId &&
                x.IsActive && !x.IsDeleted)
            .Select(x => new { x.Id, x.Name, x.TimeZoneId })
            .SingleOrDefaultAsync(ct);
        if (campus is null) return NotFound(new { message = "Campus unavailable." });
        if (!current.IsTenantAdmin && !await db.UserCampusAccesses.AsNoTracking().AnyAsync(x =>
            x.TenantId == current.TenantId && x.UserId == current.UserId &&
            x.CampusId == campus.Id && x.IsActive && !x.IsDeleted, ct))
            return NotFound(new { message = "Campus unavailable." });

        var tenantZone = await db.Tenants.AsNoTracking()
            .Where(x => x.Id == current.TenantId && !x.IsDeleted)
            .Select(x => x.TimeZoneId).SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(tenantZone))
            return StatusCode(503, new { message = "Institution time zone is unavailable." });

        DateOnly localToday;
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(
                string.IsNullOrWhiteSpace(campus.TimeZoneId) ? tenantZone : campus.TimeZoneId);
            localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        }
        catch (TimeZoneNotFoundException)
        {
            return StatusCode(503, new { message = "Institution time zone is unavailable." });
        }
        catch (InvalidTimeZoneException)
        {
            return StatusCode(503, new { message = "Institution time zone is invalid." });
        }

        // The endpoint policy rechecks current membership, module and permission.
        // This scope is constructed from trusted server data; no browser-supplied
        // tenant, user, permission or entitlement value is accepted.
        var scope = new AiRequestScope(
            current.TenantId.ToString(CultureInfo.InvariantCulture),
            campus.Id.ToString(CultureInfo.InvariantCulture),
            current.UserId.ToString(CultureInfo.InvariantCulture),
            new HashSet<string>(StringComparer.Ordinal) { "Attendance.View" }, true);
        var plan = ReviewedAttendanceQueryCompiler.Compile(request.Question, localToday, scope, registry);
        if (plan.Decision == CapabilityDecision.Denied) return Forbid();
        if (plan.Decision != CapabilityDecision.Ready || plan.Query is null)
            return Ok(new { decision = plan.Decision.ToString(), message = plan.Message,
                requiresConfirmation = false, reportExecuted = false });

        return Ok(new
        {
            decision = "Ready",
            capability = plan.CapabilityKey,
            campus = new { id = campus.Id, name = campus.Name },
            date = plan.Query.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            attendanceStatus = plan.Query.Status.ToString(),
            requiresConfirmation = false,
            reportExecuted = false,
            message = "Question reviewed. Choose a specific batch/session in Attendance Reports for the canonical count."
        });
    }

    private bool HasTrustedTenant() =>
        current.IsAuthenticated && current.UserId > 0 && current.TenantId > 0 &&
        db.CurrentTenantId == current.TenantId;
}
