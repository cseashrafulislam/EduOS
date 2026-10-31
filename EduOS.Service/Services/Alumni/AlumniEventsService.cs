using System.Data;
using System.Security.Cryptography;
using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Alumni;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Alumni;

public sealed partial class AlumniService
{
    private const string CampusGrantSql = @"
        (NOT EXISTS (SELECT 1 FROM dbo.UserCampusAccesses ca
            WHERE ca.TenantId = {0} AND ca.UserId = {1})
         OR EXISTS (SELECT 1 FROM dbo.UserCampusAccesses ca
            WHERE ca.TenantId = {0} AND ca.UserId = {1}
              AND ca.CampusId = e.CampusId AND ca.IsActive = 1 AND ca.IsDeleted = 0))";

    private const string EventColumns = @"
        SELECT e.PublicId AS Reference, e.CampusId, e.Title, e.Description,
            e.StartsAtUtc, e.EndsAtUtc, CAST(e.Status AS int) AS Status,
            e.RowVersion, e.CreatedByUserId,
            (SELECT COUNT(*) FROM dbo.AlumniEventRsvps r
                WHERE r.TenantId = e.TenantId AND r.EventId = e.Id AND r.Response = 1) AS GoingCount,
            (SELECT COUNT(*) FROM dbo.AlumniEventRsvps r
                WHERE r.TenantId = e.TenantId AND r.EventId = e.Id AND r.Response = 2) AS DeclinedCount,";

    private const string GraduateEventFrom = @"
        FROM dbo.AlumniEvents e
        OUTER APPLY (
            SELECT TOP (1) p.Id AS ProfileId, CAST(r.Response AS int) AS MyResponse,
                r.RowVersion AS MyRowVersion
            FROM dbo.AlumniProfiles p
            INNER JOIN dbo.Students s ON s.TenantId = p.TenantId AND s.Id = p.StudentId
                AND s.UserId = {1} AND s.IsDeleted = 0
            INNER JOIN dbo.StudentExitRecords x ON x.Id = p.StudentExitRecordId
                AND x.TenantId = p.TenantId AND x.StudentId = s.Id
                AND x.ExitType = 2 AND x.FeesCleared = 1 AND x.IsDeleted = 0
            INNER JOIN dbo.StudentEnrollments en ON en.Id = x.StudentEnrollmentId
                AND en.TenantId = p.TenantId AND en.StudentId = s.Id
                AND en.State = 2 AND en.IsDeleted = 0 AND en.CampusId = p.CampusId
            LEFT JOIN dbo.AlumniEventRsvps r ON r.TenantId = p.TenantId
                AND r.AlumniProfileId = p.Id AND r.EventId = e.Id
            WHERE p.TenantId = e.TenantId AND p.CampusId = e.CampusId
                AND p.DirectoryOptIn = 1
            ORDER BY p.Id
        ) mine
        WHERE e.TenantId = {0} AND e.Status = 2 AND mine.ProfileId IS NOT NULL";

    private const string AdminEventFrom = @"
        FROM dbo.AlumniEvents e
        WHERE e.TenantId = {0} AND " + CampusGrantSql;


