using System.Data;
using System.Globalization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Alumni;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Enums.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Alumni;

public sealed partial class AlumniService
{
    private const int InvitationBatchLimit = 5; // bounded network work per authorized request
    // External invitations require a separate channel opt-in; directory sharing and
    // RSVPs never substitute for it. Recheck all V1 graduation evidence at send time.
    private const string VerifiedInviteFrom = @"
        FROM dbo.AlumniProfiles p
        JOIN dbo.Students s ON s.TenantId = p.TenantId AND s.Id = p.StudentId AND s.IsDeleted = 0
        JOIN dbo.StudentExitRecords x ON x.TenantId = p.TenantId AND x.Id = p.StudentExitRecordId
            AND x.StudentId = s.Id AND x.ExitType = 2 AND x.FeesCleared = 1 AND x.IsDeleted = 0
        JOIN dbo.StudentEnrollments en ON en.TenantId = p.TenantId AND en.Id = x.StudentEnrollmentId
            AND en.StudentId = s.Id AND en.CampusId = p.CampusId AND en.State = 2 AND en.IsDeleted = 0
        WHERE p.TenantId = {0} AND p.CampusId = {1}";

    private static string InvitationConsent(int channel) => channel == 2
        ? " AND p.InvitationEmailOptIn = 1 AND p.ContactEmail IS NOT NULL AND LEN(p.ContactEmail) > 0"
        : " AND p.InvitationSmsOptIn = 1 AND p.ContactPhone IS NOT NULL AND LEN(p.ContactPhone) > 0";

    private async Task<AlumniInvitationEventRow?> InvitationEventAsync(Guid reference, bool requireFuture,
        CancellationToken ct)
    {
        var rows = await _db.Database.SqlQueryRaw<AlumniInvitationEventRow>(@"
            SELECT e.Id, e.CampusId, e.Title, e.Description, e.StartsAtUtc
            FROM dbo.AlumniEvents e
            WHERE e.TenantId = {0} AND e.PublicId = {2} AND e.Status = 2 AND "
            + CampusGrantSql + (requireFuture ? " AND e.StartsAtUtc > SYSUTCDATETIME()" : ""),
            _user.TenantId, _user.UserId, reference).ToListAsync(ct);
        return rows.FirstOrDefault();
    }

