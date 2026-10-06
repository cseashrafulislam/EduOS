using System.Globalization;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

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
        long userId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var personIds = await GetControlledPersonIdsAsync(userId, cancellationToken);
        if (personIds.Length == 0) return Array.Empty<LearnerConsentRequestRecord>();

        var requests = await _context.LearnerConsentRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => !x.IsDeleted
                && personIds.Contains(x.PersonId)
                && x.State == ConsentState.Pending
                && x.ExpiresAt.HasValue
                && x.ExpiresAt.Value > utcNow)
            .OrderBy(x => x.ExpiresAt)
            .ToListAsync(cancellationToken);

        var tenantIds = requests.Select(x => x.TenantId).Distinct().ToArray();
        var tenants = await _context.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(x => tenantIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return requests.Select(x => new LearnerConsentRequestRecord(
            x.PublicId,
            tenants.TryGetValue(x.TenantId, out var name) ? name : "Institution",
            ParsePurpose(x.Purpose),
            ParseScopes(x.RequestedScopes),
            x.ExpiresAt!.Value)).ToList();
    }

    public async Task<IReadOnlyList<LearnerDataGrantRecord>> GetActiveGrantsForUserAsync(
        long userId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var personIds = await GetControlledPersonIdsAsync(userId, cancellationToken);
        if (personIds.Length == 0) return Array.Empty<LearnerDataGrantRecord>();

        var grants = await _context.LearnerDataGrants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => !x.IsDeleted
                && personIds.Contains(x.PersonId)
                && x.State == GrantState.Active
                && (!x.ExpiresAt.HasValue || x.ExpiresAt.Value > utcNow))
            .OrderBy(x => x.ExpiresAt)
            .ToListAsync(cancellationToken);

        var requestIds = grants.Select(x => x.LearnerConsentRequestId).Distinct().ToArray();
        var requests = await _context.LearnerConsentRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(x => requestIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var tenantIds = grants.Select(x => x.TenantId).Distinct().ToArray();
        var tenants = await _context.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(x => tenantIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return grants.Select(x =>
        {
            requests.TryGetValue(x.LearnerConsentRequestId, out var request);
            return new LearnerDataGrantRecord(
                x.PublicId,
                tenants.TryGetValue(x.TenantId, out var name) ? name : "Institution",
                ParsePurpose(request?.Purpose),
                ParseScopes(x.GrantedScopes),
                x.GrantedAt,
                x.ExpiresAt ?? DateTime.MaxValue);
        }).ToList();
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
        var request = await FindAuthorizedRequestAsync(requestReference, userId, cancellationToken);
        if (request == null) return NotFound();

        if (request.State != ConsentState.Pending)
            return await ExistingResolutionAsync(request, decision, cancellationToken);

        if (!request.ExpiresAt.HasValue || request.ExpiresAt.Value <= utcNow)
        {
            request.State = ConsentState.Expired;
            request.ResolvedAt = utcNow;
            request.ResolvedByUserId = userId;
            AddLog(request, userId, "ResolveConsent", "Expired", "CONSENT_REQUEST_EXPIRED", ipAddress, userAgent);
            return await SaveRequestResultAsync(request, userId, LearnerConsentMutationState.Expired, cancellationToken);
        }

        if (decision == LearnerConsentDecision.Deny)
        {
            request.State = ConsentState.Rejected;
            request.ResolvedAt = utcNow;
            request.ResolvedByUserId = userId;
            AddLog(request, userId, "ResolveConsent", "Denied", "CONSENT_DENIED", ipAddress, userAgent);
            return await SaveRequestResultAsync(request, userId, LearnerConsentMutationState.Denied, cancellationToken);
        }

        var purpose = ParsePurpose(request.Purpose);
        var scopes = ParseScopes(request.RequestedScopes);
        if (!Enum.IsDefined(purpose)
            || scopes == LearnerDataScope.None
            || !scopes.HasFlag(LearnerDataScope.BasicIdentity)
            || (scopes & ~AllowedScopes) != 0
            || grantExpiresAt <= utcNow
            || grantExpiresAt > utcNow.AddYears(10))
        {
            AddLog(request, userId, "ResolveConsent", "Conflict", "CONSENT_SCOPE_INVALID", ipAddress, userAgent);
            await SaveDecisionAsync(request, userId, cancellationToken);
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Conflict);
        }

        var currentLink = await _context.StudentPersonLinks.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => !x.IsDeleted
                && x.TenantId == request.TenantId
                && x.StudentId == request.RequestedStudentId
                && x.UnlinkedAt == null, cancellationToken);
        if (currentLink != null && currentLink.PersonId != request.PersonId)
        {
            AddLog(request, userId, "ResolveConsent", "Conflict", "TARGET_STUDENT_LINK_CONFLICT", ipAddress, userAgent);
            await SaveDecisionAsync(request, userId, cancellationToken);
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Conflict);
        }

        var existingGrant = await _context.LearnerDataGrants.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.LearnerConsentRequestId == request.Id, cancellationToken);
        if (existingGrant != null)
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Conflict);

        if (currentLink == null)
        {
            await _context.StudentPersonLinks.AddAsync(new StudentPersonLink
            {
                TenantId = request.TenantId,
                StudentId = request.RequestedStudentId,
                PersonId = request.PersonId,
                IsPrimary = true,
                LinkedAt = utcNow,
                LinkedByUserId = userId,
                LinkReason = "Approved learner identity consent"
            }, cancellationToken);
        }

        var grant = new LearnerDataGrant
        {
            TenantId = request.TenantId,
            PublicId = Guid.NewGuid(),
            LearnerConsentRequestId = request.Id,
            PersonId = request.PersonId,
            StudentId = request.RequestedStudentId,
            GrantedToUserId = request.RequestedByUserId,
            GrantedScopes = SerializeScopes(scopes),
            State = GrantState.Active,
            GrantedAt = utcNow,
            ExpiresAt = grantExpiresAt
        };
        await _context.LearnerDataGrants.AddAsync(grant, cancellationToken);

        request.State = ConsentState.Approved;
        request.ResolvedAt = utcNow;
        request.ResolvedByUserId = userId;
        AddLog(request, userId, "ResolveConsent", "Approved", "CONSENT_APPROVED", ipAddress, userAgent);

        try
        {
            await SaveDecisionAsync(request, userId, cancellationToken);
            return new LearnerConsentMutationResult(
                LearnerConsentMutationState.Approved, grant.PublicId, grant.ExpiresAt);
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
        var grant = await _context.LearnerDataGrants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.PublicId == grantReference, cancellationToken);
        if (grant == null || !await UserControlsPersonAsync(grant.PersonId, userId, cancellationToken))
            return NotFound();

        if (grant.State == GrantState.Revoked)
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Revoked, grant.PublicId, grant.ExpiresAt, true);

        var request = await _context.LearnerConsentRequests.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == grant.LearnerConsentRequestId, cancellationToken);
        if (request == null)
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Conflict);

        if (grant.State == GrantState.Expired || (grant.ExpiresAt.HasValue && grant.ExpiresAt.Value <= utcNow))
        {
            if (grant.State == GrantState.Active)
            {
                grant.State = GrantState.Expired;
                AddLog(request, userId, "RevokeDataGrant", "Expired", "DATA_GRANT_EXPIRED", ipAddress, userAgent);
                await SaveDecisionAsync(request, userId, cancellationToken);
            }
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Expired, grant.PublicId, grant.ExpiresAt, true);
        }

        if (grant.State != GrantState.Active)
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Conflict);

        grant.State = GrantState.Revoked;
        grant.RevokedAt = utcNow;
        grant.RevokedByUserId = userId;
        if (request.State == ConsentState.Approved) request.State = ConsentState.Revoked;
        AddLog(request, userId, "RevokeDataGrant", "Revoked", "DATA_GRANT_REVOKED", ipAddress, userAgent);

        try
        {
            await SaveDecisionAsync(request, userId, cancellationToken);
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Revoked, grant.PublicId, grant.ExpiresAt);
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
        Guid requestReference, long userId, CancellationToken cancellationToken)
    {
        var request = await _context.LearnerConsentRequests.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.PublicId == requestReference, cancellationToken);
        return request != null && await UserControlsPersonAsync(request.PersonId, userId, cancellationToken)
            ? request : null;
    }

    private async Task<bool> UserControlsPersonAsync(long personId, long userId, CancellationToken cancellationToken)
    {
        if (await _context.Students.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.PersonId == personId && x.UserId == userId && x.IsActive, cancellationToken))
            return true;

        return await (
            from sg in _context.StudentGuardians.IgnoreQueryFilters().AsNoTracking()
            join student in _context.Students.IgnoreQueryFilters().AsNoTracking()
                on new { sg.TenantId, Id = sg.StudentId } equals new { student.TenantId, Id = student.Id }
            join guardian in _context.Guardians.IgnoreQueryFilters().AsNoTracking()
                on new { sg.TenantId, Id = sg.GuardianId } equals new { guardian.TenantId, Id = guardian.Id }
            where !sg.IsDeleted && !student.IsDeleted && !guardian.IsDeleted
                && student.PersonId == personId && guardian.UserId == userId && guardian.IsActive
            select sg.Id).AnyAsync(cancellationToken);
    }

    private async Task<long[]> GetControlledPersonIdsAsync(long userId, CancellationToken cancellationToken)
    {
        var direct = _context.Students.IgnoreQueryFilters().AsNoTracking()
            .Where(x => !x.IsDeleted && x.UserId == userId && x.IsActive)
            .Select(x => x.PersonId);

        var guardian =
            from sg in _context.StudentGuardians.IgnoreQueryFilters().AsNoTracking()
            join student in _context.Students.IgnoreQueryFilters().AsNoTracking()
                on new { sg.TenantId, Id = sg.StudentId } equals new { student.TenantId, Id = student.Id }
            join g in _context.Guardians.IgnoreQueryFilters().AsNoTracking()
                on new { sg.TenantId, Id = sg.GuardianId } equals new { g.TenantId, Id = g.Id }
            where !sg.IsDeleted && !student.IsDeleted && !g.IsDeleted && g.UserId == userId && g.IsActive
            select student.PersonId;

        return await direct.Concat(guardian).Distinct().ToArrayAsync(cancellationToken);
    }

    private async Task<LearnerConsentMutationResult> ExistingResolutionAsync(
        LearnerConsentRequest request, LearnerConsentDecision decision, CancellationToken cancellationToken)
    {
        if (request.State == ConsentState.Approved && decision == LearnerConsentDecision.Approve)
        {
            var grant = await _context.LearnerDataGrants.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.LearnerConsentRequestId == request.Id, cancellationToken);
            return grant == null
                ? new LearnerConsentMutationResult(LearnerConsentMutationState.Conflict)
                : new LearnerConsentMutationResult(LearnerConsentMutationState.Approved, grant.PublicId, grant.ExpiresAt, true);
        }
        if (request.State == ConsentState.Rejected && decision == LearnerConsentDecision.Deny)
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Denied, AlreadyProcessed: true);
        if (request.State == ConsentState.Expired)
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Expired, AlreadyProcessed: true);
        if (request.State == ConsentState.Revoked)
            return new LearnerConsentMutationResult(LearnerConsentMutationState.Revoked, AlreadyProcessed: true);
        return new LearnerConsentMutationResult(LearnerConsentMutationState.Conflict, AlreadyProcessed: true);
    }

    private async Task<LearnerConsentMutationResult> ReadAfterConflictAsync(
        Guid requestReference, long userId, LearnerConsentDecision decision, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        var request = await FindAuthorizedRequestAsync(requestReference, userId, cancellationToken);
        return request == null ? NotFound() : await ExistingResolutionAsync(request, decision, cancellationToken);
    }

    private async Task<LearnerConsentMutationResult> ReadGrantAfterConflictAsync(
        Guid grantReference, long userId, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        var grant = await _context.LearnerDataGrants.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.PublicId == grantReference, cancellationToken);
        if (grant == null || !await UserControlsPersonAsync(grant.PersonId, userId, cancellationToken))
            return NotFound();
        return grant.State switch
        {
            GrantState.Revoked => new LearnerConsentMutationResult(LearnerConsentMutationState.Revoked, grant.PublicId, grant.ExpiresAt, true),
            GrantState.Expired => new LearnerConsentMutationResult(LearnerConsentMutationState.Expired, grant.PublicId, grant.ExpiresAt, true),
            _ => new LearnerConsentMutationResult(LearnerConsentMutationState.Conflict)
        };
    }

    private async Task<LearnerConsentMutationResult> SaveRequestResultAsync(
        LearnerConsentRequest request, long userId, LearnerConsentMutationState state, CancellationToken cancellationToken)
    {
        try
        {
            await SaveDecisionAsync(request, userId, cancellationToken);
            return new LearnerConsentMutationResult(state);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ReadAfterConflictAsync(
                request.PublicId, userId,
                state == LearnerConsentMutationState.Denied ? LearnerConsentDecision.Deny : LearnerConsentDecision.Approve,
                cancellationToken);
        }
    }

    private Task<int> SaveDecisionAsync(LearnerConsentRequest request, long userId, CancellationToken cancellationToken) =>
        _context.SaveLearnerConsentDecisionAsync(
            request.Id, request.TenantId, request.PersonId, request.RequestedStudentId, userId, cancellationToken);

    private void AddLog(
        LearnerConsentRequest request,
        long userId,
        string action,
        string outcome,
        string reasonCode,
        string? ipAddress,
        string? userAgent)
    {
        _context.LearnerIdentityAccessLogs.Add(new LearnerIdentityAccessLog
        {
            TenantId = request.TenantId,
            PersonId = request.PersonId,
            StudentId = request.RequestedStudentId,
            LearnerConsentRequestId = request.Id,
            UserId = userId,
            Action = action,
            OutcomeCode = outcome,
            ReasonCode = reasonCode,
            Purpose = request.Purpose,
            AccessedAt = DateTime.UtcNow,
            IpAddress = Truncate(ipAddress, 100),
            UserAgent = Truncate(userAgent, 500)
        });
    }

    private static LearnerIdentityPurpose ParsePurpose(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            if (Enum.TryParse<LearnerIdentityPurpose>(value, true, out var named) && Enum.IsDefined(named)) return named;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric)
                && Enum.IsDefined((LearnerIdentityPurpose)numeric))
                return (LearnerIdentityPurpose)numeric;
        }
        return 0;
    }

    private static LearnerDataScope ParseScopes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return LearnerDataScope.None;
        if (Enum.TryParse<LearnerDataScope>(value, true, out var named)) return named;
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric)
            ? (LearnerDataScope)numeric : LearnerDataScope.None;
    }

    private static string SerializeScopes(LearnerDataScope scopes) =>
        ((long)scopes).ToString(CultureInfo.InvariantCulture);

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed[..Math.Min(trimmed.Length, maxLength)];
    }

    private static LearnerConsentMutationResult NotFound() =>
        new(LearnerConsentMutationState.NotFound);
}