    public async Task<ApiResponse<IReadOnlyList<AlumniEventCampusDto>>> EventCampusesAsync(CancellationToken ct = default)
    {
        if (!CanRead() || !_user.IsTenantAdmin)
            return ApiResponse<IReadOnlyList<AlumniEventCampusDto>>.ErrorResponse("Alumni event administration required.", 403);
        try
        {
            var campuses = await _db.Database.SqlQueryRaw<AlumniEventCampusDto>(@"
                SELECT e.Id, e.Name FROM dbo.Campuses e
                WHERE e.TenantId = {0} AND e.IsDeleted = 0 AND e.IsActive = 1 AND "
                + CampusGrantSql.Replace("ca.CampusId = e.CampusId", "ca.CampusId = e.Id") + " ORDER BY e.Name, e.Id",
                _user.TenantId, _user.UserId).ToListAsync(ct);
            return ApiResponse<IReadOnlyList<AlumniEventCampusDto>>.SuccessResponse(campuses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alumni event campus list failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<IReadOnlyList<AlumniEventCampusDto>>.ErrorResponse("Campuses could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<AlumniEventItemDto>> CreateEventAsync(
        AlumniEventDraftDto request, CancellationToken ct = default)
    {
        if (!CanRead() || !_user.IsTenantAdmin)
            return ApiResponse<AlumniEventItemDto>.ErrorResponse("Alumni event administration required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.CampusId <= 0 ||
            string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length is < 3 or > 160 ||
            request.Description?.Length > 1500 || request.StartsAtUtc.Kind != DateTimeKind.Utc ||
            request.EndsAtUtc.Kind != DateTimeKind.Utc || request.StartsAtUtc <= DateTime.UtcNow ||
            request.EndsAtUtc <= request.StartsAtUtc ||
            request.EndsAtUtc - request.StartsAtUtc > TimeSpan.FromDays(14))
            return ApiResponse<AlumniEventItemDto>.ErrorResponse("Valid future UTC event details are required.");

        var tenant = _user.TenantId;
        var actor = _user.UserId;
        var title = request.Title.Trim();
        var description = Normalize(request.Description);
        var hash = Hash($"{request.CampusId}|{title}|{description}|{request.StartsAtUtc:O}|{request.EndsAtUtc:O}");
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var existing = await _db.Database.SqlQueryRaw<AlumniEventIdentityRow>(
                    @"SELECT Id, PublicId AS Reference, CampusId, CreatedByUserId, RequestHash,
                        CAST(Status AS int) AS Status, StartsAtUtc, RowVersion
                      FROM dbo.AlumniEvents e WHERE e.TenantId = {0} AND e.CreateRequestId = {1}
                        AND " + CampusGrantSql,
                    tenant, request.ClientRequestId).ToListAsync(token);
                if (existing.Count != 0)
                {
                    if (existing[0].CreatedByUserId != actor || existing[0].RequestHash != hash)
                        return ApiResponse<AlumniEventItemDto>.ErrorResponse("Request reference is already used.", 409);
                    var replay = await ReadEventAsync(tenant, existing[0].Id, null, token);
                    return ApiResponse<AlumniEventItemDto>.SuccessResponse(ToEvent(replay!, true), "Event draft already created.");
                }

                var authorized = await _db.Database.SqlQueryRaw<int>(
                    @"SELECT COUNT(*) AS Value FROM dbo.Campuses e
                      WHERE e.TenantId = {0} AND e.Id = {2} AND e.IsDeleted = 0
                        AND e.IsActive = 1 AND
                        (NOT EXISTS (SELECT 1 FROM dbo.UserCampusAccesses ca
                            WHERE ca.TenantId = {0} AND ca.UserId = {1})
                         OR EXISTS (SELECT 1 FROM dbo.UserCampusAccesses ca
                            WHERE ca.TenantId = {0} AND ca.UserId = {1}
                              AND ca.CampusId = e.Id AND ca.IsActive = 1 AND ca.IsDeleted = 0))",
                    tenant, actor, request.CampusId).ToListAsync(token);
                if (authorized.FirstOrDefault() != 1)
                    return ApiResponse<AlumniEventItemDto>.ErrorResponse("Campus access denied.", 403);

                var now = DateTime.UtcNow;
                var reference = Guid.NewGuid();
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO dbo.AlumniEvents
                    (TenantId, PublicId, CreateRequestId, RequestHash, CampusId, Title,
                     Description, StartsAtUtc, EndsAtUtc, Status, CreatedByUserId,
                     UpdatedByUserId, CreatedAtUtc, UpdatedAtUtc)
                    VALUES ({tenant}, {reference}, {request.ClientRequestId}, {hash},
                        {request.CampusId}, {title}, {description}, {request.StartsAtUtc},
                        {request.EndsAtUtc}, {(byte)1}, {actor}, {actor}, {now}, {now})", token);
                var created = await ReadEventByReferenceAsync(tenant, reference, token);
                if (created == null) throw new InvalidOperationException("Alumni event was not persisted.");
                await AuditEventAsync(tenant, created.Id, "DraftCreated", actor, token);
                return ApiResponse<AlumniEventItemDto>.SuccessResponse(
                    ToEvent(created.Event!, true), "Event draft created; a different administrator must publish it.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205)
        {
            _logger.LogWarning(ex, "Alumni event create conflict for tenant {TenantId}", tenant);
            return ApiResponse<AlumniEventItemDto>.ErrorResponse("Concurrent event request; retry or reload.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alumni event create failed for tenant {TenantId}", tenant);
            return ApiResponse<AlumniEventItemDto>.ErrorResponse("Event draft could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<AlumniEventItemDto>> ChangeEventStatusAsync(
        Guid reference, AlumniEventActionDto request, bool publish, CancellationToken ct = default)
    {
        if (!CanRead() || !_user.IsTenantAdmin)
            return ApiResponse<AlumniEventItemDto>.ErrorResponse("Alumni event administration required.", 403);
        if (reference == Guid.Empty || request == null || !TryRowVersion(request.RowVersion, out var version))
            return ApiResponse<AlumniEventItemDto>.ErrorResponse("Valid event reference and version required.");
        var tenant = _user.TenantId;
        var actor = _user.UserId;
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var rows = await _db.Database.SqlQueryRaw<AlumniEventIdentityRow>(
                    @"SELECT e.Id, e.PublicId AS Reference, e.CampusId, e.CreatedByUserId,
                        e.RequestHash, CAST(e.Status AS int) AS Status, e.StartsAtUtc, e.RowVersion
                      FROM dbo.AlumniEvents e WHERE e.TenantId = {0} AND e.PublicId = {2}
                        AND " + CampusGrantSql, tenant, actor, reference).ToListAsync(token);
                var current = rows.FirstOrDefault();
                if (current == null) return ApiResponse<AlumniEventItemDto>.ErrorResponse("Event not found.", 404);
                if (!CryptographicOperations.FixedTimeEquals(current.RowVersion, version))
                    return ApiResponse<AlumniEventItemDto>.ErrorResponse("Event changed; reload.", 409);
                if (publish && current.CreatedByUserId == actor)
                    return ApiResponse<AlumniEventItemDto>.ErrorResponse("The event creator cannot approve publication.", 403);
                if (publish && (current.Status != 1 || current.StartsAtUtc <= DateTime.UtcNow) ||
                    !publish && current.Status == 3)
                    return ApiResponse<AlumniEventItemDto>.ErrorResponse("Invalid event transition.", 409);
                var target = (byte)(publish ? 2 : 3);
                var now = DateTime.UtcNow;
                var changed = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE dbo.AlumniEvents SET Status = {target}, UpdatedAtUtc = {now},
                        UpdatedByUserId = {actor}
                    WHERE TenantId = {tenant} AND Id = {current.Id} AND RowVersion = {version}", token);
                if (changed != 1)
                    return ApiResponse<AlumniEventItemDto>.ErrorResponse("Event changed; reload.", 409);
                await AuditEventAsync(tenant, current.Id, publish ? "Published" : "Cancelled", actor, token);
                var saved = await ReadEventAsync(tenant, current.Id, null, token);
                return ApiResponse<AlumniEventItemDto>.SuccessResponse(ToEvent(saved!, true));
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 2601 or 2627)
        {
            _logger.LogWarning(ex, "Alumni event status concurrency for tenant {TenantId}", tenant);
            return ApiResponse<AlumniEventItemDto>.ErrorResponse("Concurrent event update; reload.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alumni event status failed for tenant {TenantId}", tenant);
            return ApiResponse<AlumniEventItemDto>.ErrorResponse("Event could not be updated.", 500);
        }
    }

    public async Task<ApiResponse<PagedResult<AlumniEventItemDto>>> EventsAsync(
        AlumniEventQueryDto query, CancellationToken ct = default)
    {
        if (!CanRead()) return ApiResponse<PagedResult<AlumniEventItemDto>>.ErrorResponse("Institution membership required.", 403);
        if (query == null || query.Page < 1 || query.PageSize is < 1 or > 50)
            return ApiResponse<PagedResult<AlumniEventItemDto>>.ErrorResponse("Invalid event page.");
        var tenant = _user.TenantId;
        var actor = _user.UserId;
        var admin = _user.IsTenantAdmin;
        var from = admin ? AdminEventFrom : GraduateEventFrom;
        var mineColumns = admin
            ? " CAST(NULL AS int) AS MyResponse, CAST(NULL AS varbinary(8)) AS MyRowVersion "
            : " mine.MyResponse, mine.MyRowVersion ";
        try
        {
            var total = await _db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value " + from, tenant, actor).ToListAsync(ct);
            var offset = (int)Math.Min((long)(query.Page - 1) * query.PageSize, int.MaxValue);
            var rows = await _db.Database.SqlQueryRaw<AlumniEventSqlRow>(
                EventColumns + mineColumns + from +
                " ORDER BY e.StartsAtUtc DESC, e.Id DESC OFFSET {2} ROWS FETCH NEXT {3} ROWS ONLY",
                tenant, actor, offset, query.PageSize).ToListAsync(ct);
            return ApiResponse<PagedResult<AlumniEventItemDto>>.SuccessResponse(
                new PagedResult<AlumniEventItemDto> {
                    Items = rows.Select(x => ToEvent(x, admin)).ToList(),
                    TotalCount = total.FirstOrDefault(), Page = query.Page, PageSize = query.PageSize
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alumni event list failed for tenant {TenantId}", tenant);
            return ApiResponse<PagedResult<AlumniEventItemDto>>.ErrorResponse("Events could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<AlumniEventItemDto>> RsvpAsync(
        AlumniEventRsvpDto request, CancellationToken ct = default)
    {
        if (!CanRead()) return ApiResponse<AlumniEventItemDto>.ErrorResponse("Institution membership required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.EventReference == Guid.Empty ||
            request.StudentReference == Guid.Empty || request.Response is < 1 or > 2)
            return ApiResponse<AlumniEventItemDto>.ErrorResponse("Valid event, graduate and response required.");
        var tenant = _user.TenantId;
        var actor = _user.UserId;
        var hash = Hash($"{request.EventReference:D}|{request.StudentReference:D}|{request.Response}");
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var eligible = await _db.Database.SqlQueryRaw<AlumniEventEligibilityRow>(@"
                    SELECT TOP (1) e.Id AS EventId, p.Id AS ProfileId
                    FROM dbo.AlumniEvents e
                    INNER JOIN dbo.AlumniProfiles p ON p.TenantId = e.TenantId
                        AND p.CampusId = e.CampusId AND p.DirectoryOptIn = 1
                    INNER JOIN dbo.Students s ON s.TenantId = p.TenantId AND s.Id = p.StudentId
                        AND s.UserId = {1} AND s.PublicId = {2} AND s.IsDeleted = 0
                    INNER JOIN dbo.StudentExitRecords x ON x.TenantId = p.TenantId
                        AND x.Id = p.StudentExitRecordId AND x.StudentId = s.Id
                        AND x.ExitType = 2 AND x.FeesCleared = 1 AND x.IsDeleted = 0
                    INNER JOIN dbo.StudentEnrollments en ON en.TenantId = p.TenantId
                        AND en.Id = x.StudentEnrollmentId AND en.StudentId = s.Id
                        AND en.State = 2 AND en.IsDeleted = 0 AND en.CampusId = p.CampusId
                    WHERE e.TenantId = {0} AND e.PublicId = {3} AND e.Status = 2
                        AND e.StartsAtUtc > {4}", tenant, actor, request.StudentReference,
                    request.EventReference, DateTime.UtcNow).ToListAsync(token);
                var target = eligible.FirstOrDefault();
                if (target == null)
                    return ApiResponse<AlumniEventItemDto>.ErrorResponse("Event unavailable or alumni consent required.", 404);

                var previous = await _db.Database.SqlQueryRaw<AlumniEventCommandRow>(
                    @"SELECT RequestHash, ActorUserId FROM dbo.AlumniEventCommands
                      WHERE TenantId = {0} AND ClientRequestId = {1}",
                    tenant, request.ClientRequestId).ToListAsync(token);
                if (previous.Count != 0)
                {
                    if (previous[0].ActorUserId != actor || previous[0].RequestHash != hash)
                        return ApiResponse<AlumniEventItemDto>.ErrorResponse("Request reference is already used.", 409);
                    var replay = await ReadEventAsync(tenant, target.EventId, target.ProfileId, token);
                    return ApiResponse<AlumniEventItemDto>.SuccessResponse(ToEvent(replay!, false), "RSVP already processed.");
                }

                var existing = await _db.Database.SqlQueryRaw<AlumniRsvpSqlRow>(
                    @"SELECT CAST(Response AS int) AS Response, RowVersion
                      FROM dbo.AlumniEventRsvps WHERE TenantId = {0} AND EventId = {1}
                        AND AlumniProfileId = {2}", tenant, target.EventId, target.ProfileId).ToListAsync(token);
                var now = DateTime.UtcNow;
                if (existing.Count == 0)
                {
                    if (!string.IsNullOrEmpty(request.RowVersion))
                        return ApiResponse<AlumniEventItemDto>.ErrorResponse("RSVP changed; reload.", 409);
                    await _db.Database.ExecuteSqlInterpolatedAsync($@"
                        INSERT INTO dbo.AlumniEventRsvps
                            (TenantId, EventId, AlumniProfileId, Response, UpdatedAtUtc)
                        VALUES ({tenant}, {target.EventId}, {target.ProfileId},
                            {(byte)request.Response}, {now})", token);
                }
                else
                {
                    if (!TryRowVersion(request.RowVersion, out var version) ||
                        !CryptographicOperations.FixedTimeEquals(existing[0].RowVersion, version))
                        return ApiResponse<AlumniEventItemDto>.ErrorResponse("RSVP changed; reload.", 409);
                    var changed = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE dbo.AlumniEventRsvps SET Response = {(byte)request.Response},
                            UpdatedAtUtc = {now}
                        WHERE TenantId = {tenant} AND EventId = {target.EventId}
                            AND AlumniProfileId = {target.ProfileId} AND RowVersion = {version}", token);
                    if (changed != 1)
                        return ApiResponse<AlumniEventItemDto>.ErrorResponse("RSVP changed; reload.", 409);
                }
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO dbo.AlumniEventCommands
                        (TenantId, ClientRequestId, EventId, AlumniProfileId,
                         RequestHash, ActorUserId, CreatedAtUtc)
                    VALUES ({tenant}, {request.ClientRequestId}, {target.EventId},
                        {target.ProfileId}, {hash}, {actor}, {now})", token);
                await AuditEventAsync(tenant, target.EventId,
                    request.Response == 1 ? "RsvpGoing" : "RsvpDeclined", actor, token);
                var saved = await ReadEventAsync(tenant, target.EventId, target.ProfileId, token);
                return ApiResponse<AlumniEventItemDto>.SuccessResponse(ToEvent(saved!, false), "RSVP saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 2601 or 2627)
        {
            _logger.LogWarning(ex, "Alumni RSVP concurrency in tenant {TenantId}", tenant);
            return ApiResponse<AlumniEventItemDto>.ErrorResponse("Concurrent RSVP update; reload.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alumni RSVP failed in tenant {TenantId}", tenant);
            return ApiResponse<AlumniEventItemDto>.ErrorResponse("RSVP could not be saved.", 500);
        }
    }

    private async Task<AlumniEventIdentityWithDto?> ReadEventByReferenceAsync(
        long tenant, Guid reference, CancellationToken ct)
    {
        var ids = await _db.Database.SqlQueryRaw<long>(
            "SELECT Id AS Value FROM dbo.AlumniEvents WHERE TenantId = {0} AND PublicId = {1}",
            tenant, reference).ToListAsync(ct);
        if (ids.Count == 0) return null;
        return new AlumniEventIdentityWithDto(ids[0], await ReadEventAsync(tenant, ids[0], null, ct));
    }

    private async Task<AlumniEventSqlRow?> ReadEventAsync(
        long tenant, long id, long? profileId, CancellationToken ct)
    {
        var rows = await _db.Database.SqlQueryRaw<AlumniEventSqlRow>(
            EventColumns + @"
                CAST(r.Response AS int) AS MyResponse, r.RowVersion AS MyRowVersion
                FROM dbo.AlumniEvents e
                LEFT JOIN dbo.AlumniEventRsvps r ON r.TenantId = e.TenantId
                    AND r.EventId = e.Id AND r.AlumniProfileId = {2}
                WHERE e.TenantId = {0} AND e.Id = {1}", tenant, id, profileId).ToListAsync(ct);
        return rows.FirstOrDefault();
    }

    private async Task AuditEventAsync(long tenant, long eventId, string action, long actor, CancellationToken ct)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO dbo.AlumniEventAudits (TenantId, EventId, ActionCode, ActorUserId, OccurredAtUtc)
            VALUES ({tenant}, {eventId}, {action}, {actor}, {DateTime.UtcNow})", ct);
    }

    private static AlumniEventItemDto ToEvent(AlumniEventSqlRow row, bool admin) => new()
    {
        Reference = row.Reference, CampusId = row.CampusId, Title = row.Title,
        Description = row.Description, StartsAtUtc = DateTime.SpecifyKind(row.StartsAtUtc, DateTimeKind.Utc), EndsAtUtc = DateTime.SpecifyKind(row.EndsAtUtc, DateTimeKind.Utc),
        Status = row.Status, RowVersion = admin ? Convert.ToBase64String(row.RowVersion) : "",
        GoingCount = admin ? row.GoingCount : 0,
        DeclinedCount = admin ? row.DeclinedCount : 0,
        MyResponse = row.MyResponse,
        MyRowVersion = row.MyRowVersion == null ? null : Convert.ToBase64String(row.MyRowVersion),
        CanManage = admin
    };

    private static bool TryRowVersion(string? value, out byte[] version)
    {
        try { version = Convert.FromBase64String(value ?? ""); return version.Length == 8; }
        catch (FormatException) { version = Array.Empty<byte>(); return false; }
    }
    private static string Hash(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private sealed record AlumniEventIdentityWithDto(long Id, AlumniEventSqlRow? Event);
}

public sealed class AlumniEventSqlRow
{
    public Guid Reference { get; set; }
    public long CampusId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int Status { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public long CreatedByUserId { get; set; }
    public int GoingCount { get; set; }
    public int DeclinedCount { get; set; }
    public int? MyResponse { get; set; }
    public byte[]? MyRowVersion { get; set; }
}
public sealed class AlumniEventIdentityRow
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long CampusId { get; set; }
    public long CreatedByUserId { get; set; }
    public string RequestHash { get; set; } = "";
    public int Status { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
public sealed class AlumniEventEligibilityRow
{
    public long EventId { get; set; }
    public long ProfileId { get; set; }
}
public sealed class AlumniEventCommandRow
{
    public string RequestHash { get; set; } = "";
    public long ActorUserId { get; set; }
}
public sealed class AlumniRsvpSqlRow
{
    public int Response { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
