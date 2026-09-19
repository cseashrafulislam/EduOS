using EduOS.Core.Entities.Learners;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

/// <summary>
/// The only persistence boundary allowed to resolve a consent request owned by
/// a different tenant. Every mutation first proves that the signed-in student or
/// parent controls an active link to the platform person.
/// </summary>
public sealed class LearnerConsentRepository : ILearnerConsentRepository
{
    private const LearnerDataScope AllowedScopes =
        LearnerDataScope.BasicIdentity
        | LearnerDataScope.InstitutionMembershipHistory
        | LearnerDataScope.AcademicSummary;

    private readonly EduOSDbContext _context;

    public LearnerConsentRepository(EduOSDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<LearnerConsentRequestRecord>> GetPendingForUserAsync(
        long userId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var personIds = await GetControlledPersonIdsAsync(userId, cancellationToken);
        if (personIds.Length == 0) return [];

        var requests = await _context.LearnerConsentRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Tenant)
            .Where(x => !x.IsDeleted
                        && personIds.Contains(x.PersonId)
                        && x.Status == LearnerConsentRequestStatus.Pending
                        && x.ExpiresAt > utcNow)
            .OrderBy(x => x.ExpiresAt)
            .ToListAsync(cancellationToken);

        return requests.Select(x => new LearnerConsentRequestRecord(
                x.PublicId,
                !string.IsNullOrWhiteSpace(x.Tenant?.Name) ? x.Tenant.Name : "Institution",
                x.Purpose,
                x.RequestedScopes,
                x.ExpiresAt))
            .ToList();
    }

    public async Task<IReadOnlyList<LearnerDataGrantRecord>> GetActiveGrantsForUserAsync(
        long userId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var personIds = await GetControlledPersonIdsAsync(userId, cancellationToken);
        if (personIds.Length == 0) return [];

        var grants = await _context.LearnerDataGrants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Tenant)
            .Where(x => !x.IsDeleted
                        && personIds.Contains(x.PersonId)
                        && x.Status == LearnerDataGrantStatus.Active
                        && x.ExpiresAt > utcNow)
            .OrderBy(x => x.ExpiresAt)
            .ToListAsync(cancellationToken);

