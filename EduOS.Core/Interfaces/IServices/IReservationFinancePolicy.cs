namespace EduOS.Core.Interfaces.IServices;

/// <summary>
/// Finance-owned, tenant-scoped preflight for edits to existing hostel and transport reservations.
/// True means posted activity OR an issued, unposted invoice exists. Both block
/// financially effective reservation mutations until Finance reconciliation/approval.
/// This is a conservative exposure gate, not an approval or a posting operation.
/// </summary>
public interface IReservationFinancePolicy
{
    Task<bool> HasPostedActivityAsync(long tenantId, long studentId,
        long studentEnrollmentId, CancellationToken cancellationToken = default);
    /// <summary>Stable hash of Finance exposure, or null when evidence cannot be bounded/verified.</summary>
    Task<string?> GetExposureHashAsync(long tenantId, long studentId,
        long studentEnrollmentId, CancellationToken cancellationToken = default);
    Task<bool> HasValidPostedSettlementJournalAsync(long tenantId, long journalId,
        CancellationToken cancellationToken = default);
}
