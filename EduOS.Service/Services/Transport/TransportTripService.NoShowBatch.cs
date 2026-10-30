using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.Transport;

/// <summary>Atomic, retry-safe closure of unrecorded frozen riders. No changes to prior events.</summary>
public sealed partial class TransportTripService
{
    public Task<ApiResponse<TransportTripNoShowPreviewDto>> GetNoShowPreviewAsync(
        Guid reference, CancellationToken ct = default) =>
        GetNoShowPreviewCoreAsync(reference, driverOnly: false, ct);

    public Task<ApiResponse<TransportTripNoShowPreviewDto>> GetDriverNoShowPreviewAsync(
        Guid reference, CancellationToken ct = default) =>
        GetNoShowPreviewCoreAsync(reference, driverOnly: true, ct);

    private async Task<ApiResponse<TransportTripNoShowPreviewDto>> GetNoShowPreviewCoreAsync(
        Guid reference, bool driverOnly, CancellationToken ct)
    {
        var report = await GetManifestReportCoreAsync(reference, driverOnly, ct);
        if (!report.Success || report.Data == null)
            return ApiResponse<TransportTripNoShowPreviewDto>.ErrorResponse(report.Message, report.StatusCode);
        return ApiResponse<TransportTripNoShowPreviewDto>.SuccessResponse(new TransportTripNoShowPreviewDto
        {
            RiderCount = report.Data.Trip.RiderCount,
            AwaitingCount = report.Data.AwaitingCount,
            BoardedCount = report.Data.BoardedCount,
            DroppedCount = report.Data.DroppedCount,
            NoShowCount = report.Data.NoShowCount
        });
    }

    public Task<ApiResponse<TransportTripNoShowBatchResultDto>> MarkAwaitingNoShowAsync(
        Guid reference, MarkTransportTripAwaitingNoShowRequestDto request, CancellationToken ct = default) =>
        MarkAwaitingNoShowCoreAsync(reference, request, driverOnly: false, ct);

    public Task<ApiResponse<TransportTripNoShowBatchResultDto>> MarkDriverAwaitingNoShowAsync(
        Guid reference, MarkTransportTripAwaitingNoShowRequestDto request, CancellationToken ct = default) =>
        MarkAwaitingNoShowCoreAsync(reference, request, driverOnly: true, ct);

    private static Guid NoShowEventKey(Guid batchKey, long tripId, long riderId)
    {
        var bytes = Encoding.UTF8.GetBytes("EduOS.Transport.NoShowBatch.v1:" +
            batchKey.ToString("N") + ":" + tripId + ":" + riderId);
        return new Guid(SHA256.HashData(bytes).AsSpan(0, 16));
    }

