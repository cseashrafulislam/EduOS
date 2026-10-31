using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Hostel;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    /// <summary>
    /// Called only inside the serializable reservation mutation transaction. An approved
    /// Finance snapshot is single-use, operation/date/bed/version/campus bound.
    /// Returning an error rolls back the entire enclosing transaction.
    /// </summary>
    private async Task<(HostelFinanceClearance? Clearance, string? Error)> ConsumeHostelFinanceClearanceAsync(
        long clearanceId, StudentHostelAllocation source, long campusId,
        HostelClearanceOperation operation, DateOnly date, long? destinationBedId,
        byte[]? destinationVersion, CancellationToken ct)
    {
        var tenant = _currentUser.TenantId;
        var clearance = await _hostelClearances.GetQueryable().FirstOrDefaultAsync(x =>
            x.Id == clearanceId && x.TenantId == tenant && !x.IsDeleted, ct);
        if (clearance == null || clearance.State != HostelClearanceState.Approved ||
            clearance.SourceAllocationId != source.Id || clearance.StudentId != source.StudentId ||
            clearance.StudentEnrollmentId != source.StudentEnrollmentId ||
            clearance.CampusId != campusId || clearance.Operation != operation ||
            clearance.EffectiveDate != date || clearance.DestinationBedId != destinationBedId)
            return (null, "Finance approval is absent, already used, or for a different operation.");
        if (clearance.SourceVersion != Convert.ToBase64String(source.RowVersion) ||
            clearance.MonthlyRentAtRequest != source.MonthlyRent ||
            clearance.DepositAtRequest != source.SecurityDeposit ||
            (destinationVersion == null ? clearance.DestinationVersion != null :
                clearance.DestinationVersion != Convert.ToBase64String(destinationVersion)))
            return (null, "Hostel reservation or destination changed after Finance approval.");
        if (clearance.RequestedByUserId == clearance.ReviewedByUserId ||
            !clearance.ReviewedByUserId.HasValue ||
            clearance.ApprovedRentCredit < 0 || clearance.ApprovedDepositRefund < 0 ||
            clearance.ApprovedDepositRefund > clearance.DepositAtRequest)
            return (null, "Finance approval or settlement amounts are invalid.");
        if (clearance.NoAdjustmentRequired)
        {
            if (clearance.SettlementJournalId.HasValue ||
                clearance.ApprovedRentCredit != 0 || clearance.ApprovedDepositRefund != 0)
                return (null, "Zero-adjustment attestation contradicts settlement amounts.");
        }
        else
        {
            if (!clearance.SettlementJournalId.HasValue ||
                !await _reservationFinancePolicy.HasValidPostedSettlementJournalAsync(tenant,
                    clearance.SettlementJournalId.Value, ct))
                return (null, "Finance settlement journal is missing, reversed or no longer posted.");
        }
        var exposure = await _reservationFinancePolicy.GetExposureHashAsync(tenant,
            source.StudentId, source.StudentEnrollmentId, ct);
        if (exposure == null || exposure != clearance.FinanceSnapshotHash)
            return (null, "Finance invoice/payment exposure changed after approval; reconcile again.");
        clearance.State = HostelClearanceState.Consumed;
        clearance.ConsumedByUserId = _currentUser.UserId;
        clearance.ConsumedAtUtc = _clock.GetUtcNow().UtcDateTime;
        clearance.UpdatedAt = clearance.ConsumedAtUtc;
        clearance.UpdatedBy = _currentUser.UserId;
        return (clearance, null);
    }
}
