namespace EduOS.Core.Interfaces.IServices;

/// <summary>
/// Finance-owned, tenant-scoped preflight for edits to existing hostel and transport reservations.
/// A true result means that a finance adjustment approval is required before mutation.
/// </summary>
public interface IReservationFinancePolicy
{
    Task<bool> HasPostedActivityAsync(long tenantId, long studentId,
        long studentEnrollmentId, CancellationToken cancellationToken = default);
}
