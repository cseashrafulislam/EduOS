using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Facilities;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace EduOS.Service.Services.Facilities;

/// <summary>
/// Privacy-minimized internal visitor appointments. Never stores national IDs,
/// photos, phones or documents. This first slice does not implement incidents,
/// retention deletion or visitor exports and must not be represented as complete.
/// </summary>
public sealed class FacilityVisitorService(
    EduOSDbContext db, ICurrentUserService user, TimeProvider clock,
    ILogger<FacilityVisitorService> logger) : IFacilityVisitorService
{
    private bool Admin => user.IsTenantAdmin || user.IsInRole("Principal");
    private bool Manager => Admin || user.IsInRole("FacilitiesManager");
    private bool ValidTenant => user.IsAuthenticated && user.UserId > 0 && user.TenantId > 0 &&
                                db.CurrentTenantId == user.TenantId;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Visitor access denied.", 403);
    private IQueryable<long> Campuses() => db.Campuses.AsNoTracking()
        .Where(c => c.TenantId == user.TenantId && c.IsActive && !c.IsDeleted &&
            (Admin || db.UserCampusAccesses.Any(g => g.TenantId == user.TenantId &&
                g.UserId == user.UserId && g.CampusId == c.Id && g.IsActive && !g.IsDeleted)))
        .Select(c => c.Id);
    private IQueryable<long> ActiveHosts(long campusId) => db.Users.AsNoTracking()
        .Where(u => u.IsActive && !u.IsDeleted &&
            db.TenantMemberships.Any(m => m.TenantId == user.TenantId && m.UserId == u.Id &&
                !m.IsDeleted && m.Status == MembershipStatus.Active) &&
            db.UserCampusAccesses.Any(g => g.TenantId == user.TenantId && g.UserId == u.Id &&
                g.CampusId == campusId && g.IsActive && !g.IsDeleted) &&
            db.UserRoles.Any(ur => ur.UserId == u.Id &&
                db.Roles.IgnoreQueryFilters().Any(role => role.Id == ur.RoleId &&
                    role.IsActive && !role.IsDeleted &&
                    (role.TenantId == user.TenantId || (role.TenantId == null && role.IsSystemRole)) &&
                    (role.NormalizedName == "STAFF" || role.NormalizedName == "TEACHER" ||
                     role.NormalizedName == "PRINCIPAL" || role.NormalizedName == "TENANTADMIN" ||
                     role.NormalizedName == "FACILITIESMANAGER"))))
        .Select(u => u.Id);
    private async Task<bool> ActorActiveAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking().AnyAsync(u => u.Id == user.UserId && u.IsActive && !u.IsDeleted, ct) &&
        await db.TenantMemberships.AsNoTracking().AnyAsync(m =>
            m.TenantId == user.TenantId && m.UserId == user.UserId &&
            !m.IsDeleted && m.Status == MembershipStatus.Active, ct);
    // Cookie role claims alone cannot authorize a revoked Facilities desk role.
    private async Task<bool> ManagerLiveAsync(CancellationToken ct) =>
        Manager && await db.UserRoles.AnyAsync(ur => ur.UserId == user.UserId &&
            db.Roles.IgnoreQueryFilters().Any(role => role.Id == ur.RoleId &&
                role.IsActive && !role.IsDeleted &&
                (role.TenantId == user.TenantId || (role.TenantId == null && role.IsSystemRole)) &&
                (role.NormalizedName == "TENANTADMIN" || role.NormalizedName == "PRINCIPAL" ||
                 role.NormalizedName == "FACILITIESMANAGER")), ct);
    private IQueryable<FacilityVisitorVisit> Visible() =>
        db.Set<FacilityVisitorVisit>().AsNoTracking().Where(v =>
            v.TenantId == user.TenantId && !v.IsDeleted &&
            Campuses().Contains(v.CampusId) && (Manager || v.HostUserId == user.UserId));
    private static FacilityVisitorDto Map(FacilityVisitorVisit v) => new()
    {
        Id = v.Id, CampusId = v.CampusId, HostUserId = v.HostUserId,
        VisitorName = v.VisitorName, Purpose = v.Purpose,
        VisitStartUtc = v.VisitStartUtc, VisitEndUtc = v.VisitEndUtc,
        StateCode = v.StateCode, BadgeCode = v.BadgeCode,
        CheckedInAtUtc = v.CheckedInAtUtc, CheckedOutAtUtc = v.CheckedOutAtUtc,
        RowVersion = Convert.ToBase64String(v.RowVersion)
    };
    private static bool Version(byte[] version, string? supplied)
    {
        try { return version.AsSpan().SequenceEqual(Convert.FromBase64String(supplied ?? "")); }
        catch (FormatException) { return false; }
    }
    private static bool Plain(string value) => value.All(c => !char.IsControl(c));

    public async Task<ApiResponse<PagedResult<FacilityVisitorHostDto>>> HostsAsync(
        long campusId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!ValidTenant || !Manager || !await ActorActiveAsync(ct) || !await ManagerLiveAsync(ct))
            return Denied<PagedResult<FacilityVisitorHostDto>>();
        if (campusId <= 0 || page is < 1 or > 100000 || pageSize is < 1 or > 50 || search?.Length > 100)
            return ApiResponse<PagedResult<FacilityVisitorHostDto>>.ErrorResponse("Invalid host lookup.", 400);
        if (!await Campuses().ContainsAsync(campusId, ct)) return Denied<PagedResult<FacilityVisitorHostDto>>();
        var query = db.Users.AsNoTracking().Where(u => ActiveHosts(campusId).Contains(u.Id) && u.Id != user.UserId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u => u.FullName.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(u => u.FullName).ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new FacilityVisitorHostDto { UserId = u.Id, DisplayName = u.FullName })
            .ToListAsync(ct);
        return ApiResponse<PagedResult<FacilityVisitorHostDto>>.SuccessResponse(new()
        { Page = page, PageSize = pageSize, TotalCount = total, Items = items });
    }

    public async Task<ApiResponse<PagedResult<FacilityVisitorDto>>> ListAsync(
        long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!ValidTenant || !await ActorActiveAsync(ct) || (Manager && !await ManagerLiveAsync(ct)))
            return Denied<PagedResult<FacilityVisitorDto>>();
        if (campusId is <= 0 || page is < 1 or > 100000 || pageSize is < 1 or > 50)
            return ApiResponse<PagedResult<FacilityVisitorDto>>.ErrorResponse("Invalid visitor filter.", 400);
        if (campusId.HasValue && !await Campuses().ContainsAsync(campusId.Value, ct))
            return Denied<PagedResult<FacilityVisitorDto>>();
        var query = Visible();
        if (campusId.HasValue) query = query.Where(v => v.CampusId == campusId.Value);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<FacilityVisitorDto>>.SuccessResponse(new()
        { Page = page, PageSize = pageSize, TotalCount = total, Items = rows.Select(Map).ToList() });
    }

    public async Task<ApiResponse<FacilityVisitorDto>> CreateAsync(
        CreateFacilityVisitorDto input, CancellationToken ct = default)
    {
        if (!ValidTenant || !Manager || !await ActorActiveAsync(ct) || !await ManagerLiveAsync(ct)) return Denied<FacilityVisitorDto>();
        if (input == null || input.ClientRequestId == Guid.Empty || input.CampusId <= 0 ||
            input.HostUserId <= 0 || input.HostUserId == user.UserId)
            return ApiResponse<FacilityVisitorDto>.ErrorResponse("Valid campus, independent host and command ID required.", 400);
        var name = (input.VisitorName ?? "").Trim();
        var purpose = (input.Purpose ?? "").Trim();
        var now = Now;
        if (name.Length is < 2 or > 100 || purpose.Length is < 3 or > 200 ||
            !Plain(name) || !Plain(purpose) ||
            input.VisitStartUtc.Kind != DateTimeKind.Utc || input.VisitEndUtc.Kind != DateTimeKind.Utc ||
            input.VisitStartUtc <= now || input.VisitEndUtc <= input.VisitStartUtc ||
            input.VisitEndUtc - input.VisitStartUtc > TimeSpan.FromDays(7))
            return ApiResponse<FacilityVisitorDto>.ErrorResponse("Valid minimal visitor details and future UTC visit window required.", 400);
        if (!await Campuses().ContainsAsync(input.CampusId, ct) ||
            !await ActiveHosts(input.CampusId).ContainsAsync(input.HostUserId, ct))
            return Denied<FacilityVisitorDto>();
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            var prior = await db.Set<FacilityVisitorVisit>().AsNoTracking().FirstOrDefaultAsync(
                v => v.TenantId == user.TenantId && v.ClientRequestId == input.ClientRequestId, ct);
            if (prior != null)
                return prior.CreatedBy == user.UserId && prior.CampusId == input.CampusId &&
                       prior.HostUserId == input.HostUserId && prior.VisitorName == name &&
                       prior.Purpose == purpose && prior.VisitStartUtc == input.VisitStartUtc &&
                       prior.VisitEndUtc == input.VisitEndUtc
                    ? ApiResponse<FacilityVisitorDto>.SuccessResponse(Map(prior), "Already preregistered.")
                    : ApiResponse<FacilityVisitorDto>.ErrorResponse("Visitor command ID reused.", 409);
            var visit = new FacilityVisitorVisit
            {
                TenantId = user.TenantId, CampusId = input.CampusId, HostUserId = input.HostUserId,
                ClientRequestId = input.ClientRequestId, VisitorName = name, Purpose = purpose,
                VisitStartUtc = input.VisitStartUtc, VisitEndUtc = input.VisitEndUtc,
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.Set<FacilityVisitorVisit>().Add(visit);
            await db.SaveChangesAsync(ct);
            db.Set<FacilityVisitorEvent>().Add(new()
            {
                TenantId = user.TenantId, FacilityVisitorVisitId = visit.Id,
                ClientCommandId = input.ClientRequestId, ActorUserId = user.UserId,
                ActionCode = "preregister", FromState = "None", ToState = "PendingHost",
                CreatedAt = now, CreatedBy = user.UserId
            });
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            var result = ApiResponse<FacilityVisitorDto>.SuccessResponse(Map(visit), "Visitor preregistered.");
            result.StatusCode = 201;
            return result;
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Visitor preregistration conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<FacilityVisitorDto>.ErrorResponse("Duplicate visitor command or concurrent change.", 409);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222)
        {
            logger.LogWarning(ex, "Visitor preregistration lock contention");
            return ApiResponse<FacilityVisitorDto>.ErrorResponse("Visitor booking busy; retry.", 409);
        }
    }

    public async Task<ApiResponse<FacilityVisitorDto>> TransitionAsync(
        long id, string action, FacilityVisitorTransitionDto command, CancellationToken ct = default)
    {
        if (!ValidTenant || !await ActorActiveAsync(ct) || (Manager && !await ManagerLiveAsync(ct))) return Denied<FacilityVisitorDto>();
        action = (action ?? "").Trim().ToLowerInvariant();
        if (id <= 0 || command == null || command.ClientCommandId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.RowVersion) ||
            action is not ("hostapprove" or "hostreject" or "checkin" or "checkout"))
            return ApiResponse<FacilityVisitorDto>.ErrorResponse("Invalid visitor transition.", 400);
        var badge = command.BadgeCode?.Trim().ToUpperInvariant();
        var reason = command.Reason?.Trim();
        if (reason?.Length > 200 || (reason != null && !Plain(reason)) ||
            (action != "checkin" && !string.IsNullOrEmpty(badge)) ||
            (action == "checkin" && (badge == null || badge.Length is < 4 or > 20 ||
                !badge.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))))
            return ApiResponse<FacilityVisitorDto>.ErrorResponse("Invalid badge or reason.", 400);
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            var visit = await db.Set<FacilityVisitorVisit>().FirstOrDefaultAsync(v =>
                v.TenantId == user.TenantId && v.Id == id && !v.IsDeleted, ct);
            if (visit == null) return ApiResponse<FacilityVisitorDto>.ErrorResponse("Visitor appointment not found.", 404);
            if (!await Campuses().ContainsAsync(visit.CampusId, ct)) return Denied<FacilityVisitorDto>();
            var hostAction = action is "hostapprove" or "hostreject";
            if (hostAction
                ? (visit.HostUserId != user.UserId || visit.CreatedBy == user.UserId ||
                   !await ActiveHosts(visit.CampusId).ContainsAsync(user.UserId, ct))
                : !Manager)
                return Denied<FacilityVisitorDto>();
            var prior = await db.Set<FacilityVisitorEvent>().AsNoTracking().FirstOrDefaultAsync(e =>
                e.TenantId == user.TenantId && e.ClientCommandId == command.ClientCommandId, ct);
            if (prior != null)
            {
                var latest = await db.Set<FacilityVisitorEvent>().AsNoTracking()
                    .Where(e => e.TenantId == user.TenantId && e.FacilityVisitorVisitId == id)
                    .OrderByDescending(e => e.Id).Select(e => e.Id).FirstOrDefaultAsync(ct);
                return prior.FacilityVisitorVisitId == id && prior.ActorUserId == user.UserId &&
                       prior.ActionCode == action && prior.BadgeCode == badge &&
                       prior.Reason == reason && prior.Id == latest
                    ? ApiResponse<FacilityVisitorDto>.SuccessResponse(Map(visit), "Already applied.")
                    : ApiResponse<FacilityVisitorDto>.ErrorResponse("Visitor command reused or superseded.", 409);
            }
            if (!Version(visit.RowVersion, command.RowVersion))
                return ApiResponse<FacilityVisitorDto>.ErrorResponse("Visitor appointment changed; reload.", 409);
            var now = Now;
            var before = visit.StateCode;
            if (hostAction)
            {
                if (visit.StateCode != "PendingHost" || now >= visit.VisitEndUtc ||
                    (action == "hostreject" && (reason?.Length ?? 0) < 10))
                    return ApiResponse<FacilityVisitorDto>.ErrorResponse("Host decision unavailable or reason required.", 409);
                visit.StateCode = action == "hostapprove" ? "Approved" : "Rejected";
                if (action == "hostapprove")
                {
                    visit.ApprovedByUserId = user.UserId;
                    visit.ApprovedAtUtc = now;
                }
            }
            else if (action == "checkin")
            {
                if (visit.StateCode != "Approved" || visit.ApprovedByUserId != visit.HostUserId ||
                    now < visit.VisitStartUtc.AddMinutes(-30) || now >= visit.VisitEndUtc ||
                    !await ActiveHosts(visit.CampusId).ContainsAsync(visit.HostUserId, ct))
                    return ApiResponse<FacilityVisitorDto>.ErrorResponse("Approved active host and visit window required.", 409);
                if (await db.Set<FacilityVisitorVisit>().AnyAsync(v =>
                    v.TenantId == user.TenantId && v.CampusId == visit.CampusId &&
                    v.Id != visit.Id && v.StateCode == "CheckedIn" && v.BadgeCode == badge && !v.IsDeleted, ct))
                    return ApiResponse<FacilityVisitorDto>.ErrorResponse("Badge is already checked in.", 409);
                visit.BadgeCode = badge;
                visit.CheckedInAtUtc = now;
                visit.StateCode = "CheckedIn";
            }
            else
            {
                if (visit.StateCode != "CheckedIn" || visit.CheckedInAtUtc == null)
                    return ApiResponse<FacilityVisitorDto>.ErrorResponse("Visitor is not checked in.", 409);
                visit.CheckedOutAtUtc = now;
                visit.StateCode = "CheckedOut";
            }
            visit.UpdatedAt = now; visit.UpdatedBy = user.UserId;
            db.Set<FacilityVisitorEvent>().Add(new()
            {
                TenantId = user.TenantId, FacilityVisitorVisitId = visit.Id,
                ClientCommandId = command.ClientCommandId, ActorUserId = user.UserId,
                ActionCode = action, FromState = before, ToState = visit.StateCode,
                BadgeCode = action == "checkin" ? badge : null, Reason = reason,
                CreatedAt = now, CreatedBy = user.UserId
            });
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<FacilityVisitorDto>.SuccessResponse(Map(visit), "Visitor transition recorded.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<FacilityVisitorDto>.ErrorResponse("Visitor appointment changed; reload.", 409);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Visitor transition constraint conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<FacilityVisitorDto>.ErrorResponse("Concurrent visitor transition or badge conflict.", 409);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222)
        {
            logger.LogWarning(ex, "Visitor transition lock contention");
            return ApiResponse<FacilityVisitorDto>.ErrorResponse("Visitor appointment busy; retry.", 409);
        }
    }

    public async Task<ApiResponse<PagedResult<FacilityVisitorEventDto>>> HistoryAsync(
        long id, int page, int pageSize, CancellationToken ct = default)
    {
        if (!ValidTenant || !await ActorActiveAsync(ct) || (Manager && !await ManagerLiveAsync(ct)))
            return Denied<PagedResult<FacilityVisitorEventDto>>();
        if (id <= 0 || page is < 1 or > 100000 || pageSize is < 1 or > 50)
            return ApiResponse<PagedResult<FacilityVisitorEventDto>>.ErrorResponse("Invalid history page.", 400);
        if (!await Visible().AnyAsync(v => v.Id == id, ct))
            return ApiResponse<PagedResult<FacilityVisitorEventDto>>.ErrorResponse("Visitor appointment not found.", 404);
        var query = db.Set<FacilityVisitorEvent>().AsNoTracking()
            .Where(e => e.TenantId == user.TenantId && e.FacilityVisitorVisitId == id && !e.IsDeleted);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(e => e.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new FacilityVisitorEventDto
            {
                ActionCode = e.ActionCode, FromState = e.FromState, ToState = e.ToState,
                ActorUserId = e.ActorUserId, OccurredAtUtc = e.CreatedAt
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<FacilityVisitorEventDto>>.SuccessResponse(new()
        { Page = page, PageSize = pageSize, TotalCount = count, Items = rows });
    }
}
