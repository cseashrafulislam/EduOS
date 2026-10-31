using System.Data;
using System.Security.Cryptography;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Alumni;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Alumni;

public sealed partial class AlumniService
{
    // Consent and V1 completion are checked on EVERY read and write, not only at RSVP creation.
    private const string AttendanceFrom = @"
        FROM dbo.AlumniEventRsvps r
        JOIN dbo.AlumniEvents e ON e.TenantId = r.TenantId AND e.Id = r.EventId
        JOIN dbo.AlumniProfiles p ON p.TenantId = r.TenantId AND p.Id = r.AlumniProfileId
            AND p.CampusId = e.CampusId AND p.DirectoryOptIn = 1
        JOIN dbo.Students s ON s.TenantId = p.TenantId AND s.Id = p.StudentId
            AND s.IsDeleted = 0
        JOIN dbo.StudentExitRecords x ON x.TenantId = p.TenantId
            AND x.Id = p.StudentExitRecordId AND x.StudentId = s.Id
            AND x.ExitType = 2 AND x.FeesCleared = 1 AND x.IsDeleted = 0
        JOIN dbo.StudentEnrollments en ON en.TenantId = p.TenantId
            AND en.Id = x.StudentEnrollmentId AND en.StudentId = s.Id
            AND en.State = 2 AND en.CampusId = p.CampusId AND en.IsDeleted = 0
        WHERE e.TenantId = {0} AND e.PublicId = {2} AND e.Status = 2
            AND r.Response = 1 AND " + CampusGrantSql;

    private const string AttendanceColumns = @"
        SELECT r.Id AS RsvpId, r.EventId, r.AlumniProfileId,
            s.PublicId AS StudentReference, s.StudentCode, s.FullName,
            r.CheckedInAtUtc, r.RowVersion ";

