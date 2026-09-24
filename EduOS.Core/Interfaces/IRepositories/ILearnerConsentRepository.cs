using EduOS.Core.Enums;

namespace EduOS.Core.Interfaces.IRepositories;

public interface ILearnerConsentRepository
{
    Task<IReadOnlyList<LearnerConsentRequestRecord>> GetPendingForUserAsync(
        long userId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LearnerDataGrantRecord>> GetActiveGrantsForUserAsync(
        long userId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<LearnerConsentMutationResult> ResolveAsync(
        Guid requestReference,
        long userId,
        LearnerConsentDecision decision,
        DateTime utcNow,
        DateTime grantExpiresAt,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default);

    Task<LearnerConsentMutationResult> RevokeAsync(
        Guid grantReference,
        long userId,
        DateTime utcNow,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default);
}

public sealed record LearnerConsentRequestRecord(
    Guid Reference,
    string RequestingInstitution,
    LearnerIdentityPurpose Purpose,
    LearnerDataScope RequestedScopes,
    DateTime ExpiresAt);

public sealed record LearnerDataGrantRecord(
    Guid Reference,
    string Institution,
    LearnerIdentityPurpose Purpose,
    LearnerDataScope GrantedScopes,
    DateTime StartsAt,
    DateTime ExpiresAt);

public sealed record LearnerConsentMutationResult(
    LearnerConsentMutationState State,
    Guid? GrantReference = null,
    DateTime? GrantExpiresAt = null,
    bool AlreadyProcessed = false);

public enum LearnerConsentMutationState
{
    NotFound = 0,
    Approved = 1,
    Denied = 2,
    Expired = 3,
    Revoked = 4,
    Conflict = 5
}
