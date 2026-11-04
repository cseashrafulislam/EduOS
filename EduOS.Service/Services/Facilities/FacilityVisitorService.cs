using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Facilities;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Globalization;

namespace EduOS.Service.Services.Facilities;

/// <summary>
/// Privacy-minimized internal visitor appointments. Never stores national IDs,
/// photos, phones or documents. Incident events are append-only; retention
/// independent two-person, retention-gated redaction and separately approved campus privacy policy changes.
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
    // A revoked host role must not retain visitor PII read access via old cookies.
    private Task<bool> HostLiveAsync(CancellationToken ct) =>
        db.UserRoles.AnyAsync(ur => ur.UserId == user.UserId &&
            db.Roles.IgnoreQueryFilters().Any(role => role.Id == ur.RoleId &&
                role.IsActive && !role.IsDeleted &&
                (role.TenantId == user.TenantId || (role.TenantId == null && role.IsSystemRole)) &&
                (role.NormalizedName == "STAFF" || role.NormalizedName == "TEACHER" ||
                 role.NormalizedName == "PRINCIPAL" || role.NormalizedName == "TENANTADMIN" ||
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
        RowVersion = Convert.ToBase64String(v.RowVersion),
        PrivacyRedacted = v.VisitorName == RedactedVisitor
    };
    private static bool Version(byte[] version, string? supplied)
    {
        try { return version.AsSpan().SequenceEqual(Convert.FromBase64String(supplied ?? "")); }
        catch (FormatException) { return false; }
    }
    private static bool Plain(string value) => value.All(c => !char.IsControl(c));
    private const string RedactedVisitor = "[redacted]";
    private const string RedactionRequestReason = "Visitor retention period expired";
    private const string RedactionApprovalReason = "Independent retention approval";

    // Redaction is manual, never a timer or tenant-wide cleanup. An explicit
    // campus retention policy is mandatory; unconfigured/malformed/legal-hold
    // settings fail closed. Active visits and any incident evidence stay intact.
    private async Task<bool> CanRedactAsync(FacilityVisitorVisit visit, CancellationToken ct)
    {
        if (visit.StateCode is not ("Rejected" or "CheckedOut") ||
            visit.VisitorName == RedactedVisitor) return false;
        var suffix = visit.CampusId.ToString(CultureInfo.InvariantCulture);
        var retention = await db.TenantSettings.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId &&
                x.Key == "Facilities.Visitor.RetentionDays." + suffix && !x.IsDeleted)
            .Select(x => new { x.Value, x.IsSensitive }).SingleOrDefaultAsync(ct);
        if (retention == null || retention.IsSensitive ||
            !int.TryParse(retention.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) ||
            days is < 30 or > 3650 || visit.VisitEndUtc > Now.AddDays(-days))
            return false;
        var hold = await db.TenantSettings.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId &&
                x.Key == "Facilities.Visitor.LegalHold." + suffix && !x.IsDeleted)
            .Select(x => new { x.Value, x.IsSensitive }).SingleOrDefaultAsync(ct);
        if (hold != null && (hold.IsSensitive || !bool.TryParse(hold.Value, out var isHeld) || isHeld))
            return false;
        // The latest incident disposition is authoritative. New incidents after
        // closure reopen the case. Pending/resolved-unapproved requests still block.
        var incident = await LastIncidentDispositionAsync(visit.Id, ct);
        if (incident != null && incident.ActionCode != "incidentclose") return false;
        // A visit-specific legal hold is independent of the campus-wide setting.
        // Pending hold/release always blocks; only reject or approved release clears it.
        var holdEvent = await LastHoldDispositionAsync(visit.Id, ct);
        return holdEvent == null || holdEvent.ActionCode is "holdreject" or "holdrelease";
    }

    private Task<FacilityVisitorEvent?> LastIncidentDispositionAsync(long visitId, CancellationToken ct) =>
        db.Set<FacilityVisitorEvent>().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.FacilityVisitorVisitId == visitId &&
                !x.IsDeleted && (x.ActionCode == "incident" ||
                 x.ActionCode == "incidentresolve" || x.ActionCode == "incidentclose"))
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);

    private Task<FacilityVisitorEvent?> LastHoldDispositionAsync(long visitId, CancellationToken ct) =>
        db.Set<FacilityVisitorEvent>().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.FacilityVisitorVisitId == visitId &&
                !x.IsDeleted && (x.ActionCode == "holdrequest" ||
                x.ActionCode == "holdapprove" || x.ActionCode == "holdreject" ||
                x.ActionCode == "holdreleaserequest" || x.ActionCode == "holdrelease"))
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);

    private Task<bool> ReviewerLiveAsync(CancellationToken ct) =>
        db.UserRoles.AnyAsync(ur => ur.UserId == user.UserId &&
            db.Roles.IgnoreQueryFilters().Any(role => role.Id == ur.RoleId &&
                role.IsActive && !role.IsDeleted &&
                (role.TenantId == user.TenantId ||
                 (role.TenantId == null && role.IsSystemRole)) &&
                (role.NormalizedName == "TENANTADMIN" ||
                 role.NormalizedName == "PRINCIPAL")), ct);

    // Optional, tenant-owned campus occupancy ceiling uses the existing versioned
    // TenantSetting schema; no duplicate policy or stock ledger is introduced.
    // Missing settings preserve existing installations; malformed/sensitive values
    // fail closed rather than silently lifting an explicitly configured ceiling.
    private const string OccupancyKeyPrefix = "Facilities.Visitor.MaxConcurrent.";
    private async Task<(bool Valid, int? MaxConcurrent)> OccupancyLimitAsync(
        long campusId, CancellationToken ct)
    {
        var key = OccupancyKeyPrefix + campusId.ToString(CultureInfo.InvariantCulture);
        var setting = await db.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == user.TenantId && s.Key == key && !s.IsDeleted)
            .Select(s => new { s.Value, s.IsSensitive }).SingleOrDefaultAsync(ct);
        if (setting == null) return (true, null);
        if (setting.IsSensitive ||
            !int.TryParse(setting.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) ||
            limit is < 1 or > 5000)
            return (false, null);
        return (true, limit);
    }


    // Campus-scoped time policy: missing values retain historical defaults.
    // Invalid/sensitive settings fail closed inside the serializable command.
    // Independent policy-change approval remains outstanding.
    private async Task<(bool Valid, int Minutes)> VisitMinutesPolicyAsync(
        string policy, long campusId, int fallback, int minimum, int maximum, CancellationToken ct)
    {
        var key = "Facilities.Visitor." + policy + "." +
            campusId.ToString(CultureInfo.InvariantCulture);
        var setting = await db.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == user.TenantId && s.Key == key && !s.IsDeleted)
            .Select(s => new { s.Value, s.IsSensitive }).SingleOrDefaultAsync(ct);
        if (setting == null) return (true, fallback);
        if (setting.IsSensitive ||
            !int.TryParse(setting.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            minutes < minimum || minutes > maximum)
            return (false, 0);
        return (true, minutes);
    }

    // Hold canonical identity and campus authorization locks through the caller's
    // SERIALIZABLE visit/event/audit transaction. Revoked cookies are insufficient.
    private async Task<bool> LockLiveVisitorIdentityAsync(
        long actorId, long campusId, bool desk, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer())
            return desk
                ? actorId == user.UserId && await ActorActiveAsync(ct) &&
                  await ManagerLiveAsync(ct) && await Campuses().ContainsAsync(campusId, ct)
                : await ActiveHosts(campusId).ContainsAsync(actorId, ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                IF NOT EXISTS (
                    SELECT 1
                    FROM dbo.AspNetUsers AS u WITH (UPDLOCK, HOLDLOCK)
                    INNER JOIN dbo.TenantMemberships AS m WITH (UPDLOCK, HOLDLOCK)
                        ON m.UserId = u.Id AND m.TenantId = {user.TenantId}
                    INNER JOIN dbo.AspNetUserRoles AS ur WITH (UPDLOCK, HOLDLOCK)
                        ON ur.UserId = u.Id
                    INNER JOIN dbo.AspNetRoles AS r WITH (UPDLOCK, HOLDLOCK)
                        ON r.Id = ur.RoleId
                    WHERE u.Id = {actorId} AND u.IsActive = 1 AND u.IsDeleted = 0
                        AND m.IsDeleted = 0 AND m.Status = {(int)MembershipStatus.Active}
                        AND r.IsActive = 1 AND r.IsDeleted = 0
                        AND (r.TenantId = {user.TenantId} OR
                             (r.TenantId IS NULL AND r.IsSystemRole = 1))
                        AND (({desk} = 1 AND r.NormalizedName IN
                                  (N'TENANTADMIN',N'PRINCIPAL',N'FACILITIESMANAGER'))
                             OR ({desk} = 0 AND r.NormalizedName IN
                                  (N'STAFF',N'TEACHER',N'PRINCIPAL',N'TENANTADMIN',N'FACILITIESMANAGER')))
                        AND ((r.NormalizedName IN (N'TENANTADMIN',N'PRINCIPAL') AND {desk} = 1)
                             OR EXISTS (
                                 SELECT 1 FROM dbo.UserCampusAccesses AS g WITH (UPDLOCK, HOLDLOCK)
                                 WHERE g.UserId = u.Id AND g.TenantId = {user.TenantId}
                                     AND g.CampusId = {campusId} AND g.IsActive = 1 AND g.IsDeleted = 0
                             ))
                ) THROW 51003, 'Visitor actor or campus authorization revoked', 1;
                """, ct);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 51003 or 1205 or 1222)
        {
            logger.LogWarning(ex, "Visitor live authorization conflict for tenant {TenantId}", user.TenantId);
            return false;
        }
        return desk
            ? actorId == user.UserId && await ActorActiveAsync(ct) &&
              await ManagerLiveAsync(ct) && await Campuses().ContainsAsync(campusId, ct)
            : await ActiveHosts(campusId).ContainsAsync(actorId, ct);
    }

    public async Task<ApiResponse<PagedResult<FacilityVisitorHostDto>>> HostsAsync(
        long campusId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!ValidTenant || !Manager || !await ActorActiveAsync(ct) || !await ManagerLiveAsync(ct))
            return Denied<PagedResult<FacilityVisitorHostDto>>();
        if (campusId <= 0 || page is < 1 or > 100000 || pageSize is < 1 or > 50 || search?.Length > 100)
            return ApiResponse<PagedResult<FacilityVisitorHostDto>>.ErrorResponse("Invalid host lookup.", 400);
        if (!await Campuses().ContainsAsync(campusId, ct)) return Denied<PagedResult<FacilityVisitorHostDto>>();
        try
        {
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            if (!await LockLiveVisitorIdentityAsync(user.UserId, campusId, true, ct))
                return Denied<PagedResult<FacilityVisitorHostDto>>();
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
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<PagedResult<FacilityVisitorHostDto>>.SuccessResponse(new()
            { Page = page, PageSize = pageSize, TotalCount = total, Items = items });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222)
        {
            logger.LogWarning(ex, "Visitor host directory read contention");
            return ApiResponse<PagedResult<FacilityVisitorHostDto>>.ErrorResponse("Visitor host directory busy; retry.", 409);
        }
    }

    public async Task<ApiResponse<PagedResult<FacilityVisitorDto>>> ListAsync(
        long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!ValidTenant || !await ActorActiveAsync(ct) ||
            (Manager ? !await ManagerLiveAsync(ct) : !await HostLiveAsync(ct)))
            return Denied<PagedResult<FacilityVisitorDto>>();
        if (campusId is <= 0 || page is < 1 or > 100000 || pageSize is < 1 or > 50)
            return ApiResponse<PagedResult<FacilityVisitorDto>>.ErrorResponse("Invalid visitor filter.", 400);
        if (campusId.HasValue && !await Campuses().ContainsAsync(campusId.Value, ct))
            return Denied<PagedResult<FacilityVisitorDto>>();
        try
        {
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            // Lock a live campus before count, and all campuses on the bounded page.
            var firstCampus = campusId ?? await Campuses().OrderBy(x => x).FirstOrDefaultAsync(ct);
            if (firstCampus != 0 &&
                !await LockLiveVisitorIdentityAsync(user.UserId, firstCampus, Manager, ct))
                return Denied<PagedResult<FacilityVisitorDto>>();
        var query = Visible();
        if (campusId.HasValue) query = query.Where(v => v.CampusId == campusId.Value);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            foreach (var pageCampus in rows.Select(v => v.CampusId).Distinct())
                if (!await LockLiveVisitorIdentityAsync(user.UserId, pageCampus, Manager, ct))
                    return Denied<PagedResult<FacilityVisitorDto>>();
            // One bounded query for the page, never a per-visitor event lookup.
            var items = rows.Select(Map).ToList();
            if (items.Count > 0 && Manager)
            {
                // Case dispositions, legal holds and redaction approvals are
                // private desk information: never expose them to a visiting host.
                var ids = items.Select(x => x.Id).ToArray();
                // SQL computes one latest event ID per visit and case category.
                // A decades-old appointment may have thousands of history events;
                // never load all of them into the list process.
                var candidates = db.Set<FacilityVisitorEvent>().AsNoTracking()
                    .Where(x => x.TenantId == user.TenantId &&
                        ids.Contains(x.FacilityVisitorVisitId) && !x.IsDeleted &&
                        (x.ActionCode == "redactrequest" || x.ActionCode == "redactapprove" ||
                         x.ActionCode == "incident" || x.ActionCode == "incidentresolve" ||
                         x.ActionCode == "incidentclose" || x.ActionCode == "holdrequest" ||
                         x.ActionCode == "holdapprove" || x.ActionCode == "holdreject" ||
                         x.ActionCode == "holdreleaserequest" || x.ActionCode == "holdrelease"))
                    .Select(x => new
                    {
                        x.Id, x.FacilityVisitorVisitId,
                        Bucket = x.ActionCode == "redactrequest" || x.ActionCode == "redactapprove" ? 1 :
                            x.ActionCode == "incident" || x.ActionCode == "incidentresolve" ||
                            x.ActionCode == "incidentclose" ? 2 : 3
                    });
                var latestIds = await candidates.GroupBy(x =>
                        new { x.FacilityVisitorVisitId, x.Bucket })
                    .Select(g => g.Max(x => x.Id)).ToListAsync(ct);
                var dispositions = await db.Set<FacilityVisitorEvent>().AsNoTracking()
                    .Where(x => x.TenantId == user.TenantId && latestIds.Contains(x.Id) && !x.IsDeleted)
                    .OrderByDescending(x => x.Id)
                    .Select(x => new { x.FacilityVisitorVisitId, x.ActionCode }).ToListAsync(ct);
                var latestByVisit = dispositions.GroupBy(x => x.FacilityVisitorVisitId)
                    .ToDictionary(x => x.Key, x => x.ToList());
                foreach (var item in items)
                {
                    if (!latestByVisit.TryGetValue(item.Id, out var actions)) continue;
                    var privacy = actions.FirstOrDefault(x => x.ActionCode is "redactrequest" or "redactapprove");
                    item.RedactionRequested = privacy?.ActionCode == "redactrequest" && !item.PrivacyRedacted;
                    var incident = actions.FirstOrDefault(x =>
                        x.ActionCode is "incident" or "incidentresolve" or "incidentclose");
                    item.IncidentStateCode = incident?.ActionCode switch
                    {
                        "incident" => "Open",
                        "incidentresolve" => "PendingReview",
                        "incidentclose" => "Closed",
                        _ => "None"
                    };
                    var hold = actions.FirstOrDefault(x =>
                        x.ActionCode is "holdrequest" or "holdapprove" or "holdreject" or
                        "holdreleaserequest" or "holdrelease");
                    item.LegalHoldStateCode = hold?.ActionCode switch
                    {
                        "holdrequest" => "Pending",
                        "holdapprove" => "Active",
                        "holdreleaserequest" => "ReleasePending",
                        "holdreject" or "holdrelease" => "Clear",
                        _ => "None"
                    };
                }
            }
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<PagedResult<FacilityVisitorDto>>.SuccessResponse(new()
            { Page = page, PageSize = pageSize, TotalCount = total, Items = items });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222)
        {
            logger.LogWarning(ex, "Visitor list read contention");
            return ApiResponse<PagedResult<FacilityVisitorDto>>.ErrorResponse("Visitor list busy; retry.", 409);
        }
    }

    /// <summary>PII-free, manager-only campus occupancy read under a consistent SQL snapshot.</summary>
    public async Task<ApiResponse<FacilityVisitorOccupancyDto>> OccupancyAsync(
        long campusId, CancellationToken ct = default)
    {
        if (!ValidTenant || !Manager || !await ActorActiveAsync(ct) || !await ManagerLiveAsync(ct))
            return Denied<FacilityVisitorOccupancyDto>();
        if (campusId <= 0 || !await Campuses().ContainsAsync(campusId, ct))
            return Denied<FacilityVisitorOccupancyDto>();
        try
        {
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            if (!await ActorActiveAsync(ct) || !await ManagerLiveAsync(ct) ||
                !await Campuses().ContainsAsync(campusId, ct))
                return Denied<FacilityVisitorOccupancyDto>();
            if (!await LockLiveVisitorIdentityAsync(user.UserId, campusId, true, ct))
                return Denied<FacilityVisitorOccupancyDto>();
            var policy = await OccupancyLimitAsync(campusId, ct);
            if (!policy.Valid)
                return ApiResponse<FacilityVisitorOccupancyDto>.ErrorResponse(
                    "Invalid campus visitor occupancy policy; contact an administrator.", 409);
            var snapshotAt = Now;
            var active = db.Set<FacilityVisitorVisit>().AsNoTracking().Where(v =>
                v.TenantId == user.TenantId && v.CampusId == campusId &&
                v.StateCode == "CheckedIn" && !v.IsDeleted);
            var count = await active.CountAsync(ct);
            // A warning only: never silently check out an overdue visitor.
            var overdue = await active.CountAsync(v => v.VisitEndUtc < snapshotAt, ct);
            var snapshot = new FacilityVisitorOccupancyDto
            {
                CampusId = campusId, CheckedInCount = count, OverdueCount = overdue,
                MaxConcurrent = policy.MaxConcurrent, AsOfUtc = snapshotAt
            };
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<FacilityVisitorOccupancyDto>.SuccessResponse(snapshot);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222)
        {
            logger.LogWarning(ex, "Visitor occupancy snapshot contention");
            return ApiResponse<FacilityVisitorOccupancyDto>.ErrorResponse(
                "Visitor occupancy busy; retry.", 409);
        }
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
            input.VisitEndUtc <= input.VisitStartUtc ||
            input.VisitEndUtc - input.VisitStartUtc > TimeSpan.FromDays(7))
            return ApiResponse<FacilityVisitorDto>.ErrorResponse("Valid minimal visitor details and future UTC visit window required.", 400);
        if (!await Campuses().ContainsAsync(input.CampusId, ct) ||
            !await ActiveHosts(input.CampusId).ContainsAsync(input.HostUserId, ct))
            return Denied<FacilityVisitorDto>();
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            if (!await LockLiveVisitorIdentityAsync(user.UserId, input.CampusId, true, ct) ||
                !await LockLiveVisitorIdentityAsync(input.HostUserId, input.CampusId, false, ct))
                return Denied<FacilityVisitorDto>();
            var prior = await db.Set<FacilityVisitorVisit>().AsNoTracking().FirstOrDefaultAsync(
                v => v.TenantId == user.TenantId && v.ClientRequestId == input.ClientRequestId, ct);
            if (prior != null)
                return prior.CreatedBy == user.UserId && prior.CampusId == input.CampusId &&
                       prior.HostUserId == input.HostUserId && prior.VisitorName == name &&
                       prior.Purpose == purpose && prior.VisitStartUtc == input.VisitStartUtc &&
                       prior.VisitEndUtc == input.VisitEndUtc
                    ? ApiResponse<FacilityVisitorDto>.SuccessResponse(Map(prior), "Already preregistered.")
                    : ApiResponse<FacilityVisitorDto>.ErrorResponse("Visitor command ID reused.", 409);
            // Check immutable replay first: policy changes must not duplicate past commands.
            var maxDuration = await VisitMinutesPolicyAsync(
                "MaxVisitMinutes", input.CampusId, 10080, 15, 10080, ct);
            if (!maxDuration.Valid)
                return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                    "Invalid campus visitor duration policy; contact an administrator.", 409);
            if (input.VisitEndUtc - input.VisitStartUtc > TimeSpan.FromMinutes(maxDuration.Minutes))
                return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                    "Visit exceeds the configured campus duration limit.", 409);
            // Exact source-linked retries remain idempotent even after the original
            // visit window begins. Only a NEW preregistration needs a future start.
            if (input.VisitStartUtc <= Now)
                return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                    "New visitor preregistration needs a future UTC window.", 409);
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
        if (!ValidTenant || !await ActorActiveAsync(ct) ||
            (Manager ? !await ManagerLiveAsync(ct) : !await HostLiveAsync(ct))) return Denied<FacilityVisitorDto>();
        action = (action ?? "").Trim().ToLowerInvariant();
        if (id <= 0 || command == null || command.ClientCommandId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.RowVersion) ||
            action is not ("hostapprove" or "hostreject" or "checkin" or "checkout" or "incident" or "redactrequest" or "redactapprove" or
                "incidentresolve" or "incidentclose" or "holdrequest" or "holdapprove" or "holdreject" or
                "holdreleaserequest" or "holdrelease"))
            return ApiResponse<FacilityVisitorDto>.ErrorResponse("Invalid visitor transition.", 400);
        var badge = command.BadgeCode?.Trim().ToUpperInvariant();
        var reason = command.Reason?.Trim();
        if (reason?.Length > 200 || (reason != null && !Plain(reason)) ||
            (action != "checkin" && !string.IsNullOrEmpty(badge)) ||
            (action == "checkin" && (badge == null || badge.Length is < 4 or > 20 ||
                !badge.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))))
            return ApiResponse<FacilityVisitorDto>.ErrorResponse("Invalid badge or reason.", 400);
        if ((action == "redactrequest" && reason != RedactionRequestReason) ||
            (action == "redactapprove" && reason != RedactionApprovalReason))
            return ApiResponse<FacilityVisitorDto>.ErrorResponse("Use the reviewed privacy reason.", 400);
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            var visit = await db.Set<FacilityVisitorVisit>().FirstOrDefaultAsync(v =>
                v.TenantId == user.TenantId && v.Id == id && !v.IsDeleted, ct);
            if (visit == null) return ApiResponse<FacilityVisitorDto>.ErrorResponse("Visitor appointment not found.", 404);
            if (!await Campuses().ContainsAsync(visit.CampusId, ct)) return Denied<FacilityVisitorDto>();
            var hostAction = action is "hostapprove" or "hostreject";
            if (!await LockLiveVisitorIdentityAsync(user.UserId, visit.CampusId, !hostAction, ct) ||
                (action == "checkin" &&
                 !await LockLiveVisitorIdentityAsync(visit.HostUserId, visit.CampusId, false, ct)))
                return Denied<FacilityVisitorDto>();
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
                var earlyPolicy = await VisitMinutesPolicyAsync(
                    "EarlyCheckInMinutes", visit.CampusId, 30, 0, 60, ct);
                if (!earlyPolicy.Valid)
                    return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                        "Invalid campus visitor early-entry policy; contact an administrator.", 409);
                if (visit.StateCode != "Approved" || visit.ApprovedByUserId != visit.HostUserId ||
                    now < visit.VisitStartUtc.AddMinutes(-earlyPolicy.Minutes) || now >= visit.VisitEndUtc ||
                    !await ActiveHosts(visit.CampusId).ContainsAsync(visit.HostUserId, ct))
                    return ApiResponse<FacilityVisitorDto>.ErrorResponse("Approved active host and visit window required.", 409);
                // Serializable range reads on the campus/state index prevent two
                // concurrent admissions from exceeding the configured ceiling.
                var occupancyPolicy = await OccupancyLimitAsync(visit.CampusId, ct);
                if (!occupancyPolicy.Valid)
                    return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                        "Invalid campus visitor occupancy policy; contact an administrator.", 409);
                if (occupancyPolicy.MaxConcurrent.HasValue &&
                    await db.Set<FacilityVisitorVisit>().CountAsync(v =>
                        v.TenantId == user.TenantId && v.CampusId == visit.CampusId &&
                        v.StateCode == "CheckedIn" && !v.IsDeleted, ct) >= occupancyPolicy.MaxConcurrent.Value)
                    return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                        "Campus visitor occupancy limit reached; check out an existing visit.", 409);
                if (await db.Set<FacilityVisitorVisit>().AnyAsync(v =>
                    v.TenantId == user.TenantId && v.CampusId == visit.CampusId &&
                    v.Id != visit.Id && v.StateCode == "CheckedIn" && v.BadgeCode == badge && !v.IsDeleted, ct))
                    return ApiResponse<FacilityVisitorDto>.ErrorResponse("Badge is already checked in.", 409);
                visit.BadgeCode = badge;
                visit.CheckedInAtUtc = now;
                visit.StateCode = "CheckedIn";
            }
            else if (action is "incidentresolve" or "incidentclose")
            {
                if (visit.StateCode != "CheckedOut" || visit.VisitorName == RedactedVisitor ||
                    reason is not { Length: >= 10 and <= 200 })
                    return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                        "Checked-out visit and reasoned incident disposition required.", 409);
                var latestIncident = await LastIncidentDispositionAsync(id, ct);
                if (action == "incidentresolve")
                {
                    if (latestIncident?.ActionCode != "incident")
                        return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                            "No unresolved incident awaiting a resolution request.", 409);
                }
                else
                {
                    if (!Admin || !await ReviewerLiveAsync(ct))
                        return Denied<FacilityVisitorDto>();
                    if (latestIncident?.ActionCode != "incidentresolve" ||
                        latestIncident.ActorUserId == user.UserId)
                        return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                            "Independent pending incident resolution required.", 409);
                }
            }
            else if (action is "holdrequest" or "holdapprove" or "holdreject" or
                     "holdreleaserequest" or "holdrelease")
            {
                if (visit.VisitorName == RedactedVisitor || reason is not { Length: >= 10 and <= 200 })
                    return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                        "Unredacted visit and recorded legal basis required.", 409);
                var lastHold = await LastHoldDispositionAsync(id, ct);
                if (action == "holdrequest")
                {
                    if (lastHold != null && lastHold.ActionCode is not ("holdreject" or "holdrelease"))
                        return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                            "A visit-specific hold is already pending or active.", 409);
                }
                else if (action == "holdreleaserequest")
                {
                    if (lastHold?.ActionCode != "holdapprove")
                        return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                            "An independently approved active hold is required.", 409);
                }
                else
                {
                    if (!Admin || !await ReviewerLiveAsync(ct))
                        return Denied<FacilityVisitorDto>();
                    var expected = action == "holdrelease" ? "holdreleaserequest" : "holdrequest";
                    if (lastHold?.ActionCode != expected || lastHold.ActorUserId == user.UserId)
                        return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                            "Independent current hold decision required.", 409);
                }
            }
            else if (action is "redactrequest" or "redactapprove")
            {
                if (!await CanRedactAsync(visit, ct))
                    return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                        "Retention period, legal hold or incident policy prevents redaction.", 409);
                var latest = await db.Set<FacilityVisitorEvent>().AsNoTracking()
                    .Where(x => x.TenantId == user.TenantId &&
                        x.FacilityVisitorVisitId == id && !x.IsDeleted)
                    .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
                if (action == "redactrequest")
                {
                    if (latest?.ActionCode is "redactrequest" or "redactapprove")
                        return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                            "Privacy request already pending or approved.", 409);
                }
                else
                {
                    // Actual live principal/TenantAdmin role, not a stale cookie,
                    // and a different checker from the last requesting actor.
                    if (!Admin || !await ReviewerLiveAsync(ct))
                        return Denied<FacilityVisitorDto>();
                    if (latest?.ActionCode != "redactrequest" ||
                        latest.ActorUserId == user.UserId)
                        return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                            "Independent pending privacy request required.", 409);
                    // Preserve immutable action/actor/time/status metadata. Only
                    // privacy payload columns are erased on checked approval.
                    var events = await db.Set<FacilityVisitorEvent>()
                        .Where(x => x.TenantId == user.TenantId &&
                            x.FacilityVisitorVisitId == id && !x.IsDeleted).ToListAsync(ct);
                    foreach (var previous in events)
                    {
                        previous.BadgeCode = null; previous.Reason = null;
                        previous.UpdatedAt = now; previous.UpdatedBy = user.UserId;
                    }
                    // Legacy EF generic audit payloads may predate the visitor
                    // audit-suppression fix. After this exact visit passes
                    // retention/legal-hold/maker-checker guards, scrub ONLY its
                    // private Old/New JSON in the SAME transaction. Keep
                    // action, actor, occurred-at and source IDs as evidence.
                    var eventIds = events.Select(x => x.Id).ToArray();
                    var legacyVisitPayloads = await db.AuditLogs
                        .Where(x => x.TenantId == user.TenantId &&
                            x.EntityName == nameof(FacilityVisitorVisit) &&
                            x.EntityId == visit.Id &&
                            (x.OldValue != null || x.NewValue != null))
                        .ExecuteUpdateAsync(up => up
                            .SetProperty(x => x.OldValue, (string?)null)
                            .SetProperty(x => x.NewValue, (string?)null), ct);
                    var legacyEventPayloads = eventIds.Length == 0 ? 0 :
                        await db.AuditLogs.Where(x => x.TenantId == user.TenantId &&
                            x.EntityName == nameof(FacilityVisitorEvent) &&
                            x.EntityId.HasValue && eventIds.Contains(x.EntityId.Value) &&
                            (x.OldValue != null || x.NewValue != null))
                        .ExecuteUpdateAsync(up => up
                            .SetProperty(x => x.OldValue, (string?)null)
                            .SetProperty(x => x.NewValue, (string?)null), ct);
                    db.AuditLogs.Add(new AuditLog {
                        TenantId = user.TenantId, UserId = user.UserId,
                        Action = "VisitorLegacyAuditPayloadRedacted",
                        EntityName = nameof(FacilityVisitorVisit), EntityId = visit.Id,
                        IsSuccess = true, OldValue = null, NewValue = null,
                        OccurredAt = now, CreatedAt = now, CreatedBy = user.UserId
                    });
                    logger.LogInformation(
                        "Approved visitor audit payload redaction: tenant {TenantId}, visit {VisitId}, legacy visit audit rows {VisitRows}, legacy event audit rows {EventRows}",
                        user.TenantId, visit.Id, legacyVisitPayloads, legacyEventPayloads);
                    visit.VisitorName = RedactedVisitor;
                    visit.Purpose = RedactedVisitor;
                    visit.BadgeCode = null;
                }
            }
            else if (action == "incident")
            {
                // Append-only incident evidence, never a badge or state override.
                if (visit.StateCode is not ("CheckedIn" or "CheckedOut") ||
                    (visit.StateCode == "CheckedOut" &&
                     (visit.CheckedOutAtUtc == null ||
                      now > visit.CheckedOutAtUtc.Value.AddHours(24))) ||
                    (reason?.Length ?? 0) < 10)
                    return ApiResponse<FacilityVisitorDto>.ErrorResponse(
                        "Incident requires a checked-in or recently checked-out visit and a 10-character reason.", 409);
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


    /// <summary>Manager-only consistent 31-day, 500-row operational summary without visitor PII.</summary>
    public async Task<ApiResponse<IReadOnlyList<FacilityVisitorExportRowDto>>> ExportSnapshotAsync(
        long campusId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        if (!ValidTenant || !Manager || !await ActorActiveAsync(ct) || !await ManagerLiveAsync(ct))
            return Denied<IReadOnlyList<FacilityVisitorExportRowDto>>();
        if (campusId <= 0 || fromUtc.Kind != DateTimeKind.Utc || toUtc.Kind != DateTimeKind.Utc ||
            fromUtc >= toUtc || toUtc - fromUtc > TimeSpan.FromDays(31))
            return ApiResponse<IReadOnlyList<FacilityVisitorExportRowDto>>.ErrorResponse(
                "A campus and a valid UTC window of at most 31 days are required.", 400);
        try
        {
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            // Revalidate the live actor and campus within the same export transaction.
            // A revoked session must never receive a previously authorized CSV.
            if (!await ActorActiveAsync(ct) || !await ManagerLiveAsync(ct) ||
                !await Campuses().ContainsAsync(campusId, ct))
                return Denied<IReadOnlyList<FacilityVisitorExportRowDto>>();
            if (!await LockLiveVisitorIdentityAsync(user.UserId, campusId, true, ct))
                return Denied<IReadOnlyList<FacilityVisitorExportRowDto>>();
            var rows = await db.Set<FacilityVisitorVisit>().AsNoTracking()
                .Where(v => v.TenantId == user.TenantId && !v.IsDeleted &&
                    v.CampusId == campusId && v.VisitStartUtc >= fromUtc && v.VisitStartUtc < toUtc)
                .OrderByDescending(v => v.VisitStartUtc).ThenByDescending(v => v.Id)
                .Take(501)
                .Select(v => new FacilityVisitorExportRowDto
                {
                    VisitId = v.Id, CampusId = v.CampusId, StateCode = v.StateCode,
                    VisitStartUtc = v.VisitStartUtc, VisitEndUtc = v.VisitEndUtc,
                    CheckedInAtUtc = v.CheckedInAtUtc, CheckedOutAtUtc = v.CheckedOutAtUtc,
                    IncidentCount = db.Set<FacilityVisitorEvent>().Count(e =>
                        e.TenantId == user.TenantId && !e.IsDeleted &&
                        e.FacilityVisitorVisitId == v.Id && e.ActionCode == "incident")
                }).ToListAsync(ct);
            if (rows.Count > 500)
                return ApiResponse<IReadOnlyList<FacilityVisitorExportRowDto>>.ErrorResponse(
                    "More than 500 visits; narrow the UTC date window.", 413);
            // This is a successful, bounded export snapshot. Persist its access
            // evidence atomically with the read, before any CSV bytes are returned.
            // No visitor PII, badge, host or incident narrative enters this audit.
            var auditAt = Now;
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = user.TenantId,
                UserId = user.UserId,
                Action = "VisitorSummaryExport",
                EntityName = "FacilityVisitorVisit",
                EntityId = campusId,
                Endpoint = "/api/facilities/visitors/export",
                RequestId = Guid.NewGuid().ToString("N"),
                NewValue = $"CampusId={campusId};FromUtc={fromUtc:O};ToUtc={toUtc:O};Rows={rows.Count}",
                IsSuccess = true,
                OccurredAt = auditAt,
                CreatedAt = auditAt,
                CreatedBy = user.UserId
            });
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<IReadOnlyList<FacilityVisitorExportRowDto>>.SuccessResponse(rows);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222)
        {
            logger.LogWarning(ex, "Visitor summary snapshot lock contention for tenant {TenantId}", user.TenantId);
            return ApiResponse<IReadOnlyList<FacilityVisitorExportRowDto>>.ErrorResponse(
                "Visitor summary is busy; retry with the same filters.", 409);
        }
    }

    public async Task<ApiResponse<PagedResult<FacilityVisitorEventDto>>> HistoryAsync(
        long id, int page, int pageSize, CancellationToken ct = default)
    {
        if (!ValidTenant || !await ActorActiveAsync(ct) ||
            (Manager ? !await ManagerLiveAsync(ct) : !await HostLiveAsync(ct)))
            return Denied<PagedResult<FacilityVisitorEventDto>>();
        if (id <= 0 || page is < 1 or > 100000 || pageSize is < 1 or > 50)
            return ApiResponse<PagedResult<FacilityVisitorEventDto>>.ErrorResponse("Invalid history page.", 400);
        try
        {
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var campus = await Visible().Where(v => v.Id == id)
                .Select(v => v.CampusId).FirstOrDefaultAsync(ct);
            if (campus == 0)
                return ApiResponse<PagedResult<FacilityVisitorEventDto>>.ErrorResponse("Visitor appointment not found.", 404);
            if (!await LockLiveVisitorIdentityAsync(user.UserId, campus, Manager, ct))
                return Denied<PagedResult<FacilityVisitorEventDto>>();
            var query = db.Set<FacilityVisitorEvent>().AsNoTracking()
            .Where(e => e.TenantId == user.TenantId && e.FacilityVisitorVisitId == id && !e.IsDeleted);
            // A host can see admission/visit history, but not case investigation,
            // legal-hold or personal-data retention decisions.
            if (!Manager)
                query = query.Where(e => e.ActionCode != "incidentresolve" &&
                    e.ActionCode != "incidentclose" && e.ActionCode != "holdrequest" &&
                    e.ActionCode != "holdapprove" && e.ActionCode != "holdreject" &&
                    e.ActionCode != "holdreleaserequest" && e.ActionCode != "holdrelease" &&
                    e.ActionCode != "redactrequest" && e.ActionCode != "redactapprove");
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(e => e.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new FacilityVisitorEventDto
            {
                ActionCode = e.ActionCode, FromState = e.FromState, ToState = e.ToState,
                ActorUserId = e.ActorUserId, OccurredAtUtc = e.CreatedAt,
                Reason = Manager && (e.ActionCode == "incident" ||
                    e.ActionCode == "incidentresolve" || e.ActionCode == "incidentclose" ||
                    e.ActionCode == "holdrequest" || e.ActionCode == "holdapprove" ||
                    e.ActionCode == "holdreject" || e.ActionCode == "holdreleaserequest" ||
                    e.ActionCode == "holdrelease") ? e.Reason : null
            }).ToListAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<PagedResult<FacilityVisitorEventDto>>.SuccessResponse(new()
            { Page = page, PageSize = pageSize, TotalCount = count, Items = rows });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222)
        {
            logger.LogWarning(ex, "Visitor history read contention");
            return ApiResponse<PagedResult<FacilityVisitorEventDto>>.ErrorResponse("Visitor history busy; retry.", 409);
        }
    }

    private static FacilityVisitorPolicyDto MapPolicy(FacilityVisitorPolicyChange row) => new()
    {
        Id = row.Id, CampusId = row.CampusId, PolicyCode = row.PolicyCode,
        ProposedValue = row.ProposedValue, PreviousValue = row.PreviousValue,
        StateCode = row.StateCode, RequestReason = row.RequestReason,
        RequestedByUserId = row.RequestedByUserId,
        ReviewedByUserId = row.ReviewedByUserId, ReviewedAtUtc = row.ReviewedAtUtc,
        ReviewReason = row.ReviewReason,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    private static string? PrivacyPolicyKey(string code, long campusId) =>
        code switch
        {
            "RetentionDays" => "Facilities.Visitor.RetentionDays." + campusId.ToString(CultureInfo.InvariantCulture),
            "LegalHold" => "Facilities.Visitor.LegalHold." + campusId.ToString(CultureInfo.InvariantCulture),
            _ => null
        };

    private static string? PrivacyPolicyValue(string code, string? value)
    {
        if (code == "RetentionDays" &&
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) &&
            days is >= 30 and <= 3650)
            return days.ToString(CultureInfo.InvariantCulture);
        if (code == "LegalHold" && bool.TryParse(value, out var hold))
            return hold ? "true" : "false";
        return null;
    }

    public async Task<ApiResponse<PagedResult<FacilityVisitorPolicyDto>>> PolicyChangesAsync(
        long campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!ValidTenant || !Manager || !await ActorActiveAsync(ct) || !await ManagerLiveAsync(ct))
            return Denied<PagedResult<FacilityVisitorPolicyDto>>();
        if (campusId <= 0 || page is < 1 or > 100000 || pageSize is < 1 or > 50)
            return ApiResponse<PagedResult<FacilityVisitorPolicyDto>>.ErrorResponse("Invalid policy page.", 400);
        if (!await Campuses().ContainsAsync(campusId, ct))
            return Denied<PagedResult<FacilityVisitorPolicyDto>>();
        var rows = db.Set<FacilityVisitorPolicyChange>().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.CampusId == campusId && !x.IsDeleted);
        var count = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<FacilityVisitorPolicyDto>>.SuccessResponse(new()
        {
            Page = page, PageSize = pageSize, TotalCount = count,
            Items = items.Select(MapPolicy).ToList()
        });
    }

    public async Task<ApiResponse<FacilityVisitorPolicyDto>> RequestPolicyChangeAsync(
        FacilityVisitorPolicyRequestDto request, CancellationToken ct = default)
    {
        if (!ValidTenant || !Manager || !await ActorActiveAsync(ct) || !await ManagerLiveAsync(ct))
            return Denied<FacilityVisitorPolicyDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.CampusId <= 0 ||
            request.Reason == null || request.Reason.Trim().Length is < 10 or > 200 ||
            !Plain(request.Reason))
            return ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse("Valid policy request and reason required.", 400);
        var policy = request.PolicyCode?.Trim() ?? "";
        var key = PrivacyPolicyKey(policy, request.CampusId);
        var proposed = PrivacyPolicyValue(policy, request.ProposedValue?.Trim());
        if (key == null || proposed == null)
            return ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse("Unsupported visitor privacy policy value.", 400);
        try
        {
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            if (!await LockLiveVisitorIdentityAsync(user.UserId, request.CampusId, true, ct))
                return Denied<FacilityVisitorPolicyDto>();
            var replay = await db.Set<FacilityVisitorPolicyChange>().AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == user.TenantId &&
                    x.ClientRequestId == request.ClientRequestId, ct);
            if (replay != null)
                return replay.CampusId == request.CampusId && replay.PolicyCode == policy &&
                       replay.ProposedValue == proposed && replay.RequestReason == request.Reason.Trim()
                    ? ApiResponse<FacilityVisitorPolicyDto>.SuccessResponse(MapPolicy(replay))
                    : ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse("Request identifier was already used.", 409);
            if (await db.Set<FacilityVisitorPolicyChange>().AnyAsync(x =>
                x.TenantId == user.TenantId && x.CampusId == request.CampusId &&
                x.PolicyCode == policy && x.StateCode == "Submitted" && !x.IsDeleted, ct))
                return ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse(
                    "A policy request is already awaiting independent review.", 409);
            var current = await db.TenantSettings.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Key == key && !x.IsDeleted, ct);
            if (current?.IsSensitive == true ||
                (current != null && PrivacyPolicyValue(policy, current.Value) == null))
                return ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse(
                    "Existing policy is invalid; administrative reconciliation required.", 409);
            if (current != null && PrivacyPolicyValue(policy, current.Value) == proposed)
                return ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse("Policy already has the requested value.", 409);
            var row = new FacilityVisitorPolicyChange
            {
                TenantId = user.TenantId, CampusId = request.CampusId,
                ClientRequestId = request.ClientRequestId, PolicyCode = policy,
                ProposedValue = proposed,
                PreviousValue = current?.Value,
                PreviousVersion = current == null ? null : Convert.ToBase64String(current.RowVersion),
                RequestedByUserId = user.UserId, RequestReason = request.Reason.Trim(),
                StateCode = "Submitted", CreatedAt = Now, CreatedBy = user.UserId
            };
            db.Set<FacilityVisitorPolicyChange>().Add(row);
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<FacilityVisitorPolicyDto>.SuccessResponse(MapPolicy(row));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse("Policy request changed; reload.", 409);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Concurrent visitor policy request");
            return ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse("Policy request conflicts with another change.", 409);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222)
        {
            logger.LogWarning(ex, "Visitor policy request contention");
            return ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse("Policy is busy; retry.", 409);
        }
    }

    public async Task<ApiResponse<FacilityVisitorPolicyDto>> DecidePolicyChangeAsync(
        long id, string decision, FacilityVisitorPolicyDecisionDto request, CancellationToken ct = default)
    {
        if (!ValidTenant || !Admin || !await ActorActiveAsync(ct) || !await ReviewerLiveAsync(ct))
            return Denied<FacilityVisitorPolicyDto>();
        if (id <= 0 || decision is not ("approve" or "reject") || request == null ||
            request.ClientCommandId == Guid.Empty || string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.Reason == null || request.Reason.Trim().Length is < 10 or > 200 ||
            !Plain(request.Reason))
            return ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse("Valid independent decision required.", 400);
        try
        {
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var row = await db.Set<FacilityVisitorPolicyChange>().SingleOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == id && !x.IsDeleted, ct);
            if (row == null) return ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse("Policy request not found.", 404);
            if (!await LockLiveVisitorIdentityAsync(user.UserId, row.CampusId, true, ct))
                return Denied<FacilityVisitorPolicyDto>();
            if (row.RequestedByUserId == user.UserId)
                return Denied<FacilityVisitorPolicyDto>();
            if (row.StateCode != "Submitted")
                return row.ReviewCommandId == request.ClientCommandId &&
                       row.ReviewedByUserId == user.UserId &&
                       row.StateCode == (decision == "approve" ? "Approved" : "Rejected") &&
                       row.ReviewReason == request.Reason.Trim()
                    ? ApiResponse<FacilityVisitorPolicyDto>.SuccessResponse(MapPolicy(row))
                    : ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse("Policy was already decided.", 409);
            if (!Version(row.RowVersion, request.RowVersion))
                return ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse("Policy request changed; reload.", 409);
            var key = PrivacyPolicyKey(row.PolicyCode, row.CampusId);
            if (key == null || PrivacyPolicyValue(row.PolicyCode, row.ProposedValue) == null)
                return ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse("Invalid stored policy request.", 409);
            var current = await db.TenantSettings.SingleOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Key == key && !x.IsDeleted, ct);
            var unchanged = current == null
                ? row.PreviousVersion == null && row.PreviousValue == null
                : !current.IsSensitive && current.Value == row.PreviousValue &&
                  Version(current.RowVersion, row.PreviousVersion);
            if (!unchanged)
                return ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse(
                    "Effective policy changed since the request; submit a new request.", 409);
            var decidedAt = Now;
            if (decision == "approve")
            {
                var setting = current ?? new EduOS.Core.Entities.SaaS.TenantSetting
                {
                    TenantId = user.TenantId, Key = key, Category = "Facilities",
                    CreatedAt = decidedAt, CreatedBy = user.UserId
                };
                setting.Value = row.ProposedValue;
                setting.IsSensitive = false;
                if (current == null) db.TenantSettings.Add(setting);
                else { setting.UpdatedAt = decidedAt; setting.UpdatedBy = user.UserId; }
            }
            row.StateCode = decision == "approve" ? "Approved" : "Rejected";
            row.ReviewCommandId = request.ClientCommandId;
            row.ReviewedByUserId = user.UserId;
            row.ReviewedAtUtc = decidedAt;
            row.ReviewReason = request.Reason.Trim();
            row.UpdatedAt = decidedAt;
            row.UpdatedBy = user.UserId;
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = user.TenantId, UserId = user.UserId,
                Action = decision == "approve" ? "VisitorPolicyApproved" : "VisitorPolicyRejected",
                EntityName = nameof(FacilityVisitorPolicyChange), EntityId = row.Id,
                Endpoint = "/api/facilities/visitors/policies",
                RequestId = request.ClientCommandId.ToString("N"),
                // Only campus/policy/value metadata; no visitor identity or incident narratives.
                NewValue = $"CampusId={row.CampusId};Policy={row.PolicyCode};Value={row.ProposedValue}",
                IsSuccess = true, OccurredAt = decidedAt, CreatedAt = decidedAt, CreatedBy = user.UserId
            });
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<FacilityVisitorPolicyDto>.SuccessResponse(MapPolicy(row));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse("Policy changed; reload.", 409);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Concurrent visitor policy decision");
            return ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse("Policy decision conflicts with another change.", 409);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222)
        {
            logger.LogWarning(ex, "Visitor policy decision contention");
            return ApiResponse<FacilityVisitorPolicyDto>.ErrorResponse("Policy is busy; retry.", 409);
        }
    }
    /// <summary>
    /// Restricted, read-only, PII-free verification. Unredacted historical audit
    /// rows remain visible to legal/security review by COUNTS ONLY. No live
    /// records are silently modified, regardless of the result.
    /// </summary>
    public async Task<ApiResponse<FacilityVisitorPrivacyAuditReportDto>> PrivacyAuditReportAsync(
        CancellationToken ct = default)
    {
        if (!ValidTenant || !Admin || !await ReviewerLiveAsync(ct) ||
            !await db.Users.AsNoTracking().AnyAsync(x =>
                x.Id == user.UserId && x.IsActive && !x.IsDeleted, ct) ||
            !await db.TenantMemberships.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.UserId == user.UserId &&
                x.Status == EduOS.Core.Enums.Domain.MembershipStatus.Active &&
                !x.IsDeleted, ct))
            return Denied<FacilityVisitorPrivacyAuditReportDto>();
        var audits = db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId &&
                (x.OldValue != null || x.NewValue != null));
        var visitAudit = audits.Where(x => x.EntityName == nameof(FacilityVisitorVisit));
        var eventAudit = audits.Where(x => x.EntityName == nameof(FacilityVisitorEvent));
        var visits = db.Set<FacilityVisitorVisit>().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
        var total = await visits.LongCountAsync(ct);
        var redacted = await visits.LongCountAsync(x => x.VisitorName == RedactedVisitor, ct);
        var visitPayloadCount = await visitAudit.LongCountAsync(ct);
        var eventPayloadCount = await eventAudit.LongCountAsync(ct);
        var stillExposed = await visits.Where(v => v.VisitorName == RedactedVisitor &&
            (visitAudit.Any(a => a.EntityId == v.Id) ||
             db.Set<FacilityVisitorEvent>().Any(e =>
                e.TenantId == user.TenantId && e.FacilityVisitorVisitId == v.Id &&
                eventAudit.Any(a => a.EntityId == e.Id))))
            .LongCountAsync(ct);
        return ApiResponse<FacilityVisitorPrivacyAuditReportDto>.SuccessResponse(new()
        {
            TenantId = user.TenantId, CheckedAtUtc = Now,
            VisitorVisits = total, FullyRedactedVisits = redacted,
            LegacyVisitAuditPayloadRows = visitPayloadCount,
            LegacyEventAuditPayloadRows = eventPayloadCount,
            FullyRedactedVisitsWithLegacyPayloads = stillExposed
        });
    }

}
