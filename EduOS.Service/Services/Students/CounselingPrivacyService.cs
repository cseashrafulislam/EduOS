using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Students;

/// <summary>Independently approved campus policy and irreversible, evidence-preserving
/// case redaction. No unreviewed automated purge, case export or confidential analytics.</summary>
public sealed class CounselingPrivacyService(EduOSDbContext db, ICurrentUserService user,
    IDataProtectionProvider protection)
{
    private static ApiResponse<T> Fail<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
    private async Task<bool> Admin(CancellationToken ct) => user.IsTenantAdmin &&
        await CentralPrivilegedAccess.HasRoleAsync(db, user, new[] { "TENANTADMIN" }, ct);
    private async Task<bool> Officer(CancellationToken ct) => user.IsInRole("SafeguardingOfficer") &&
        await CentralPrivilegedAccess.HasRoleAsync(db, user, new[] { "SAFEGUARDINGOFFICER" }, ct);
    private async Task<bool> Counselor(CancellationToken ct) => user.IsInRole("Counselor") &&
        await CentralPrivilegedAccess.HasRoleAsync(db, user, new[] { "COUNSELOR" }, ct);
    private async Task<bool> Campus(long campusId, CancellationToken ct) =>
        await db.Campuses.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            x.Id == campusId && x.IsActive && !x.IsDeleted, ct);
    private async Task<bool> CampusGrant(long campusId, CancellationToken ct) =>
        await db.UserCampusAccesses.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            x.CampusId == campusId && x.UserId == user.UserId && x.IsActive && !x.IsDeleted, ct);
    private static bool Same(byte[] current, string? supplied)
    {
        try { return !string.IsNullOrEmpty(supplied) &&
            current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static CounselingCampusPolicyRow Map(CounselingCampusPolicy x) => new()
    {
        CampusId = x.CampusId, RetentionDays = x.RetentionDays, LegalHold = x.LegalHold,
        EmergencyGuidanceEn = x.EmergencyGuidanceEn, EmergencyGuidanceBn = x.EmergencyGuidanceBn,
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };
    private static CounselingPolicyChangeRow Map(CounselingPolicyChange x) => new()
    {
        Reference = x.PublicId, CampusId = x.CampusId, State = x.State,
        RetentionDays = x.RetentionDays, LegalHold = x.LegalHold,
        EmergencyGuidanceEn = x.EmergencyGuidanceEn, EmergencyGuidanceBn = x.EmergencyGuidanceBn,
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };
    private static bool Valid(ProposeCounselingPolicyRequest r) => r.RequestId != Guid.Empty &&
        r.CampusId > 0 && r.RetentionDays is >= 30 and <= 3650 &&
        r.EmergencyGuidanceEn?.Trim().Length is >= 10 and <= 350 &&
        r.EmergencyGuidanceBn?.Trim().Length is >= 10 and <= 350 &&
        r.Reason?.Trim().Length is >= 10 and <= 500;

    public async Task<ApiResponse<IReadOnlyList<CounselingCampusOption>>> CampusesAsync(
        CancellationToken ct = default)
    {
        var admin = await Admin(ct);
        if (!admin && !await Officer(ct) && !await Counselor(ct))
            return Fail<IReadOnlyList<CounselingCampusOption>>("Safeguarding role required.", 403);
        var query = db.Campuses.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.IsActive && !x.IsDeleted);
        if (!admin)
            query = query.Where(x => db.UserCampusAccesses.Any(a => a.TenantId == user.TenantId &&
                a.UserId == user.UserId && a.CampusId == x.Id && a.IsActive && !a.IsDeleted));
        var results = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Take(300)
            .Select(x => new CounselingCampusOption { CampusId = x.Id, CampusName = x.Name })
            .ToArrayAsync(ct);
        return ApiResponse<IReadOnlyList<CounselingCampusOption>>.SuccessResponse(results);
    }

    public async Task<ApiResponse<CounselingCampusPolicyRow>> PolicyAsync(long campusId,
        CancellationToken ct = default)
    {
        if (!await Admin(ct) && !((await Officer(ct) || await Counselor(ct)) &&
            await CampusGrant(campusId, ct)))
            return Fail<CounselingCampusPolicyRow>("Safeguarding role and campus required.", 403);
        var policy = await db.CounselingCampusPolicies.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.CampusId == campusId && !x.IsDeleted, ct);
        return policy == null ? Fail<CounselingCampusPolicyRow>(
            "Campus policy has not been independently approved.", 404) :
            ApiResponse<CounselingCampusPolicyRow>.SuccessResponse(Map(policy));
    }

    public async Task<ApiResponse<CounselingPolicyChangeRow>> ProposeAsync(
        ProposeCounselingPolicyRequest request, CancellationToken ct = default)
    {
        if (!await Admin(ct)) return Fail<CounselingPolicyChangeRow>("Active tenant administrator required.", 403);
        if (request == null || !Valid(request))
            return Fail<CounselingPolicyChangeRow>("Valid campus, policy and 10–500 character reason required.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (!await Admin(token) || !await Campus(request.CampusId, token))
                    return Fail<CounselingPolicyChangeRow>("Campus or administrator changed.", 403);
                var previous = await db.CounselingPolicyChanges.FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == request.RequestId, token);
                if (previous != null)
                    return previous.ProposedByUserId == user.UserId && previous.CampusId == request.CampusId &&
                        previous.RetentionDays == request.RetentionDays && previous.LegalHold == request.LegalHold &&
                        previous.EmergencyGuidanceEn == request.EmergencyGuidanceEn.Trim() &&
                        previous.EmergencyGuidanceBn == request.EmergencyGuidanceBn.Trim() &&
                        previous.Reason == request.Reason.Trim() ?
                        ApiResponse<CounselingPolicyChangeRow>.SuccessResponse(Map(previous)) :
                        Fail<CounselingPolicyChangeRow>("Policy request key reused.", 409);
                var current = await db.CounselingCampusPolicies.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.CampusId == request.CampusId && !x.IsDeleted, token);
                if ((current == null && !string.IsNullOrWhiteSpace(request.ExpectedPolicyRowVersion)) ||
                    (current != null && !Same(current.RowVersion, request.ExpectedPolicyRowVersion)))
                    return Fail<CounselingPolicyChangeRow>("Campus policy changed; reload.", 409);
                if (await db.CounselingPolicyChanges.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.CampusId == request.CampusId &&
                    x.State == "Submitted" && !x.IsDeleted, token))
                    return Fail<CounselingPolicyChangeRow>("This campus already has a pending policy proposal.", 409);
                var proposal = new CounselingPolicyChange
                {
                    TenantId = user.TenantId, CampusId = request.CampusId,
                    ClientRequestId = request.RequestId, RetentionDays = request.RetentionDays,
                    LegalHold = request.LegalHold, EmergencyGuidanceEn = request.EmergencyGuidanceEn.Trim(),
                    EmergencyGuidanceBn = request.EmergencyGuidanceBn.Trim(),
                    ExpectedPolicyVersion = current?.RowVersion?.ToArray(),
                    Reason = request.Reason.Trim(), ProposedByUserId = user.UserId,
                    ProposedAtUtc = DateTime.UtcNow, CreatedBy = user.UserId
                };
                db.CounselingPolicyChanges.Add(proposal);
                await db.SaveChangesAsync(token);
                return ApiResponse<CounselingPolicyChangeRow>.SuccessResponse(Map(proposal));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<CounselingPolicyChangeRow>("Policy changed; reload.", 409); }
        catch (DbUpdateException) { return Fail<CounselingPolicyChangeRow>("Concurrent campus policy proposal.", 409); }
    }

    public async Task<ApiResponse<IReadOnlyList<CounselingPolicyChangeRow>>> PendingAsync(
        CancellationToken ct = default)
    {
        if (!await Admin(ct))
            return Fail<IReadOnlyList<CounselingPolicyChangeRow>>("Tenant administrator required.", 403);
        var pending = await db.CounselingPolicyChanges.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.State == "Submitted" && !x.IsDeleted)
            .OrderBy(x => x.Id).Take(100).ToArrayAsync(ct);
        return ApiResponse<IReadOnlyList<CounselingPolicyChangeRow>>.SuccessResponse(
            pending.Select(Map).ToArray());
    }

    public async Task<ApiResponse<CounselingPolicyChangeRow>> ReviewAsync(Guid reference,
        ReviewCounselingPolicyRequest request, CancellationToken ct = default)
    {
        if (!await Admin(ct)) return Fail<CounselingPolicyChangeRow>("Independent tenant administrator required.", 403);
        if (reference == Guid.Empty || request == null || request.RequestId == Guid.Empty ||
            request.Reason?.Trim().Length is not (>= 10 and <= 500))
            return Fail<CounselingPolicyChangeRow>("Independent decision reference and reason required.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (!await Admin(token)) return Fail<CounselingPolicyChangeRow>("Role revoked.", 403);
                var proposed = await db.CounselingPolicyChanges.FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, token);
                if (proposed == null) return Fail<CounselingPolicyChangeRow>("Policy proposal unavailable.", 404);
                if (proposed.ReviewRequestId == request.RequestId &&
                    proposed.State == (request.Approve ? "Approved" : "Rejected") &&
                    proposed.ReviewedByUserId == user.UserId && proposed.ReviewReason == request.Reason.Trim())
                    return ApiResponse<CounselingPolicyChangeRow>.SuccessResponse(Map(proposed));
                if (proposed.State != "Submitted" || proposed.ProposedByUserId == user.UserId ||
                    !Same(proposed.RowVersion, request.RowVersion) || !await Campus(proposed.CampusId, token) ||
                    await db.CounselingPolicyChanges.AsNoTracking().AnyAsync(x =>
                        x.TenantId == user.TenantId && x.ReviewRequestId == request.RequestId, token))
                    return Fail<CounselingPolicyChangeRow>("Independent review of current policy required.", 409);
                if (request.Approve)
                {
                    var current = await db.CounselingCampusPolicies.FirstOrDefaultAsync(x =>
                        x.TenantId == user.TenantId && x.CampusId == proposed.CampusId && !x.IsDeleted, token);
                    if ((current == null && proposed.ExpectedPolicyVersion != null) ||
                        (current != null && (proposed.ExpectedPolicyVersion == null ||
                            !current.RowVersion.AsSpan().SequenceEqual(proposed.ExpectedPolicyVersion))))
                        return Fail<CounselingPolicyChangeRow>("Policy base version changed; reject and resubmit.", 409);
                    if (current == null)
                    {
                        current = new CounselingCampusPolicy
                        {
                            TenantId = user.TenantId, CampusId = proposed.CampusId,
                            CreatedBy = user.UserId
                        };
                        db.CounselingCampusPolicies.Add(current);
                    }
                    current.RetentionDays = proposed.RetentionDays;
                    current.LegalHold = proposed.LegalHold;
                    current.EmergencyGuidanceEn = proposed.EmergencyGuidanceEn;
                    current.EmergencyGuidanceBn = proposed.EmergencyGuidanceBn;
                    current.ApprovedByUserId = user.UserId;
                    current.ApprovedAtUtc = DateTime.UtcNow;
                    current.UpdatedAt = DateTime.UtcNow; current.UpdatedBy = user.UserId;
                }
                proposed.State = request.Approve ? "Approved" : "Rejected";
                proposed.ReviewRequestId = request.RequestId;
                proposed.ReviewedByUserId = user.UserId;
                proposed.ReviewedAtUtc = DateTime.UtcNow;
                proposed.ReviewReason = request.Reason.Trim();
                proposed.UpdatedAt = DateTime.UtcNow; proposed.UpdatedBy = user.UserId;
                await db.SaveChangesAsync(token);
                return ApiResponse<CounselingPolicyChangeRow>.SuccessResponse(Map(proposed));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<CounselingPolicyChangeRow>("Concurrent policy review.", 409); }
        catch (DbUpdateException) { return Fail<CounselingPolicyChangeRow>("Policy approval conflict.", 409); }
    }

    private async Task<bool> EligibleForRedaction(CounselingCase row, CancellationToken ct)
    {
        var policy = await db.CounselingCampusPolicies.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.CampusId == row.CampusId && !x.IsDeleted, ct);
        return policy != null && !policy.LegalHold && policy.RetentionDays is >= 30 and <= 3650 &&
            row.State == "Closed" && row.ClosedAtUtc.HasValue &&
            row.ClosedAtUtc.Value.AddDays(policy.RetentionDays) <= DateTime.UtcNow &&
            !row.RedactedAtUtc.HasValue;
    }

    public async Task<ApiResponse<CounselingCaseRow>> RequestRedactionAsync(Guid reference,
        CounselingPrivacyCommandRequest request, CancellationToken ct = default)
    {
        if (!await Officer(ct) && !await Counselor(ct))
            return Fail<CounselingCaseRow>("Safeguarding officer or assigned counselor required.", 403);
        if (reference == Guid.Empty || request == null || request.RequestId == Guid.Empty ||
            request.Reason?.Trim().Length is not (>= 10 and <= 500))
            return Fail<CounselingCaseRow>("Reason and request reference required.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var officer = await Officer(token);
                var counselor = await Counselor(token);
                var row = await db.CounselingCases.FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, token);
                if (row == null) return Fail<CounselingCaseRow>("Case unavailable.", 404);
                if (!await CampusGrant(row.CampusId, token) ||
                    !(officer || (counselor && row.AssignedCounselorUserId == user.UserId)))
                    return Fail<CounselingCaseRow>("Case or campus access denied.", 403);
                var studentRef = await db.Students.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.Id == row.StudentId).Select(x => x.PublicId)
                    .FirstOrDefaultAsync(token);
                var prior = await db.CounselingCaseEvents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == request.RequestId, token);
                if (prior != null)
                    return prior.Action == "RedactRequested" && prior.CounselingCaseId == row.Id &&
                        prior.ActorUserId == user.UserId && row.RedactionRequestId == request.RequestId ?
                        ApiResponse<CounselingCaseRow>.SuccessResponse(Map(row, studentRef)) :
                        Fail<CounselingCaseRow>("Redaction request key reused.", 409);
                if (!Version(row.RowVersion, request.RowVersion) ||
                    row.RedactionRequestId.HasValue || !await EligibleForRedaction(row, token))
                    return Fail<CounselingCaseRow>("Closed, retention-expired, unheld case required.", 409);
                var now = DateTime.UtcNow;
                row.RedactionRequestId = request.RequestId;
                row.RedactionRequestedByUserId = user.UserId;
                row.RedactionRequestedAtUtc = now;
                row.UpdatedAt = now; row.UpdatedBy = user.UserId;
                db.CounselingCaseEvents.Add(new CounselingCaseEvent
                {
                    TenantId = user.TenantId, CounselingCaseId = row.Id,
                    ClientRequestId = request.RequestId, Action = "RedactRequested",
                    ProtectedNote = protection.CreateProtector("EduOS.StudentSupport.PrivateCase.v1",
                        user.TenantId.ToString()).Protect(request.Reason.Trim()),
                    ActorUserId = user.UserId, OccurredAtUtc = now, CreatedBy = user.UserId
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<CounselingCaseRow>.SuccessResponse(Map(row, studentRef));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<CounselingCaseRow>("Concurrent redaction request.", 409); }
        catch (DbUpdateException) { return Fail<CounselingCaseRow>("Redaction request conflict.", 409); }
    }

    private static bool Version(byte[] current, string? version) => Same(current, version);

    public async Task<ApiResponse<IReadOnlyList<CounselingCaseRow>>> PendingRedactionsAsync(
        CancellationToken ct = default)
    {
        if (!await Admin(ct))
            return Fail<IReadOnlyList<CounselingCaseRow>>("Independent privacy reviewer required.", 403);
        var rows = await db.CounselingCases.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.RedactionRequestId != null && x.RedactedAtUtc == null && !x.IsDeleted)
            .OrderBy(x => x.RedactionRequestedAtUtc).Take(100).ToArrayAsync(ct);
        // The administrative queue contains only opaque references and status; no learner identifiers.
        return ApiResponse<IReadOnlyList<CounselingCaseRow>>.SuccessResponse(
            rows.Select(x => Map(x, Guid.Empty)).ToArray());
    }
    private static CounselingCaseRow Map(CounselingCase row, Guid studentRef) => new()
    {
        Reference = row.PublicId, StudentReference = studentRef, CampusId = row.CampusId,
        State = row.State, IsUrgent = row.IsUrgent, OpenedAtUtc = row.OpenedAtUtc,
        GuardianContactConsented = row.GuardianContactConsented,
        AssignedCounselorUserId = row.AssignedCounselorUserId,
        RedactionPending = row.RedactionRequestId.HasValue && !row.RedactedAtUtc.HasValue,
        IsRedacted = row.RedactedAtUtc.HasValue, RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    public async Task<ApiResponse<CounselingCaseRow>> ReviewRedactionAsync(Guid reference,
        CounselingPrivacyCommandRequest request, CancellationToken ct = default)
    {
        if (!await Admin(ct)) return Fail<CounselingCaseRow>("Independent privacy reviewer required.", 403);
        if (reference == Guid.Empty || request == null || request.RequestId == Guid.Empty ||
            request.Reason?.Trim().Length is not (>= 10 and <= 500))
            return Fail<CounselingCaseRow>("Review reference and reason required.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (!await Admin(token)) return Fail<CounselingCaseRow>("Role revoked.", 403);
                var row = await db.CounselingCases.FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, token);
                if (row == null) return Fail<CounselingCaseRow>("Case unavailable.", 404);
                var studentRef = await db.Students.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.Id == row.StudentId).Select(x => x.PublicId)
                    .FirstOrDefaultAsync(token);
                var prior = await db.CounselingCaseEvents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == request.RequestId, token);
                if (prior != null)
                    return prior.CounselingCaseId == row.Id && prior.ActorUserId == user.UserId &&
                        prior.Action == (request.Approve ? "RedactApproved" : "RedactRejected") ?
                        ApiResponse<CounselingCaseRow>.SuccessResponse(Map(row, studentRef)) :
                        Fail<CounselingCaseRow>("Redaction review key reused.", 409);
                if (!row.RedactionRequestId.HasValue || row.RedactionRequestedByUserId == user.UserId ||
                    !Version(row.RowVersion, request.RowVersion) ||
                    (request.Approve && !await EligibleForRedaction(row, token)))
                    return Fail<CounselingCaseRow>("Independent review of retained, unheld case required.", 409);
                var now = DateTime.UtcNow;
                if (request.Approve)
                {
                    var events = await db.CounselingCaseEvents.Where(x => x.TenantId == user.TenantId &&
                        x.CounselingCaseId == row.Id).ToArrayAsync(token);
                    foreach (var item in events) item.ProtectedNote = "";
                    row.ProtectedSummary = "";
                    row.RedactedAtUtc = now;
                    row.RedactedByUserId = user.UserId;
                }
                row.RedactionRequestId = null;
                row.RedactionRequestedByUserId = null;
                row.RedactionRequestedAtUtc = null;
                row.UpdatedAt = now; row.UpdatedBy = user.UserId;
                db.CounselingCaseEvents.Add(new CounselingCaseEvent
                {
                    TenantId = user.TenantId, CounselingCaseId = row.Id,
                    ClientRequestId = request.RequestId,
                    Action = request.Approve ? "RedactApproved" : "RedactRejected",
                    ProtectedNote = "", ActorUserId = user.UserId,
                    OccurredAtUtc = now, CreatedBy = user.UserId
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<CounselingCaseRow>.SuccessResponse(Map(row, studentRef));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<CounselingCaseRow>("Concurrent privacy review.", 409); }
        catch (DbUpdateException) { return Fail<CounselingCaseRow>("Privacy decision conflict.", 409); }
    }
}