        return grants.Select(x => new LearnerDataGrantRecord(
                x.PublicId,
                !string.IsNullOrWhiteSpace(x.Tenant?.Name) ? x.Tenant.Name : "Institution",
                x.Purpose,
                x.GrantedScopes,
                x.StartsAt,
                x.ExpiresAt))
            .ToList();
    }

    public async Task<LearnerConsentMutationResult> ResolveAsync(
        Guid requestReference,
        long userId,
        LearnerConsentDecision decision,
        DateTime utcNow,
        DateTime grantExpiresAt,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var request = await FindAuthorizedRequestAsync(
            requestReference,
            userId,
            cancellationToken);
        if (request == null) return NotFound();

        if (request.Status != LearnerConsentRequestStatus.Pending)
            return await ExistingResolutionAsync(request, decision, cancellationToken);

        if (request.ExpiresAt <= utcNow)
        {
            request.Status = LearnerConsentRequestStatus.Expired;
            request.ResolvedAt = utcNow;
            request.ResolvedByUserId = userId;
            AddLog(request, userId, LearnerIdentityAccessAction.ResolveConsent,
                LearnerIdentityAccessOutcome.Expired, "CONSENT_REQUEST_EXPIRED", ipAddress, userAgent);
            try
            {
                await SaveDecisionAsync(request, userId, cancellationToken);
                return new LearnerConsentMutationResult(LearnerConsentMutationState.Expired);
            }
            catch (DbUpdateConcurrencyException)
            {
                return await ReadAfterConflictAsync(requestReference, userId, decision, cancellationToken);
            }
        }

        if (decision == LearnerConsentDecision.Deny)
        {
            request.Status = LearnerConsentRequestStatus.Denied;
            request.ResolvedAt = utcNow;
            request.ResolvedByUserId = userId;
            AddLog(request, userId, LearnerIdentityAccessAction.ResolveConsent,
                LearnerIdentityAccessOutcome.Denied, "CONSENT_DENIED", ipAddress, userAgent);
            try
            {
                await SaveDecisionAsync(request, userId, cancellationToken);
                return new LearnerConsentMutationResult(LearnerConsentMutationState.Denied);
            }
            catch (DbUpdateConcurrencyException)
            {
                return await ReadAfterConflictAsync(requestReference, userId, decision, cancellationToken);
            }
        }

        if (!Enum.IsDefined(request.Purpose)
            || request.RequestedScopes == LearnerDataScope.None
            || !request.RequestedScopes.HasFlag(LearnerDataScope.BasicIdentity)
            || (request.RequestedScopes & ~AllowedScopes) != 0
            || grantExpiresAt <= utcNow
            || grantExpiresAt > utcNow.AddDays(3650))
        {
            AddLog(request, userId, LearnerIdentityAccessAction.ResolveConsent,
                LearnerIdentityAccessOutcome.Conflict, "CONSENT_SCOPE_INVALID", ipAddress, userAgent);
            await SaveDecisionAsync(request, userId, cancellationToken);
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Conflict);
        }

        var currentLink = await _context.StudentPersonLinks
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => !x.IsDeleted
                                      && x.TenantId == request.TenantId
                                      && x.StudentId == request.RequestedStudentId,
                cancellationToken);
        if (currentLink != null
            && (currentLink.PersonId != request.PersonId
                || currentLink.Status != StudentPersonLinkStatus.Active))
        {
            AddLog(request, userId, LearnerIdentityAccessAction.ResolveConsent,
                LearnerIdentityAccessOutcome.Conflict, "TARGET_STUDENT_LINK_CONFLICT", ipAddress, userAgent);
            await SaveDecisionAsync(request, userId, cancellationToken);
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Conflict);
        }

        var existingGrant = await _context.LearnerDataGrants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.ConsentRequestId == request.Id,
                cancellationToken);
        if (existingGrant != null)
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Conflict);

        if (currentLink == null)
        {
            await _context.StudentPersonLinks.AddAsync(new StudentPersonLink
            {
                TenantId = request.TenantId,
                StudentId = request.RequestedStudentId,
                PersonId = request.PersonId,
                Status = StudentPersonLinkStatus.Active,
                LinkedAt = utcNow,
                LinkedByUserId = userId
            }, cancellationToken);
        }

        var grant = new LearnerDataGrant
        {
            TenantId = request.TenantId,
            PublicId = Guid.NewGuid(),
            PersonId = request.PersonId,
            StudentId = request.RequestedStudentId,
            ConsentRequestId = request.Id,
            Purpose = request.Purpose,
            GrantedScopes = request.RequestedScopes,
            Status = LearnerDataGrantStatus.Active,
            StartsAt = utcNow,
            ExpiresAt = grantExpiresAt,
            GrantedByUserId = userId
        };
        await _context.LearnerDataGrants.AddAsync(grant, cancellationToken);

        request.Status = LearnerConsentRequestStatus.Approved;
        request.ResolvedAt = utcNow;
        request.ResolvedByUserId = userId;
        AddLog(request, userId, LearnerIdentityAccessAction.ResolveConsent,
            LearnerIdentityAccessOutcome.Approved, "CONSENT_APPROVED", ipAddress, userAgent);

        try
        {
            await SaveDecisionAsync(request, userId, cancellationToken);
            return new LearnerConsentMutationResult(
                LearnerConsentMutationState.Approved,
                grant.PublicId,
                grant.ExpiresAt);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ReadAfterConflictAsync(requestReference, userId, decision, cancellationToken);
        }
        catch (DbUpdateException)
        {
            return await ReadAfterConflictAsync(requestReference, userId, decision, cancellationToken);
        }
    }

    public async Task<LearnerConsentMutationResult> RevokeAsync(
        Guid grantReference,
        long userId,
        DateTime utcNow,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var grant = await _context.LearnerDataGrants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.PublicId == grantReference,
                cancellationToken);
        if (grant == null
            || !await UserControlsPersonAsync(grant.PersonId, userId, cancellationToken))
        {
            return NotFound();
        }

        if (grant.Status == LearnerDataGrantStatus.Revoked)
        {
            return new LearnerConsentMutationResult(
                LearnerConsentMutationState.Revoked,
                grant.PublicId,
                grant.ExpiresAt,
                true);
        }

        var request = await _context.LearnerConsentRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == grant.ConsentRequestId,
                cancellationToken);
        if (request == null) return new LearnerConsentMutationResult(LearnerConsentMutationState.Conflict);

        if (grant.Status == LearnerDataGrantStatus.Expired || grant.ExpiresAt <= utcNow)
        {
            if (grant.Status == LearnerDataGrantStatus.Active)
            {
                grant.Status = LearnerDataGrantStatus.Expired;
                AddLog(request, userId, LearnerIdentityAccessAction.RevokeDataGrant,
                    LearnerIdentityAccessOutcome.Expired, "DATA_GRANT_EXPIRED", ipAddress, userAgent);
                try
                {
                    await SaveDecisionAsync(request, userId, cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return await ReadGrantAfterConflictAsync(grantReference, userId, cancellationToken);
                }
            }

            return new LearnerConsentMutationResult(
                LearnerConsentMutationState.Expired,
                grant.PublicId,
                grant.ExpiresAt,
                true);
        }

        if (grant.Status != LearnerDataGrantStatus.Active)
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Conflict);

        grant.Status = LearnerDataGrantStatus.Revoked;
        grant.RevokedAt = utcNow;
        grant.RevokedByUserId = userId;
        if (request.Status == LearnerConsentRequestStatus.Approved)
            request.Status = LearnerConsentRequestStatus.Revoked;
        AddLog(request, userId, LearnerIdentityAccessAction.RevokeDataGrant,
            LearnerIdentityAccessOutcome.Revoked, "DATA_GRANT_REVOKED", ipAddress, userAgent);

        try
        {
            await SaveDecisionAsync(request, userId, cancellationToken);
            return new LearnerConsentMutationResult(
                LearnerConsentMutationState.Revoked,
                grant.PublicId,
                grant.ExpiresAt);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ReadGrantAfterConflictAsync(grantReference, userId, cancellationToken);
        }
        catch (DbUpdateException)
        {
            return await ReadGrantAfterConflictAsync(grantReference, userId, cancellationToken);
        }
    }

    private async Task<LearnerConsentRequest?> FindAuthorizedRequestAsync(
        Guid requestReference,
        long userId,
        CancellationToken cancellationToken)
    {
        var request = await _context.LearnerConsentRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.PublicId == requestReference,
                cancellationToken);
        if (request == null) return null;

        return await UserControlsPersonAsync(request.PersonId, userId, cancellationToken)
            ? request
            : null;
    }

    private async Task<bool> UserControlsPersonAsync(
        long personId,
        long userId,
        CancellationToken cancellationToken)
    {
        var directControl = await (
            from link in _context.StudentPersonLinks.IgnoreQueryFilters()
            join student in _context.Students.IgnoreQueryFilters()
                on new { link.StudentId, link.TenantId }
                equals new { StudentId = student.Id, student.TenantId }
            where !link.IsDeleted
                  && !student.IsDeleted
                  && link.PersonId == personId
                  && link.Status == StudentPersonLinkStatus.Active
                  && student.UserId == userId
            select link.Id).AnyAsync(cancellationToken);
        if (directControl) return true;

        return await (
            from link in _context.StudentPersonLinks.IgnoreQueryFilters()
            join guardian in _context.Guardians.IgnoreQueryFilters()
                on new { link.StudentId, link.TenantId }
                equals new { guardian.StudentId, guardian.TenantId }
            where !link.IsDeleted
                  && !guardian.IsDeleted
                  && link.PersonId == personId
                  && link.Status == StudentPersonLinkStatus.Active
                  && guardian.UserId == userId
            select link.Id).AnyAsync(cancellationToken);
    }

    private async Task<long[]> GetControlledPersonIdsAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        var direct =
            from link in _context.StudentPersonLinks.IgnoreQueryFilters()
            join student in _context.Students.IgnoreQueryFilters()
                on new { link.StudentId, link.TenantId }
                equals new { StudentId = student.Id, student.TenantId }
            where !link.IsDeleted
                  && !student.IsDeleted
                  && link.Status == StudentPersonLinkStatus.Active
                  && student.UserId == userId
            select link.PersonId;

        var throughGuardian =
            from link in _context.StudentPersonLinks.IgnoreQueryFilters()
            join guardian in _context.Guardians.IgnoreQueryFilters()
                on new { link.StudentId, link.TenantId }
                equals new { guardian.StudentId, guardian.TenantId }
            where !link.IsDeleted
                  && !guardian.IsDeleted
                  && link.Status == StudentPersonLinkStatus.Active
                  && guardian.UserId == userId
            select link.PersonId;

        return await direct.Concat(throughGuardian)
            .Distinct()
            .ToArrayAsync(cancellationToken);
    }

    private async Task<LearnerConsentMutationResult> ExistingResolutionAsync(
        LearnerConsentRequest request,
        LearnerConsentDecision decision,
        CancellationToken cancellationToken)
    {
        if (request.Status == LearnerConsentRequestStatus.Approved
            && decision == LearnerConsentDecision.Approve)
        {
            var grant = await _context.LearnerDataGrants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.ConsentRequestId == request.Id,
                    cancellationToken);
            return grant == null
                ? new LearnerConsentMutationResult(LearnerConsentMutationState.Conflict)
                : new LearnerConsentMutationResult(
                    LearnerConsentMutationState.Approved,
                    grant.PublicId,
                    grant.ExpiresAt,
                    true);
        }

        if (request.Status == LearnerConsentRequestStatus.Denied
            && decision == LearnerConsentDecision.Deny)
        {
            return new LearnerConsentMutationResult(
                LearnerConsentMutationState.Denied,
                AlreadyProcessed: true);
        }

        if (request.Status == LearnerConsentRequestStatus.Expired)
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Expired, AlreadyProcessed: true);
        if (request.Status == LearnerConsentRequestStatus.Revoked)
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Revoked, AlreadyProcessed: true);

        return new LearnerConsentMutationResult(LearnerConsentMutationState.Conflict, AlreadyProcessed: true);
    }

    private async Task<LearnerConsentMutationResult> ReadAfterConflictAsync(
        Guid requestReference,
        long userId,
        LearnerConsentDecision decision,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        var current = await FindAuthorizedRequestAsync(requestReference, userId, cancellationToken);
        return current == null
            ? NotFound()
            : await ExistingResolutionAsync(current, decision, cancellationToken);
    }

    private async Task<LearnerConsentMutationResult> ReadGrantAfterConflictAsync(
        Guid grantReference,
        long userId,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        var grant = await _context.LearnerDataGrants.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.PublicId == grantReference,
                cancellationToken);
        if (grant == null
            || !await UserControlsPersonAsync(grant.PersonId, userId, cancellationToken))
        {
            return NotFound();
        }

        return grant.Status switch
        {
            LearnerDataGrantStatus.Revoked => new LearnerConsentMutationResult(
                LearnerConsentMutationState.Revoked, grant.PublicId, grant.ExpiresAt, true),
            LearnerDataGrantStatus.Expired => new LearnerConsentMutationResult(
                LearnerConsentMutationState.Expired, grant.PublicId, grant.ExpiresAt, true),
            _ => new LearnerConsentMutationResult(LearnerConsentMutationState.Conflict)
        };
    }

    private Task<int> SaveDecisionAsync(
        LearnerConsentRequest request,
        long userId,
        CancellationToken cancellationToken) =>
        _context.SaveLearnerConsentDecisionAsync(
            request.Id,
            request.TenantId,
            request.PersonId,
            request.RequestedStudentId,
            userId,
            cancellationToken);

    private void AddLog(
        LearnerConsentRequest request,
        long userId,
        LearnerIdentityAccessAction action,
        LearnerIdentityAccessOutcome outcome,
        string reasonCode,
        string? ipAddress,
        string? userAgent)
    {
        _context.LearnerIdentityAccessLogs.Add(new LearnerIdentityAccessLog
        {
            TenantId = request.TenantId,
            PersonId = request.PersonId,
            StudentId = request.RequestedStudentId,
            ConsentRequestId = request.Id,
            UserId = userId,
            Action = action,
            Outcome = outcome,
            Purpose = request.Purpose,
            ReasonCode = reasonCode,
            IpAddress = Truncate(ipAddress, 64),
            UserAgent = Truncate(userAgent, 500)
        });
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed[..Math.Min(trimmed.Length, maxLength)];
    }

    private static LearnerConsentMutationResult NotFound() =>
        new(LearnerConsentMutationState.NotFound);
}