    private Task<CommunicationGateway?> DefaultInvitationGatewayAsync(int channel, CancellationToken ct) =>
        _db.CommunicationGateways.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Channel == (NotificationChannelType)channel &&
            x.IsActive && x.IsDefault && !x.IsDeleted && x.ProtectedCredential != null, ct);

    public async Task<ApiResponse<AlumniInvitationSummaryDto>> InvitationSummaryAsync(
        Guid reference, int channel, CancellationToken ct = default)
    {
        if (!CanRead() || !_user.IsTenantAdmin)
            return ApiResponse<AlumniInvitationSummaryDto>.ErrorResponse("Tenant administrator required.", 403);
        if (reference == Guid.Empty || channel is not (2 or 3))
            return ApiResponse<AlumniInvitationSummaryDto>.ErrorResponse("Invalid event or channel.");
        try
        {
            var e = await InvitationEventAsync(reference, false, ct);
            if (e == null) return ApiResponse<AlumniInvitationSummaryDto>.ErrorResponse("Published event not found or campus access denied.", 404);
            var eligible = await _db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value " + VerifiedInviteFrom + InvitationConsent(channel),
                _user.TenantId, e.CampusId).ToListAsync(ct);
            var remaining = await _db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value " + VerifiedInviteFrom + InvitationConsent(channel) + @"
                 AND NOT EXISTS (SELECT 1 FROM dbo.AlumniInvitationAttempts a
                     WHERE a.TenantId = p.TenantId AND a.EventId = {2} AND a.AlumniProfileId = p.Id
                         AND a.Channel = {3})",
                _user.TenantId, e.CampusId, e.Id, channel).ToListAsync(ct);
            var states = await _db.Database.SqlQueryRaw<AlumniInvitationCountRow>(@"
                SELECT
                  CAST(COUNT(CASE WHEN State = 2 THEN 1 END) AS int) AS AcceptedCount,
                  CAST(COUNT(CASE WHEN State IN (1, 3) THEN 1 END) AS int) AS ReviewCount,
                  CAST(COUNT(CASE WHEN State = 4 THEN 1 END) AS int) AS SuppressedCount
                FROM dbo.AlumniInvitationAttempts
                WHERE TenantId = {0} AND EventId = {1} AND Channel = {2}",
                _user.TenantId, e.Id, channel).ToListAsync(ct);
            return ApiResponse<AlumniInvitationSummaryDto>.SuccessResponse(new AlumniInvitationSummaryDto
            {
                Channel = channel, GatewayReady = await DefaultInvitationGatewayAsync(channel, ct) != null,
                EligibleCount = eligible[0], RemainingCount = remaining[0],
                AcceptedCount = states[0].AcceptedCount, ReviewCount = states[0].ReviewCount,
                SuppressedCount = states[0].SuppressedCount
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Alumni invitation summary failed in tenant {TenantId}", _user.TenantId);
            return ApiResponse<AlumniInvitationSummaryDto>.ErrorResponse("Invitation summary could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<AlumniInvitationBatchDto>> SendInvitationsAsync(
        Guid reference, AlumniInvitationSendDto request, CancellationToken ct = default)
    {
        if (!CanRead() || !_user.IsTenantAdmin)
            return ApiResponse<AlumniInvitationBatchDto>.ErrorResponse("Tenant administrator required.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.Channel is not (2 or 3) || !request.Confirmed)
            return ApiResponse<AlumniInvitationBatchDto>.ErrorResponse("Explicit confirmation, event, channel and request ID required.");
        var tenant = _user.TenantId;
        var actor = _user.UserId;
        try
        {
            // Only reserve identities within the transaction. NEVER call an external
            // gateway inside a retryable SQL transaction.
            var reserved = await _db.ExecuteInTransactionAsync(async token =>
            {
                var e = await InvitationEventAsync(reference, true, token);
                if (e == null) return new AlumniInvitationReservation(0, 404, false);
                var prior = await _db.Database.SqlQueryRaw<AlumniInvitationBatchRow>(@"
                    SELECT Id, EventId, CAST(Channel AS int) AS Channel, ActorUserId
                    FROM dbo.AlumniInvitationBatches WHERE TenantId = {0} AND ClientRequestId = {1}",
                    tenant, request.ClientRequestId).ToListAsync(token);
                if (prior.Count != 0)
                    return prior[0].EventId == e.Id && prior[0].Channel == request.Channel &&
                        prior[0].ActorUserId == actor
                        ? new AlumniInvitationReservation(prior[0].Id, 200, true)
                        : new AlumniInvitationReservation(0, 409, false);

                var gateway = await DefaultInvitationGatewayAsync(request.Channel, token);
                if (gateway == null) return new AlumniInvitationReservation(0, 409, false);

                var candidates = await _db.Database.SqlQueryRaw<long>(
                    "SELECT TOP (" + InvitationBatchLimit + ") p.Id AS Value " +
                    VerifiedInviteFrom + InvitationConsent(request.Channel) + @"
                    AND NOT EXISTS (SELECT 1 FROM dbo.AlumniInvitationAttempts a
                        WHERE a.TenantId = p.TenantId AND a.EventId = {2}
                          AND a.AlumniProfileId = p.Id AND a.Channel = {3})
                    ORDER BY p.Id", tenant, e.CampusId, e.Id, request.Channel).ToListAsync(token);

                var now = DateTime.UtcNow;
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO dbo.AlumniInvitationBatches
                    (TenantId, EventId, ClientRequestId, Channel, ActorUserId, CreatedAtUtc)
                    VALUES ({tenant}, {e.Id}, {request.ClientRequestId}, {(byte)request.Channel}, {actor}, {now})", token);
                var batchId = (await _db.Database.SqlQueryRaw<long>(@"
                    SELECT Id AS Value FROM dbo.AlumniInvitationBatches
                    WHERE TenantId = {0} AND ClientRequestId = {1}",
                    tenant, request.ClientRequestId).ToListAsync(token))[0];
                foreach (var profileId in candidates)
                    await _db.Database.ExecuteSqlInterpolatedAsync($@"
                        INSERT INTO dbo.AlumniInvitationAttempts
                        (TenantId, EventId, AlumniProfileId, BatchId, Channel, GatewayId, State, CreatedAtUtc)
                        VALUES ({tenant}, {e.Id}, {profileId}, {batchId},
                            {(byte)request.Channel}, {gateway.Id}, {(byte)1}, {now})", token);
                await AuditEventAsync(tenant, e.Id, "InviteReserved", actor, token);
                return new AlumniInvitationReservation(batchId, 200, false);
            }, IsolationLevel.Serializable, ct);

            if (reserved.StatusCode != 200)
                return ApiResponse<AlumniInvitationBatchDto>.ErrorResponse(
                    reserved.StatusCode == 404 ? "Published future event not found or campus access denied." :
                    "Invitation request reference already used, or no configured default gateway.", reserved.StatusCode);

            if (!reserved.Replay)
            {
                var attemptIds = await _db.Database.SqlQueryRaw<long>(@"
                    SELECT Id AS Value FROM dbo.AlumniInvitationAttempts
                    WHERE TenantId = {0} AND BatchId = {1} ORDER BY Id", tenant, reserved.BatchId).ToListAsync(ct);
                foreach (var attemptId in attemptIds)
                    await DispatchInvitationOnceAsync(tenant, actor, attemptId, request.Channel, ct);
            }

            var counts = await _db.Database.SqlQueryRaw<AlumniInvitationBatchCountRow>(@"
                SELECT CAST(COUNT(*) AS int) AS ReservedCount,
                  CAST(COUNT(CASE WHEN State = 2 THEN 1 END) AS int) AS AcceptedCount,
                  CAST(COUNT(CASE WHEN State IN (1, 3) THEN 1 END) AS int) AS ReviewCount,
                  CAST(COUNT(CASE WHEN State = 4 THEN 1 END) AS int) AS SuppressedCount
                FROM dbo.AlumniInvitationAttempts WHERE TenantId = {0} AND BatchId = {1}",
                tenant, reserved.BatchId).ToListAsync(ct);
            var result = counts[0];
            return ApiResponse<AlumniInvitationBatchDto>.SuccessResponse(new AlumniInvitationBatchDto
            {
                ClientRequestId = request.ClientRequestId, ReservedCount = result.ReservedCount,
                AcceptedCount = result.AcceptedCount, ReviewCount = result.ReviewCount,
                SuppressedCount = result.SuppressedCount, AlreadyProcessed = reserved.Replay
            });
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205)
        {
            _logger.LogWarning(ex, "Alumni invitation reservation conflict in tenant {TenantId}", tenant);
            return ApiResponse<AlumniInvitationBatchDto>.ErrorResponse("Concurrent invitation request; reload and retry with same request ID.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Alumni invitation attempt failed in tenant {TenantId}", tenant);
            return ApiResponse<AlumniInvitationBatchDto>.ErrorResponse(
                "Dispatch outcome needs review. Reload status; never resend with a new reference until reconciled.", 500);
        }
    }

    private async Task DispatchInvitationOnceAsync(long tenant, long actor, long attemptId,
        int channel, CancellationToken ct)
    {
        // DB reservation is already committed. A crash or timeout leaves State=1,
        // which counts as REVIEW and is never picked for automatic retry.
        var destinationColumn = channel == 2 ? "p.ContactEmail" : "p.ContactPhone";
        var recipients = await _db.Database.SqlQueryRaw<AlumniInvitationRecipientRow>($@"
            SELECT {destinationColumn} AS Destination, e.Title, e.Description, e.StartsAtUtc
            FROM dbo.AlumniInvitationAttempts a
            JOIN dbo.AlumniEvents e ON e.TenantId = a.TenantId AND e.Id = a.EventId
            JOIN dbo.AlumniProfiles p ON p.TenantId = a.TenantId AND p.Id = a.AlumniProfileId
            JOIN dbo.Students s ON s.TenantId = p.TenantId AND s.Id = p.StudentId AND s.IsDeleted = 0
            JOIN dbo.StudentExitRecords x ON x.TenantId = p.TenantId AND x.Id = p.StudentExitRecordId
                 AND x.StudentId = s.Id AND x.ExitType = 2 AND x.FeesCleared = 1 AND x.IsDeleted = 0
            JOIN dbo.StudentEnrollments en ON en.TenantId = p.TenantId AND en.Id = x.StudentEnrollmentId
                 AND en.StudentId = s.Id AND en.CampusId = p.CampusId AND en.State = 2 AND en.IsDeleted = 0
            WHERE a.TenantId = {{0}} AND a.Id = {{2}} AND a.Channel = {{3}} AND a.State = 1
              AND e.Status = 2 AND e.StartsAtUtc > SYSUTCDATETIME() AND p.CampusId = e.CampusId
              AND {CampusGrantSql} {InvitationConsent(channel)}",
            tenant, actor, attemptId, channel).ToListAsync(ct);

        var recipient = recipients.FirstOrDefault();
        var state = (byte)4;
        string? failure = "CONSENT_OR_EVENT_REVOKED";
        if (recipient != null && !string.IsNullOrWhiteSpace(recipient.Destination))
        {
            var gatewayId = (await _db.Database.SqlQueryRaw<long>(@"
                SELECT GatewayId AS Value FROM dbo.AlumniInvitationAttempts
                WHERE TenantId = {0} AND Id = {1} AND State = 1", tenant, attemptId).ToListAsync(ct))
                .FirstOrDefault();
            var gateway = await _db.CommunicationGateways.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == gatewayId && x.Channel == (NotificationChannelType)channel &&
                x.IsDefault && x.IsActive && !x.IsDeleted, ct);
            if (gateway == null)
            {
                state = 3; failure = "GATEWAY_CHANGED";
            }
            else
            {
                var bn = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "bn";
                var when = DateTime.SpecifyKind(recipient.StartsAtUtc, DateTimeKind.Utc)
                    .ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
                var subject = bn ? "প্রাক্তন শিক্ষার্থীদের আমন্ত্রণ: " + recipient.Title
                    : "Alumni event invitation: " + recipient.Title;
                var text = bn
                    ? $"আপনার প্রতিষ্ঠানের অনুষ্ঠানে আমন্ত্রণ।\nঅনুষ্ঠান: {recipient.Title}\nসময়: {when}\n{recipient.Description}\n\nএমন আমন্ত্রণ বন্ধ করতে EduOS Alumni গোপনীয়তা সেটিং থেকে এই চ্যানেলের সম্মতি প্রত্যাহার করুন।"
                    : $"You are invited to your institution's alumni event.\nEvent: {recipient.Title}\nStarts: {when}\n{recipient.Description}\n\nTo stop invitations, revoke this channel's consent in your EduOS Alumni privacy settings.";
                var sent = await _invitationTransport.SendAsync(gateway, recipient.Destination, subject, text, ct);
                state = sent.Accepted ? (byte)2 : (byte)3;
                failure = sent.FailureCode;
            }
        }
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dbo.AlumniInvitationAttempts SET State = {state}, FailureCode = {failure},
                CompletedAtUtc = {DateTime.UtcNow}
            WHERE TenantId = {tenant} AND Id = {attemptId} AND State = {(byte)1}", ct);
    }

    private sealed record AlumniInvitationReservation(long BatchId, int StatusCode, bool Replay);
}

public sealed class AlumniInvitationEventRow
{
    public long Id { get; set; }
    public long CampusId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime StartsAtUtc { get; set; }
}
public sealed class AlumniInvitationBatchRow
{
    public long Id { get; set; }
    public long EventId { get; set; }
    public int Channel { get; set; }
    public long ActorUserId { get; set; }
}
public class AlumniInvitationCountRow
{
    public int AcceptedCount { get; set; }
    public int ReviewCount { get; set; }
    public int SuppressedCount { get; set; }
}
public sealed class AlumniInvitationBatchCountRow : AlumniInvitationCountRow
{
    public int ReservedCount { get; set; }
}
public sealed class AlumniInvitationRecipientRow
{
    public string Destination { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime StartsAtUtc { get; set; }
}