    public async Task<ApiResponse<PagedResult<AlumniEventAttendanceItemDto>>> AttendanceAsync(
        Guid eventReference, AlumniEventAttendanceQueryDto query, CancellationToken ct = default)
    {
        if (!CanRead() || !_user.IsTenantAdmin)
            return ApiResponse<PagedResult<AlumniEventAttendanceItemDto>>.ErrorResponse(
                "Alumni event administration required.", 403);
        if (eventReference == Guid.Empty || query == null || query.Page < 1 || query.PageSize is < 1 or > 50)
            return ApiResponse<PagedResult<AlumniEventAttendanceItemDto>>.ErrorResponse("Invalid event page.");
        var tenant = _user.TenantId;
        var actor = _user.UserId;
        try
        {
            var visible = await _db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM dbo.AlumniEvents e
                  WHERE e.TenantId = {0} AND e.PublicId = {2} AND e.Status = 2 AND " + CampusGrantSql,
                tenant, actor, eventReference).ToListAsync(ct);
            if (visible.FirstOrDefault() != 1)
                return ApiResponse<PagedResult<AlumniEventAttendanceItemDto>>.ErrorResponse("Event not found.", 404);
            var total = await _db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value " + AttendanceFrom, tenant, actor, eventReference).ToListAsync(ct);
            var offset = (int)Math.Min((long)(query.Page - 1) * query.PageSize, int.MaxValue);
            var rows = await _db.Database.SqlQueryRaw<AlumniAttendanceSqlRow>(
                AttendanceColumns + AttendanceFrom +
                " ORDER BY s.StudentCode, r.Id OFFSET {3} ROWS FETCH NEXT {4} ROWS ONLY",
                tenant, actor, eventReference, offset, query.PageSize).ToListAsync(ct);
            return ApiResponse<PagedResult<AlumniEventAttendanceItemDto>>.SuccessResponse(
                new PagedResult<AlumniEventAttendanceItemDto>
                {
                    Items = rows.Select(ToAttendance).ToList(), TotalCount = total.FirstOrDefault(),
                    Page = query.Page, PageSize = query.PageSize
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alumni attendance roster failed in tenant {TenantId}", tenant);
            return ApiResponse<PagedResult<AlumniEventAttendanceItemDto>>.ErrorResponse(
                "Attendance could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<AlumniEventAttendanceItemDto>> CheckInAsync(
        Guid eventReference, AlumniEventCheckInDto request, CancellationToken ct = default)
    {
        if (!CanRead() || !_user.IsTenantAdmin)
            return ApiResponse<AlumniEventAttendanceItemDto>.ErrorResponse(
                "Alumni event administration required.", 403);
        if (eventReference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.StudentReference == Guid.Empty || !TryRowVersion(request.RowVersion, out var version))
            return ApiResponse<AlumniEventAttendanceItemDto>.ErrorResponse(
                "Valid event, graduate, request and version required.");
        var tenant = _user.TenantId;
        var actor = _user.UserId;
        var hash = Hash($"checkin|{eventReference:D}|{request.StudentReference:D}|{request.Present}");
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var now = DateTime.UtcNow;
                var matches = await _db.Database.SqlQueryRaw<AlumniAttendanceSqlRow>(
                    AttendanceColumns + AttendanceFrom +
                    " AND s.PublicId = {3} AND e.StartsAtUtc <= {4} AND e.EndsAtUtc >= {4}",
                    tenant, actor, eventReference, request.StudentReference, now).ToListAsync(token);
                var current = matches.FirstOrDefault();
                if (current == null)
                    return ApiResponse<AlumniEventAttendanceItemDto>.ErrorResponse(
                        "Active event or consented attendee not found.", 404);

                var prior = await _db.Database.SqlQueryRaw<AlumniEventCommandRow>(
                    @"SELECT c.RequestHash, c.ActorUserId,
                        CAST(CASE WHEN EXISTS (
                            SELECT 1 FROM dbo.AlumniEventCommands later
                            WHERE later.TenantId = c.TenantId
                              AND later.EventId = c.EventId
                              AND later.AlumniProfileId = c.AlumniProfileId
                              AND later.Id > c.Id
                        ) THEN 0 ELSE 1 END AS bit) AS IsLatest
                      FROM dbo.AlumniEventCommands c
                      WHERE c.TenantId = {0} AND c.ClientRequestId = {1}",
                    tenant, request.ClientRequestId).ToListAsync(token);
                if (prior.Count > 0)
                {
                    if (prior[0].ActorUserId != actor || prior[0].RequestHash != hash ||
                        !prior[0].IsLatest || current.CheckedInAtUtc.HasValue != request.Present)
                        return ApiResponse<AlumniEventAttendanceItemDto>.ErrorResponse(
                            "Attendance request was changed; submit a new request.", 409);
                    return ApiResponse<AlumniEventAttendanceItemDto>.SuccessResponse(
                        ToAttendance(current), "Attendance already recorded.");
                }
                if (!CryptographicOperations.FixedTimeEquals(current.RowVersion, version))
                    return ApiResponse<AlumniEventAttendanceItemDto>.ErrorResponse(
                        "Attendance changed; reload.", 409);
                if (current.CheckedInAtUtc.HasValue == request.Present)
                    return ApiResponse<AlumniEventAttendanceItemDto>.ErrorResponse(
                        "Attendance already has this state.", 409);

                DateTime? checkedAt = request.Present ? now : null;
                long? checkedBy = request.Present ? actor : null;
                var changed = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE dbo.AlumniEventRsvps
                    SET CheckedInAtUtc = {checkedAt}, CheckedInByUserId = {checkedBy},
                        UpdatedAtUtc = {now}
                    WHERE TenantId = {tenant} AND Id = {current.RsvpId}
                        AND Response = {(byte)1} AND RowVersion = {version}", token);
                if (changed != 1)
                    return ApiResponse<AlumniEventAttendanceItemDto>.ErrorResponse(
                        "Attendance changed; reload.", 409);
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO dbo.AlumniEventCommands
                        (TenantId, ClientRequestId, EventId, AlumniProfileId,
                         RequestHash, ActorUserId, CreatedAtUtc)
                    VALUES ({tenant}, {request.ClientRequestId}, {current.EventId},
                        {current.AlumniProfileId}, {hash}, {actor}, {now})", token);
                await AuditEventAsync(tenant, current.EventId,
                    request.Present ? "CheckedIn" : "CheckInReversed", actor, token);
                var saved = await _db.Database.SqlQueryRaw<AlumniAttendanceVersionRow>(
                    "SELECT RowVersion FROM dbo.AlumniEventRsvps WHERE TenantId = {0} AND Id = {1}",
                    tenant, current.RsvpId).ToListAsync(token);
                current.RowVersion = saved.Single().RowVersion;
                current.CheckedInAtUtc = checkedAt;
                return ApiResponse<AlumniEventAttendanceItemDto>.SuccessResponse(
                    ToAttendance(current), request.Present ? "Attendance recorded." : "Attendance reversed.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 2601 or 2627)
        {
            _logger.LogWarning(ex, "Alumni attendance concurrency in tenant {TenantId}", tenant);
            return ApiResponse<AlumniEventAttendanceItemDto>.ErrorResponse(
                "Concurrent attendance update; reload.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alumni attendance write failed in tenant {TenantId}", tenant);
            return ApiResponse<AlumniEventAttendanceItemDto>.ErrorResponse(
                "Attendance could not be recorded.", 500);
        }
    }

    private static AlumniEventAttendanceItemDto ToAttendance(AlumniAttendanceSqlRow row) => new()
    {
        StudentReference = row.StudentReference, StudentCode = row.StudentCode,
        FullName = row.FullName, CheckedInAtUtc = row.CheckedInAtUtc.HasValue
            ? DateTime.SpecifyKind(row.CheckedInAtUtc.Value, DateTimeKind.Utc) : null,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };
}

public sealed class AlumniAttendanceSqlRow
{
    public long RsvpId { get; set; }
    public long EventId { get; set; }
    public long AlumniProfileId { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = "";
    public string FullName { get; set; } = "";
    public DateTime? CheckedInAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
public sealed class AlumniAttendanceVersionRow
{
    public byte[] RowVersion { get; set; } = [];
}
