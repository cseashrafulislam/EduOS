using Microsoft.Data.SqlClient;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportTripService
{
    /// <summary>Lock a dispatched trip only after every frozen rider has a terminal attendance outcome.</summary>
    public async Task<ApiResponse<TransportTripRowDto>> CompleteAsync(
        Guid reference, CompleteTransportTripRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<TransportTripRowDto>.ErrorResponse("Transport permission required.", 403);
        byte[] version;
        try { version = Convert.FromBase64String(request?.RowVersion ?? ""); }
        catch (FormatException) { return ApiResponse<TransportTripRowDto>.ErrorResponse("Invalid row version."); }
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty || version.Length != 8)
            return ApiResponse<TransportTripRowDto>.ErrorResponse("Valid trip, request key and row version required.");
        var tenant = _user.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var trip = await _db.TransportTrips.FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference, token);
                if (trip == null) return ApiResponse<TransportTripRowDto>.ErrorResponse("Trip not found.", 404);
                if (!await HasCampusAsync(trip.CampusId, token))
                    return ApiResponse<TransportTripRowDto>.ErrorResponse("Campus access required.", 403);
                if (trip.State == "Completed")
                {
                    if (trip.CompletionRequestId != request.ClientRequestId)
                        return ApiResponse<TransportTripRowDto>.ErrorResponse("Trip already completed.", 409);
                    var replay = await Query().FirstAsync(x => x.Reference == reference, token);
                    return ApiResponse<TransportTripRowDto>.SuccessResponse(replay, "Existing completion returned.");
                }
                if (trip.State != "Dispatched" || trip.OperatingDate > today ||
                    trip.OperatingDate < today.AddDays(-30))
                    return ApiResponse<TransportTripRowDto>.ErrorResponse("Only recent dispatched trips can be completed.", 409);
                if (!trip.RowVersion.SequenceEqual(version))
                    return ApiResponse<TransportTripRowDto>.ErrorResponse("Trip changed; reload before completion.", 409);
                if (trip.RiderCount is < 1 or > 500)
                    return ApiResponse<TransportTripRowDto>.ErrorResponse("Frozen rider count is invalid.", 409);
                var riderIds = await _db.TransportTripRiders.AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.TransportTripId == trip.Id)
                    .Select(x => x.Id).Take(501).ToListAsync(token);
                if (riderIds.Count != trip.RiderCount || riderIds.Distinct().Count() != trip.RiderCount)
                    return ApiResponse<TransportTripRowDto>.ErrorResponse("Frozen rider manifest changed.", 409);
                var events = await _db.TransportTripAttendanceEvents.AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && riderIds.Contains(x.TransportTripRiderId))
                    .Select(x => new { x.TransportTripRiderId, x.Sequence, x.EventType })
                    .Take(1001).ToListAsync(token);
                if (events.Count > 1000)
                    return ApiResponse<TransportTripRowDto>.ErrorResponse("Attendance history exceeds safe bounds.", 409);
                var groups = events.GroupBy(x => x.TransportTripRiderId).ToDictionary(x => x.Key, x => x.ToList());
                foreach (var riderId in riderIds)
                {
                    if (!groups.TryGetValue(riderId, out var history))
                        return ApiResponse<TransportTripRowDto>.ErrorResponse("Some riders have no attendance outcome.", 409);
                    var first = history.SingleOrDefault(x => x.Sequence == 1);
                    var second = history.SingleOrDefault(x => x.Sequence == 2);
                    var terminal = (history.Count == 1 && first?.EventType == "NoShow") ||
                        (history.Count == 2 && first?.EventType == "Boarded" && second?.EventType == "Dropped");
                    if (!terminal)
                        return ApiResponse<TransportTripRowDto>.ErrorResponse("All riders must be dropped or marked no-show.", 409);
                }
                _db.Entry(trip).Property(x => x.RowVersion).OriginalValue = version;
                trip.State = "Completed";
                trip.CompletionRequestId = request.ClientRequestId;
                trip.CompletedAt = _clock.GetUtcNow().UtcDateTime;
                trip.CompletedBy = _user.UserId;
                await _db.SaveChangesAsync(token);
                var result = await Query().FirstAsync(x => x.Reference == reference, token);
                return ApiResponse<TransportTripRowDto>.SuccessResponse(result, "Trip completed and attendance locked.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<TransportTripRowDto>.ErrorResponse("Trip changed; reload.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Transport trip completion conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripRowDto>.ErrorResponse("Concurrent completion conflict.", 409);
        }
        catch (Exception ex) when (IsRetryableTripSqlConflict(ex))
        {
            _logger.LogWarning(ex, "Transport trip SQL concurrency conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripRowDto>.ErrorResponse("Concurrent trip operation; reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Transport trip completion failed for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripRowDto>.ErrorResponse("Trip could not be completed.", 500);
        }
    }
}
