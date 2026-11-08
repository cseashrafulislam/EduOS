using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Students;

/// <summary>Privacy-first nonclinical educational safeguarding. No general-purpose
/// teacher/principal/admin access to private notes or exports.</summary>
public sealed class CounselingWorkflowService(
    EduOSDbContext db, ICurrentUserService user, IDataProtectionProvider protection)
{
    private bool Scoped => user.IsAuthenticated && user.UserId > 0 && user.TenantId > 0 &&
        db.CurrentTenantId == user.TenantId;
    private bool Officer => Scoped && user.IsInRole("SafeguardingOfficer");
    private bool Counselor => Scoped && user.IsInRole("Counselor");
    private static ApiResponse<T> Fail<T>(string reason, int status = 400) =>
        ApiResponse<T>.ErrorResponse(reason, status);
    private string Protect(string text) => protection.CreateProtector(
        "EduOS.StudentSupport.PrivateCase.v1", user.TenantId.ToString()).Protect(text);
    private string Unprotect(string text) => protection.CreateProtector(
        "EduOS.StudentSupport.PrivateCase.v1", user.TenantId.ToString()).Unprotect(text);
    private static bool Version(byte[] actual, string? token)
    {
        try { return !string.IsNullOrWhiteSpace(token) &&
            actual.AsSpan().SequenceEqual(Convert.FromBase64String(token)); }
        catch (FormatException) { return false; }
    }
    private IQueryable<CounselingCase> Cases => db.CounselingCases.Where(x =>
        x.TenantId == user.TenantId && !x.IsDeleted);
    private static CounselingCaseRow Map(CounselingCase x, Guid student) => new()
    {
        Reference = x.PublicId, StudentReference = student, CampusId = x.CampusId,
        State = x.State, IsUrgent = x.IsUrgent, OpenedAtUtc = x.OpenedAtUtc,
        AssignedCounselorUserId = x.AssignedCounselorUserId,
        GuardianContactConsented = x.GuardianContactConsented,
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };
    private bool HasPrivateAccess(CounselingCase row) =>
        (Counselor && row.AssignedCounselorUserId == user.UserId) ||
        (Officer && row.IsUrgent && (row.State == "Requested" || row.State == "Escalated"));
    private async Task<bool> OwnsStudent(long studentId, CancellationToken ct)
    {
        if (await db.Students.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            x.Id == studentId && x.UserId == user.UserId && !x.IsDeleted, ct)) return true;
        // Guardian association is not permission to read confidential notes.
        return await (from link in db.StudentGuardians.AsNoTracking()
            join guardian in db.Guardians.AsNoTracking()
                on new { link.TenantId, Id = link.GuardianId }
                equals new { guardian.TenantId, guardian.Id }
            where link.TenantId == user.TenantId && guardian.TenantId == user.TenantId &&
                link.StudentId == studentId && !link.IsDeleted && !guardian.IsDeleted &&
                guardian.IsActive && guardian.UserId == user.UserId
            select link.Id).AnyAsync(ct);
    }
    private async Task<bool> CampusAccess(long campusId, long actorId, CancellationToken ct) =>
        await db.UserCampusAccesses.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            x.UserId == actorId && x.CampusId == campusId && x.IsActive && !x.IsDeleted, ct);

    public async Task<ApiResponse<CounselingCaseRow>> OpenAsync(
        OpenCounselingCaseRequest request, CancellationToken ct = default)
    {
        if (!Scoped) return Fail<CounselingCaseRow>("Authentication required.", 403);
        if (request == null || request.RequestId == Guid.Empty ||
            request.StudentReference == Guid.Empty || string.IsNullOrWhiteSpace(request.PrivateSummary) ||
            request.PrivateSummary.Trim().Length is < 10 or > 1500)
            return Fail<CounselingCaseRow>("Provide a clear private support request.");
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == request.StudentReference &&
            !x.IsDeleted && x.StatusCode == "Active", ct);
        if (student == null || (!Officer && !await OwnsStudent(student.Id, ct)))
            return Fail<CounselingCaseRow>("Student not accessible.", 404);
        var enrollment = await db.StudentEnrollments.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.StudentId == student.Id &&
            x.IsCurrent && !x.IsDeleted).OrderByDescending(x => x.Id)
            .Select(x => new { x.CampusId }).FirstOrDefaultAsync(ct);
        if (enrollment == null) return Fail<CounselingCaseRow>("No active campus enrollment.", 409);
        if (Officer && !await CampusAccess(enrollment.CampusId, user.UserId, ct))
            return Fail<CounselingCaseRow>("Campus not accessible.", 403);
        var guardianConsented = request.GuardianContactConsented &&
            (user.IsInRole("Guardian") || user.IsInRole("Parent")) &&
            await (from link in db.StudentGuardians.AsNoTracking()
                join guardian in db.Guardians.AsNoTracking()
                    on new { link.TenantId, Id = link.GuardianId }
                    equals new { guardian.TenantId, guardian.Id }
                where link.TenantId == user.TenantId && guardian.TenantId == user.TenantId &&
                    link.StudentId == student.Id && !link.IsDeleted && !guardian.IsDeleted &&
                    guardian.IsActive && guardian.UserId == user.UserId
                select link.Id).AnyAsync(ct);
        var replay = await Cases.AsNoTracking().FirstOrDefaultAsync(x =>
            x.ClientRequestId == request.RequestId, ct);
        if (replay != null)
            return replay.StudentId == student.Id && replay.OpenedByUserId == user.UserId &&
                replay.IsUrgent == request.Urgent &&
                Unprotect(replay.ProtectedSummary) == request.PrivateSummary.Trim() &&
                replay.GuardianContactConsented == guardianConsented
                ? ApiResponse<CounselingCaseRow>.SuccessResponse(Map(replay, student.PublicId))
                : Fail<CounselingCaseRow>("Request key already used.", 409);
        // Do not accept support requests into an unmonitored safeguarding worklist.
        if (!Officer)
        {
            var staffed = await (from access in db.UserCampusAccesses.AsNoTracking()
                join membership in db.TenantMemberships.AsNoTracking()
                    on new { access.TenantId, access.UserId }
                    equals new { membership.TenantId, membership.UserId }
                join staff in db.Users.AsNoTracking() on access.UserId equals staff.Id
                join assignment in db.UserRoles.AsNoTracking() on access.UserId equals assignment.UserId
                join role in db.Roles.AsNoTracking() on assignment.RoleId equals role.Id
                where access.TenantId == user.TenantId && access.CampusId == enrollment.CampusId &&
                    access.IsActive && !access.IsDeleted && membership.Status ==
                        EduOS.Core.Enums.Domain.MembershipStatus.Active && !membership.IsDeleted &&
                    staff.IsActive && !staff.IsDeleted &&
                    role.NormalizedName == "SAFEGUARDINGOFFICER" && role.IsActive && !role.IsDeleted &&
                    (role.TenantId == user.TenantId ||
                        (role.TenantId == null && db.TenantMemberships.Count(m =>
                            m.UserId == access.UserId && !m.IsDeleted &&
                            m.Status != EduOS.Core.Enums.Domain.MembershipStatus.Left) == 1))
                select access.UserId).AnyAsync(ct);
            if (!staffed)
                return Fail<CounselingCaseRow>("Safeguarding intake is not staffed for this campus. Contact the institution directly.", 409);
        }
        // Sensitive text is never logged or copied to generic audit/outbox payloads.
        var protectedSummary = Protect(request.PrivateSummary.Trim());
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var now = DateTime.UtcNow;
                var row = new CounselingCase
                {
                    TenantId = user.TenantId, ClientRequestId = request.RequestId,
                    StudentId = student.Id, CampusId = enrollment.CampusId,
                    OpenedByUserId = user.UserId, OpenedAtUtc = now,
                    ProtectedSummary = protectedSummary, IsUrgent = request.Urgent,
                    GuardianContactConsented = guardianConsented,
                    GuardianConsentAtUtc = guardianConsented ? now : null,
                    CreatedBy = user.UserId
                };
                db.CounselingCases.Add(row);
                await db.SaveChangesAsync(token);
                if (guardianConsented)
                {
                    db.CounselingCaseEvents.Add(new CounselingCaseEvent
                    {
                        TenantId = user.TenantId, CounselingCaseId = row.Id,
                        ClientRequestId = Guid.NewGuid(), Action = "ConsentGranted",
                        ProtectedNote = "", ActorUserId = user.UserId, CreatedBy = user.UserId
                    });
                    await db.SaveChangesAsync(token);
                }
                return ApiResponse<CounselingCaseRow>.SuccessResponse(Map(row, student.PublicId));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException) { return Fail<CounselingCaseRow>("Duplicate support request.", 409); }
    }

    public async Task<ApiResponse<CounselingCasePage>> SearchAsync(
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!Scoped || (!Officer && !Counselor))
            return Fail<CounselingCasePage>("Support role required.", 403);
        if (page <= 0 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return Fail<CounselingCasePage>("Invalid page.");
        var query = from row in Cases.AsNoTracking()
            join student in db.Students.AsNoTracking() on
                new { row.TenantId, Id = row.StudentId }
                equals new { student.TenantId, student.Id }
            where !student.IsDeleted
            select new { row, student.PublicId };
        if (!Officer)
            query = query.Where(x => x.row.AssignedCounselorUserId == user.UserId);
        query = query.Where(x => db.UserCampusAccesses.Any(a =>
            a.TenantId == user.TenantId && a.CampusId == x.row.CampusId &&
            a.UserId == user.UserId && a.IsActive && !a.IsDeleted));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.row.IsUrgent)
            .ThenByDescending(x => x.row.OpenedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<CounselingCasePage>.SuccessResponse(new CounselingCasePage
        {
            Page = page, PageSize = pageSize, TotalCount = count,
            Items = rows.Select(x => Map(x.row, x.PublicId)).ToList()
        });
    }

    public async Task<ApiResponse<CounselingCasePage>> MineAsync(int page = 1,
        int pageSize = 20, CancellationToken ct = default)
    {
        if (!Scoped) return Fail<CounselingCasePage>("Authentication required.", 403);
        if (page <= 0 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return Fail<CounselingCasePage>("Invalid page.");
        var query = from row in Cases.AsNoTracking()
            join student in db.Students.AsNoTracking()
                on new { row.TenantId, Id = row.StudentId }
                equals new { student.TenantId, student.Id }
            where row.OpenedByUserId == user.UserId && !student.IsDeleted &&
                (student.UserId == user.UserId ||
                    (from link in db.StudentGuardians
                     join guardian in db.Guardians on new { link.TenantId, Id = link.GuardianId }
                         equals new { guardian.TenantId, guardian.Id }
                     where link.TenantId == user.TenantId && link.StudentId == row.StudentId &&
                         !link.IsDeleted && !guardian.IsDeleted && guardian.IsActive &&
                         guardian.UserId == user.UserId
                     select link.Id).Any())
            select new { row, student.PublicId };
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.row.OpenedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var result = rows.Select(x => Map(x.row, x.PublicId)).ToList();
        foreach (var item in result) item.AssignedCounselorUserId = null;
        return ApiResponse<CounselingCasePage>.SuccessResponse(new CounselingCasePage
        {
            Page = page, PageSize = pageSize, TotalCount = count, Items = result
        });
    }

    public async Task<ApiResponse<CounselingCaseRow>> StatusAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (!Scoped) return Fail<CounselingCaseRow>("Authentication required.", 403);
        var row = await Cases.AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == reference, ct);
        if (row == null) return Fail<CounselingCaseRow>("Case not found.", 404);
        // A guardian's relationship must never reveal a learner's separate confidential case.
        if (!Officer && !(Counselor && row.AssignedCounselorUserId == user.UserId) &&
            (row.OpenedByUserId != user.UserId || !await OwnsStudent(row.StudentId, ct)))
            return Fail<CounselingCaseRow>("Case not found.", 404);
        if ((Officer || row.AssignedCounselorUserId == user.UserId) &&
            !await CampusAccess(row.CampusId, user.UserId, ct))
            return Fail<CounselingCaseRow>("Campus access revoked.", 403);
        var studentRef = await db.Students.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.Id == row.StudentId)
            .Select(x => x.PublicId).FirstOrDefaultAsync(ct);
        if (studentRef == Guid.Empty) return Fail<CounselingCaseRow>("Case not found.", 404);
        var result = Map(row, studentRef);
        if (!Officer && row.AssignedCounselorUserId != user.UserId)
            result.AssignedCounselorUserId = null;
        return ApiResponse<CounselingCaseRow>.SuccessResponse(result);
    }

    /// <summary>Only the original linked guardian may grant or withdraw permission
    /// for guardian contact. Consent is never inferred from kinship or copied into
    /// generic notifications; history is append-only and excludes case narratives.</summary>
    public async Task<ApiResponse<CounselingCaseRow>> UpdateGuardianConsentAsync(Guid reference,
        UpdateGuardianContactConsentRequest request, CancellationToken ct = default)
    {
        if (!Scoped || !(user.IsInRole("Guardian") || user.IsInRole("Parent")))
            return Fail<CounselingCaseRow>("Linked guardian permission required.", 403);
        if (reference == Guid.Empty || request == null || request.RequestId == Guid.Empty)
            return Fail<CounselingCaseRow>("Invalid consent request.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var row = await Cases.FirstOrDefaultAsync(x => x.PublicId == reference, token);
                if (row == null || row.OpenedByUserId != user.UserId ||
                    !await OwnsStudent(row.StudentId, token))
                    return Fail<CounselingCaseRow>("Case not accessible.", 404);
                var studentRef = await db.Students.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.Id == row.StudentId && !x.IsDeleted)
                    .Select(x => x.PublicId).FirstOrDefaultAsync(token);
                if (studentRef == Guid.Empty)
                    return Fail<CounselingCaseRow>("Student unavailable.", 409);
                var action = request.Consent ? "ConsentGranted" : "ConsentWithdrawn";
                var previous = await db.CounselingCaseEvents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == request.RequestId, token);
                if (previous != null)
                    return previous.CounselingCaseId == row.Id && previous.ActorUserId == user.UserId &&
                        previous.Action == action && row.GuardianContactConsented == request.Consent
                        ? ApiResponse<CounselingCaseRow>.SuccessResponse(Map(row, studentRef))
                        : Fail<CounselingCaseRow>("Consent request key already used.", 409);
                if (!Version(row.RowVersion, request.RowVersion))
                    return Fail<CounselingCaseRow>("Consent changed; reload.", 409);
                if (row.GuardianContactConsented == request.Consent)
                    return Fail<CounselingCaseRow>("Consent is already in the requested state.", 409);
                var now = DateTime.UtcNow;
                row.GuardianContactConsented = request.Consent;
                row.GuardianConsentAtUtc = request.Consent ? now : null;
                row.UpdatedAt = now; row.UpdatedBy = user.UserId;
                db.CounselingCaseEvents.Add(new CounselingCaseEvent
                {
                    TenantId = user.TenantId, CounselingCaseId = row.Id,
                    ClientRequestId = request.RequestId, Action = action,
                    ProtectedNote = "", ActorUserId = user.UserId, CreatedBy = user.UserId
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<CounselingCaseRow>.SuccessResponse(Map(row, studentRef));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<CounselingCaseRow>("Concurrent guardian consent update; reload.", 409);
        }
        catch (DbUpdateException)
        {
            return Fail<CounselingCaseRow>("Conflicting guardian consent update; reload.", 409);
        }
    }

    public async Task<ApiResponse<CounselingCaseRow>> AssignAsync(Guid reference,
        AssignCounselorRequest request, CancellationToken ct = default)
    {
        if (!Officer) return Fail<CounselingCaseRow>("Safeguarding officer required.", 403);
        if (request == null || request.RequestId == Guid.Empty || request.CounselorUserId <= 0)
            return Fail<CounselingCaseRow>("Invalid counselor assignment.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var row = await Cases.FirstOrDefaultAsync(x => x.PublicId == reference, token);
                if (row == null) return Fail<CounselingCaseRow>("Case not found.", 404);
                if (!await CampusAccess(row.CampusId, user.UserId, token))
                    return Fail<CounselingCaseRow>("Campus access revoked.", 403);
                var studentRef = await db.Students.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.Id == row.StudentId)
                    .Select(x => x.PublicId).FirstOrDefaultAsync(token);
                if (studentRef == Guid.Empty) return Fail<CounselingCaseRow>("Student unavailable.", 409);
                var prior = await db.CounselingCaseEvents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == request.RequestId, token);
                if (prior != null)
                    return prior.CounselingCaseId == row.Id && prior.Action == "Assigned" &&
                        row.AssignedCounselorUserId == request.CounselorUserId
                        ? ApiResponse<CounselingCaseRow>.SuccessResponse(Map(row, studentRef))
                        : Fail<CounselingCaseRow>("Request key already used.", 409);
                if (row.State != "Requested" || !Version(row.RowVersion, request.RowVersion))
                    return Fail<CounselingCaseRow>("Case changed; reload.", 409);
                var targetIsCounselor = await (from membership in db.UserRoles.AsNoTracking()
                    join role in db.Roles.AsNoTracking() on membership.RoleId equals role.Id
                    where membership.UserId == request.CounselorUserId &&
                        role.NormalizedName == "COUNSELOR" && role.IsActive && !role.IsDeleted &&
                        (role.TenantId == user.TenantId ||
                            (role.TenantId == null && db.TenantMemberships.Count(m =>
                                m.UserId == request.CounselorUserId && !m.IsDeleted &&
                                m.Status != EduOS.Core.Enums.Domain.MembershipStatus.Left) == 1)) &&
                        db.TenantMemberships.Any(m => m.UserId == request.CounselorUserId &&
                            m.TenantId == user.TenantId && !m.IsDeleted &&
                            m.Status == EduOS.Core.Enums.Domain.MembershipStatus.Active)
                    select membership.UserId).AnyAsync(token);
                var activeUser = await db.Users.AsNoTracking().AnyAsync(x =>
                    x.Id == request.CounselorUserId && x.IsActive && !x.IsDeleted, token);
                if (!targetIsCounselor || !activeUser ||
                    !await CampusAccess(row.CampusId, request.CounselorUserId, token))
                    return Fail<CounselingCaseRow>("Counselor must have an active campus role.", 403);
                row.State = "Assigned"; row.AssignedCounselorUserId = request.CounselorUserId;
                row.AssignedByUserId = user.UserId; row.AssignedAtUtc = DateTime.UtcNow;
                row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = user.UserId;
                db.CounselingCaseEvents.Add(new CounselingCaseEvent
                {
                    TenantId = user.TenantId, CounselingCaseId = row.Id,
                    ClientRequestId = request.RequestId, Action = "Assigned",
                    ProtectedNote = "", ActorUserId = user.UserId, CreatedBy = user.UserId
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<CounselingCaseRow>.SuccessResponse(Map(row, studentRef));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<CounselingCaseRow>("Case changed.", 409); }
        catch (DbUpdateException) { return Fail<CounselingCaseRow>("Concurrent assignment.", 409); }
    }

    public async Task<ApiResponse<CounselingCaseRow>> ActAsync(Guid reference,
        CounselingCaseActionRequest request, CancellationToken ct = default)
    {
        if (!Scoped) return Fail<CounselingCaseRow>("Authentication required.", 403);
        if (request == null || request.RequestId == Guid.Empty ||
            request.Action is not ("Note" or "Appointment" or "Referral" or "Escalate" or "Close") ||
            string.IsNullOrWhiteSpace(request.PrivateNote) ||
            request.PrivateNote.Trim().Length > 1500 ||
            (request.Action == "Appointment" && (!request.AppointmentAtUtc.HasValue ||
                request.AppointmentAtUtc <= DateTime.UtcNow)) ||
            (request.Action != "Appointment" && request.AppointmentAtUtc.HasValue))
            return Fail<CounselingCaseRow>("Invalid confidential case action.");
        var text = Protect(request.PrivateNote.Trim());
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var row = await Cases.FirstOrDefaultAsync(x => x.PublicId == reference, token);
                if (row == null) return Fail<CounselingCaseRow>("Case not found.", 404);
                if (!HasPrivateAccess(row))
                    return Fail<CounselingCaseRow>("Assigned counselor or safeguarding escalation access required.", 403);
                if (!await CampusAccess(row.CampusId, user.UserId, token))
                    return Fail<CounselingCaseRow>("Campus access revoked.", 403);
                var studentRef = await db.Students.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.Id == row.StudentId)
                    .Select(x => x.PublicId).FirstOrDefaultAsync(token);
                var previous = await db.CounselingCaseEvents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == request.RequestId, token);
                if (previous != null)
                    return previous.CounselingCaseId == row.Id && previous.Action == request.Action &&
                        previous.ActorUserId == user.UserId &&
                        previous.AppointmentAtUtc == request.AppointmentAtUtc &&
                        Unprotect(previous.ProtectedNote) == request.PrivateNote.Trim()
                        ? ApiResponse<CounselingCaseRow>.SuccessResponse(Map(row, studentRef))
                        : Fail<CounselingCaseRow>("Case action key already used.", 409);
                if (row.State == "Closed" || !Version(row.RowVersion, request.RowVersion))
                    return Fail<CounselingCaseRow>("Case changed or closed.", 409);
                if (request.Action == "Escalate")
                {
                    // Escalation must enter the urgent officer-access path, including
                    // cases originally opened as routine educational support.
                    row.State = "Escalated"; row.IsUrgent = true;
                }
                else if (request.Action == "Close")
                {
                    row.State = "Closed"; row.ClosedAtUtc = DateTime.UtcNow;
                    row.ClosedByUserId = user.UserId;
                }
                else if (row.State == "Assigned") row.State = "Active";
                row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = user.UserId;
                db.CounselingCaseEvents.Add(new CounselingCaseEvent
                {
                    TenantId = user.TenantId, CounselingCaseId = row.Id,
                    ClientRequestId = request.RequestId, Action = request.Action,
                    ProtectedNote = text, AppointmentAtUtc = request.AppointmentAtUtc,
                    ActorUserId = user.UserId, CreatedBy = user.UserId
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<CounselingCaseRow>.SuccessResponse(Map(row, studentRef));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<CounselingCaseRow>("Case changed.", 409); }
        catch (DbUpdateException) { return Fail<CounselingCaseRow>("Concurrent case action.", 409); }
    }

    /// <summary>Explicit POST view access, auditable without revealing details to list APIs.</summary>
    public async Task<ApiResponse<CounselingPrivateDetail>> OpenPrivateAsync(
        Guid reference, Guid accessRequestId, CancellationToken ct = default)
    {
        if (!Scoped || accessRequestId == Guid.Empty)
            return Fail<CounselingPrivateDetail>("Invalid private access.", 403);
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var row = await Cases.AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == reference, token);
                if (row == null || !HasPrivateAccess(row))
                    return Fail<CounselingPrivateDetail>("Private case unavailable.", 404);
                if (!await CampusAccess(row.CampusId, user.UserId, token))
                    return Fail<CounselingPrivateDetail>("Campus access revoked.", 403);
                var studentRef = await db.Students.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.Id == row.StudentId)
                    .Select(x => x.PublicId).FirstOrDefaultAsync(token);
                var prior = await db.CounselingCaseEvents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == accessRequestId, token);
                if (prior != null && (prior.CounselingCaseId != row.Id || prior.ActorUserId != user.UserId ||
                    prior.Action != "Accessed"))
                    return Fail<CounselingPrivateDetail>("Private access key already used.", 409);
                if (prior == null)
                {
                    db.CounselingCaseEvents.Add(new CounselingCaseEvent
                    {
                        TenantId = user.TenantId, CounselingCaseId = row.Id,
                        ClientRequestId = accessRequestId, Action = "Accessed",
                        ProtectedNote = "", ActorUserId = user.UserId, CreatedBy = user.UserId
                    });
                    await db.SaveChangesAsync(token);
                }
                var events = await db.CounselingCaseEvents.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.CounselingCaseId == row.Id &&
                    x.Action != "Accessed" && !x.IsDeleted).OrderBy(x => x.Id).Take(300)
                    .Select(x => new { x.Action, x.ProtectedNote, x.AppointmentAtUtc, x.OccurredAtUtc })
                    .ToListAsync(token);
                return ApiResponse<CounselingPrivateDetail>.SuccessResponse(new CounselingPrivateDetail
                {
                    Case = Map(row, studentRef), PrivateSummary = Unprotect(row.ProtectedSummary),
                    Events = events.Select(x => new CounselingPrivateEvent
                    {
                        Action = x.Action, Note = string.IsNullOrEmpty(x.ProtectedNote) ? "" :
                            Unprotect(x.ProtectedNote), AppointmentAtUtc = x.AppointmentAtUtc,
                        OccurredAtUtc = x.OccurredAtUtc
                    }).ToList()
                });
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException) { return Fail<CounselingPrivateDetail>("Concurrent access; retry.", 409); }
    }

    /// <summary>Calendar-month campus counts only, with small-cell suppression.
    /// No case identifiers, private text, status breakdowns or arbitrary dates that
    /// would permit repeated differencing attacks against a small cohort.</summary>
    public async Task<ApiResponse<CounselingAggregateReport>> AggregatesAsync(
        int year, int month, CancellationToken ct = default)
    {
        if (!Scoped || (!Officer && !user.IsTenantAdmin))
            return Fail<CounselingAggregateReport>("Safeguarding aggregate access required.", 403);
        if (year is < 2020 or > 2200 || month is < 1 or > 12)
            return Fail<CounselingAggregateReport>("Choose a valid calendar month.");
        var from = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(1);
        var query = Cases.AsNoTracking().Where(x => x.OpenedAtUtc >= from && x.OpenedAtUtc < to);
        if (!user.IsTenantAdmin)
            query = query.Where(x => db.UserCampusAccesses.Any(a => a.TenantId == user.TenantId &&
                a.CampusId == x.CampusId && a.UserId == user.UserId && a.IsActive && !a.IsDeleted));
        var campuses = await query.GroupBy(x => x.CampusId)
            .Select(group => new CounselingCampusAggregate
            {
                CampusId = group.Key, CaseCount = group.Count()
            }).Where(x => x.CaseCount >= 5).OrderBy(x => x.CampusId)
            .Take(500).ToListAsync(ct);
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId, UserId = user.UserId,
            Action = "Counseling.AggregateViewed", EntityName = "CounselingAggregate",
            NewValue = year.ToString("D4") + "-" + month.ToString("D2"),
            OccurredAt = DateTime.UtcNow
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Fail<CounselingAggregateReport>("Aggregate access could not be audited.", 503); }
        return ApiResponse<CounselingAggregateReport>.SuccessResponse(new()
        {
            Year = year, Month = month, Campuses = campuses
        });
    }
}
