namespace EduOS.Core.Interfaces.IServices;

/// <summary>
/// Compute effective tenant-scoped permission. Explicit per-user deny overrides allow;
/// role fallback is applied only when no user-specific decision exists.
/// Resource and action identifiers are canonical permission codes, not UI visibility hints.
/// </summary>
public interface IPermissionService
{
    Task<bool> HasPermissionAsync(long tenantId, long userId, string resourceCode, string actionCode, CancellationToken cancellationToken = default);
}
