using EduOS.Core.Common;

namespace EduOS.Core.Interfaces.IServices;

public sealed record ReservationCommandReplay<T>(bool Found, bool Conflict, ApiResponse<T>? Response);

/// <summary>Uses the caller's transaction and DbContext; receipt and source mutation commit atomically.</summary>
public interface IReservationCommandReceiptStore
{
    Task<ReservationCommandReplay<T>> ReplayAsync<T>(long tenantId, long actorId,
        string scope, Guid requestId, long targetId, string requestHash, CancellationToken ct = default);
    Task RecordAsync<T>(long tenantId, long actorId, string scope, Guid requestId,
        long targetId, string requestHash, ApiResponse<T> response, CancellationToken ct = default);
}