    private async Task<ApiResponse<TransportTripNoShowBatchResultDto>> MarkAwaitingNoShowCoreAsync(
        Guid reference, MarkTransportTripAwaitingNoShowRequestDto request,
        bool driverOnly, CancellationToken ct)
    {
        if (driverOnly ? !CanDrive : !CanManage)
            return ApiResponse<TransportTripNoShowBatchResultDto>.ErrorResponse("Transport permission required.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.ExpectedAwaitingCount is < 1 or > 500)
            return ApiResponse<TransportTripNoShowBatchResultDto>.ErrorResponse(
                "Valid trip, request key and expected awaiting count required.");
        var tenant = _user.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var trip = await _db.TransportTrips.AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference)
                    .Select(x => new { x.Id, x.CampusId, x.State, x.OperatingDate, x.RiderCount })
                    .SingleOrDefaultAsync(token);
                if (trip == null)
                    return ApiResponse<TransportTripNoShowBatchResultDto>.ErrorResponse("Trip not found.", 404);
                if (driverOnly ? !await CanDriveTripAsync(trip.Id, token) :
                    !await HasCampusAsync(trip.CampusId, token))
                    return ApiResponse<TransportTripNoShowBatchResultDto>.ErrorResponse(
                        "Trip assignment or campus access required.", 403);
                if (trip.OperatingDate != today || trip.State is not ("Dispatched" or "Completed"))
                    return ApiResponse<TransportTripNoShowBatchResultDto>.ErrorResponse(
                        "Only today's dispatched trip accepts NoShow closure.", 409);
                if (trip.RiderCount is < 1 or > 500)
                    return ApiResponse<TransportTripNoShowBatchResultDto>.ErrorResponse(
                        "Frozen manifest size is invalid.", 409);
                var riderIds = await _db.TransportTripRiders.AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.TransportTripId == trip.Id)
                    .OrderBy(x => x.Id).Select(x => x.Id).Take(501).ToListAsync(token);
                if (riderIds.Count != trip.RiderCount || riderIds.Distinct().Count() != trip.RiderCount)
                    return ApiResponse<TransportTripNoShowBatchResultDto>.ErrorResponse(
                        "Frozen manifest integrity conflict.", 409);
                var keys = riderIds.ToDictionary(id => id,
                    id => NoShowEventKey(request.ClientRequestId, trip.Id, id));
                var keyValues = keys.Values.ToArray();
                var previous = await _db.TransportTripAttendanceEvents.AsNoTracking()
                    .Where(x => x.TenantId == tenant && keyValues.Contains(x.ClientRequestId))
                    .Select(x => new { x.ClientRequestId, x.TransportTripRiderId, x.Sequence, x.EventType })
                    .Take(501).ToListAsync(token);
                if (previous.Count > 0)
                {
                    if (previous.Count != request.ExpectedAwaitingCount ||
                        previous.Select(x => x.TransportTripRiderId).Distinct().Count() != previous.Count ||
                        previous.Any(x => !keys.TryGetValue(x.TransportTripRiderId, out var key) ||
                            x.ClientRequestId != key || x.Sequence != 1 || x.EventType != "NoShow"))
                        return ApiResponse<TransportTripNoShowBatchResultDto>.ErrorResponse(
                            "Request key conflicts with existing attendance.", 409);
                    return ApiResponse<TransportTripNoShowBatchResultDto>.SuccessResponse(
                        new TransportTripNoShowBatchResultDto
                        {
                            TripReference = reference, RecordedCount = previous.Count,
                            AlreadyRecorded = true, RemainingAwaiting = 0
                        }, "Existing NoShow batch returned.");
                }
                if (trip.State != "Dispatched")
                    return ApiResponse<TransportTripNoShowBatchResultDto>.ErrorResponse(
                        "Completed trip attendance is locked.", 409);
                var touched = await _db.TransportTripAttendanceEvents.AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                        riderIds.Contains(x.TransportTripRiderId))
                    .Select(x => x.TransportTripRiderId).Distinct().Take(501).ToListAsync(token);
                var awaiting = riderIds.Except(touched).ToArray();
                if (awaiting.Length != request.ExpectedAwaitingCount)
                    return ApiResponse<TransportTripNoShowBatchResultDto>.ErrorResponse(
                        "Awaiting count changed; reload before recording NoShow.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                _db.TransportTripAttendanceEvents.AddRange(awaiting.Select(id =>
                    new TransportTripAttendanceEvent
                    {
                        TenantId = tenant, TransportTripRiderId = id,
                        ClientRequestId = keys[id], Sequence = 1,
                        EventType = "NoShow", OccurredAt = now, CreatedBy = _user.UserId
                    }));
                await _db.SaveChangesAsync(token);
                return new ApiResponse<TransportTripNoShowBatchResultDto>
                {
                    Success = true, StatusCode = 201, Message = "Awaiting riders recorded NoShow.",
                    Data = new TransportTripNoShowBatchResultDto
                    {
                        TripReference = reference, RecordedCount = awaiting.Length,
                        AlreadyRecorded = false, RemainingAwaiting = 0
                    }
                };
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Transport NoShow batch uniqueness conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripNoShowBatchResultDto>.ErrorResponse(
                "Concurrent attendance update; reload.", 409);
        }
        catch (Exception ex) when (IsRetryableTripSqlConflict(ex))
        {
            _logger.LogWarning(ex, "Transport NoShow batch SQL conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripNoShowBatchResultDto>.ErrorResponse(
                "Concurrent attendance update; reload.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Transport NoShow batch failed for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripNoShowBatchResultDto>.ErrorResponse(
                "NoShow batch could not be recorded.", 500);
        }
    }
}
