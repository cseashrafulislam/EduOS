using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Transport;
using EduOS.Service.Services.Finance;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    /// <summary>Consume a Finance-approved exact snapshot inside the caller's serializable transaction.</summary>
    private async Task<(TransportFinanceClearance? Clearance, string? Error)> ConsumeTransportFinanceClearanceAsync(
        long id, StudentTransport source, long campusId, TransportClearanceOperation operation,
        DateOnly effectiveDate, long? targetVehicleId, byte[]? targetVersion, CancellationToken ct)
    {
        var tenant = _currentUser.TenantId;
        var row = await _financeClearances.GetQueryable().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
        if (row == null || row.State != TransportClearanceState.Approved ||
            row.SourceAssignmentId != source.Id || row.StudentId != source.StudentId ||
            row.StudentEnrollmentId != source.StudentEnrollmentId || row.CampusId != campusId ||
            row.Operation != operation || row.EffectiveDate != effectiveDate ||
            row.DestinationVehicleId != targetVehicleId)
            return (null, "Finance clearance missing, already used or for a different operation.");
        if (row.SourceVersion != Convert.ToBase64String(source.RowVersion) ||
            row.MonthlyFareAtRequest != source.MonthlyFare ||
            (targetVersion == null ? row.DestinationVersion != null :
                row.DestinationVersion != Convert.ToBase64String(targetVersion)))
            return (null, "Transport terms or destination changed after Finance approval.");
        if (!row.ReviewedByUserId.HasValue || row.ReviewedByUserId == row.RequestedByUserId ||
            row.ApprovedFareCredit < 0 ||
            row.Operation == TransportClearanceOperation.VehicleTransfer && row.ApprovedFareCredit != 0)
            return (null, "Finance approval or fare amount invalid.");
        if (operation == TransportClearanceOperation.UpcomingRiderExtension &&
            (!ReservationPeriodSettlement.ValidAmount(row.ApprovedNetAdjustment) ||
             row.ApprovedFareCredit != 0m))
            return (null, "Approved period adjustment is invalid.");
        var autoCharge = false;
        if (row.NoAdjustmentRequired)
        {
            if (row.ApprovedFareCredit != 0 || row.SettlementJournalId.HasValue ||
                operation == TransportClearanceOperation.UpcomingRiderExtension &&
                row.ApprovedNetAdjustment != 0)
                return (null, "Zero-adjustment Finance attestation contradicts settlement.");
        }
        else
        {
            if (!row.SettlementJournalId.HasValue)
                return (null, "Finance journal missing, reversed, or no longer independently approved.");
            autoCharge = operation == TransportClearanceOperation.UpcomingRiderExtension &&
                row.ApprovedNetAdjustment > 0m &&
                await _reservationFinancePolicy.HasValidAutomaticChargeAsync(tenant,
                    "TRANSPORT", row.Id, row.SettlementJournalId.Value,
                    campusId, source.StudentId, source.StudentEnrollmentId,
                    row.ApprovedNetAdjustment, ct);
            if (!autoCharge && !await _reservationFinancePolicy.HasValidPostedSettlementJournalAsync(
                    tenant, row.SettlementJournalId.Value, ct))
                return (null, "Finance journal missing, reversed, or no longer independently approved.");
        }
        var exposure = await _reservationFinancePolicy.GetExposureHashAsync(tenant,
            source.StudentId, source.StudentEnrollmentId, ct);
        if (exposure == null || exposure != row.FinanceSnapshotHash)
            return (null, "Finance invoice/payment exposure changed after approval.");
        if (autoCharge && !await _reservationFinancePolicy.FinalizeAutomaticChargeAsync(tenant,
                "TRANSPORT", row.Id, row.SettlementJournalId!.Value, campusId,
                source.StudentId, source.StudentEnrollmentId, row.ApprovedNetAdjustment,
                _currentUser.UserId, _clock.GetUtcNow().UtcDateTime, ct))
            return (null, "Reviewed automatic invoice could not be posted with the operation.");
        row.State = TransportClearanceState.Consumed;
        row.ConsumedByUserId = _currentUser.UserId;
        row.ConsumedAtUtc = _clock.GetUtcNow().UtcDateTime;
        row.UpdatedAt = row.ConsumedAtUtc; row.UpdatedBy = _currentUser.UserId;
        return (row, null);
    }
}
