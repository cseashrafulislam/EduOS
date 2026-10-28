using EduOS.Core.Common;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Entities.HR;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.HR;

/// <summary>
/// Internal applicant pipeline. No Person, Employee, login, salary or accounting write
/// occurs here. Hiring links a separately verified existing tenant Employee.
/// </summary>
public sealed class RecruitmentService : IRecruitmentService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    public RecruitmentService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    { this.db = db; this.user = user; this.clock = clock; }
    private bool Allowed => user.IsAuthenticated && user.IsTenantAdmin &&
        user.UserId > 0 && user.TenantId > 0 && db.CurrentTenantId == user.TenantId;
    private static ApiResponse<T> Error<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static bool ValidVersion(byte[] current, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied) || supplied.Length > 256) return false;
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static bool Next(RecruitmentStage old, RecruitmentStage target) =>
        old switch
        {
            RecruitmentStage.Applied => target is RecruitmentStage.Shortlisted or RecruitmentStage.Rejected,
            RecruitmentStage.Shortlisted => target is RecruitmentStage.Interviewed or RecruitmentStage.Rejected,
            RecruitmentStage.Interviewed => target is RecruitmentStage.Offered or RecruitmentStage.Rejected,
            RecruitmentStage.Offered => target is RecruitmentStage.Hired or RecruitmentStage.Rejected,
            _ => false
        };
    private RecruitmentCandidateDto Map(RecruitmentCandidate candidate, Guid? employee) => new()
    {
        Reference = candidate.PublicId, CampusId = candidate.CampusId,
        DesignationId = candidate.DesignationId, FullName = candidate.FullName,
        Email = candidate.Email, Phone = candidate.Phone,
        AppliedOn = candidate.AppliedOn, InterviewOn = candidate.InterviewOn,
        Stage = candidate.Stage, LinkedEmployeeReference = employee,
        Notes = candidate.Notes, RowVersion = Convert.ToBase64String(candidate.RowVersion)
    };

    public async Task<ApiResponse<PagedResult<RecruitmentCandidateDto>>> ListAsync(
        string? search, long? campusId, RecruitmentStage? stage, int page, int pageSize,
        CancellationToken ct = default)
    {
        if (!Allowed) return Error<PagedResult<RecruitmentCandidateDto>>("Tenant administrator required.", 403);
        if (search?.Length > 100 || campusId is <= 0 ||
            stage.HasValue && !Enum.IsDefined(stage.Value) ||
            page < 1 || pageSize is < 1 or > 50 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<RecruitmentCandidateDto>>("Invalid recruitment filters.");
        var q = db.RecruitmentCandidates.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted);
        if (campusId.HasValue) q = q.Where(x => x.CampusId == campusId);
        if (stage.HasValue) q = q.Where(x => x.Stage == stage);
        var term = Clean(search);
        if (term != null) q = q.Where(x => x.FullName.StartsWith(term) ||
            (x.Email != null && x.Email.StartsWith(term)));
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.AppliedOn).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var employeeIds = rows.Where(x => x.LinkedEmployeeId.HasValue)
            .Select(x => x.LinkedEmployeeId!.Value).Distinct().ToArray();
        var links = await db.Employees.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            employeeIds.Contains(x.Id) && !x.IsDeleted)
            .Select(x => new { x.Id, x.PublicId }).ToDictionaryAsync(x => x.Id, x => x.PublicId, ct);
        return ApiResponse<PagedResult<RecruitmentCandidateDto>>.SuccessResponse(
            new PagedResult<RecruitmentCandidateDto> { Page = page, PageSize = pageSize,
                TotalCount = total, Items = rows.Select(x => Map(x,
                    x.LinkedEmployeeId.HasValue &&
                    links.TryGetValue(x.LinkedEmployeeId.Value, out var employee) ? employee : null)).ToList() });
    }

    public async Task<ApiResponse<RecruitmentCandidateDto>> CreateAsync(
        RecruitmentCreateRequestDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Error<RecruitmentCandidateDto>("Tenant administrator required.", 403);
        var name = Clean(request?.FullName), email = Clean(request?.Email), phone = Clean(request?.Phone);
        var notes = Clean(request?.Notes);
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.CampusId <= 0 || request.DesignationId <= 0 ||
            name is null || name.Length > 200 || email?.Length > 200 ||
            phone?.Length > 30 || notes?.Length > 500 ||
            request.AppliedOn == default || !request.ConsentConfirmed)
            return Error<RecruitmentCandidateDto>("Candidate details and recorded consent are required.");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var replay = await db.RecruitmentCandidates.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, ct);
            if (replay != null)
            {
                if (replay.CampusId != request.CampusId || replay.DesignationId != request.DesignationId ||
                    replay.FullName != name || replay.Email != email || replay.Phone != phone ||
                    replay.AppliedOn != request.AppliedOn || replay.Notes != notes)
                    return Error<RecruitmentCandidateDto>("Request key belongs to a different application.", 409);
                return ApiResponse<RecruitmentCandidateDto>.SuccessResponse(Map(replay, null),
                    "Existing recruitment application returned.");
            }
            if (!await db.Campuses.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.Id == request.CampusId && x.IsActive && !x.IsDeleted, ct) ||
                !await db.Designations.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.Id == request.DesignationId && x.IsActive && !x.IsDeleted, ct))
                return Error<RecruitmentCandidateDto>("Campus or designation is not active in this tenant.", 404);
            var now = clock.GetUtcNow().UtcDateTime;
            var candidate = new RecruitmentCandidate
            {
                TenantId = user.TenantId, ClientRequestId = request.ClientRequestId,
                CampusId = request.CampusId, DesignationId = request.DesignationId,
                FullName = name, Email = email, Phone = phone, Notes = notes,
                AppliedOn = request.AppliedOn, ConsentConfirmedAt = now,
                Stage = RecruitmentStage.Applied, CreatedAt = now, CreatedBy = user.UserId
            };
            db.RecruitmentCandidates.Add(candidate);
            await db.SaveChangesAsync(ct);
            db.RecruitmentStageHistories.Add(new RecruitmentStageHistory
            {
                TenantId = user.TenantId, RecruitmentCandidateId = candidate.Id,
                NewStage = RecruitmentStage.Applied, TransitionAt = now,
                ActorUserId = user.UserId, CreatedAt = now, CreatedBy = user.UserId
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ApiResponse<RecruitmentCandidateDto>.SuccessResponse(Map(candidate, null));
        }
        catch (DbUpdateException)
        { return Error<RecruitmentCandidateDto>("Duplicate application or concurrent change.", 409); }
    }

    public async Task<ApiResponse<RecruitmentCandidateDto>> TransitionAsync(
        Guid reference, RecruitmentTransitionRequestDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Error<RecruitmentCandidateDto>("Tenant administrator required.", 403);
        var reason = Clean(request?.Reason);
        if (reference == Guid.Empty || request == null ||
            request.RowVersion?.Length is < 1 or > 256 || reason?.Length > 500 ||
            !Enum.IsDefined(request.NextStage) ||
            request.NextStage == RecruitmentStage.Rejected && reason == null)
            return Error<RecruitmentCandidateDto>("Invalid status, reason or concurrency version.");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var row = await db.RecruitmentCandidates.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
            if (row == null) return Error<RecruitmentCandidateDto>("Candidate not found.", 404);
            if (!ValidVersion(row.RowVersion, request.RowVersion))
                return Error<RecruitmentCandidateDto>("Candidate changed. Reload and retry.", 409);
            if (!Next(row.Stage, request.NextStage))
                return Error<RecruitmentCandidateDto>("Invalid recruitment stage transition.", 409);
            if (request.NextStage == RecruitmentStage.Offered &&
                (row.CreatedBy == user.UserId || row.UpdatedBy == user.UserId))
                return Error<RecruitmentCandidateDto>(
                    "A separate administrator must approve the offer.", 403);
            if (request.NextStage == RecruitmentStage.Interviewed &&
                (!request.InterviewOn.HasValue || request.InterviewOn.Value < row.AppliedOn))
                return Error<RecruitmentCandidateDto>("Interview date must be supplied and follow application.", 409);
            if (request.NextStage == RecruitmentStage.Offered && !row.InterviewOn.HasValue)
                return Error<RecruitmentCandidateDto>("An interview record is required before an offer.", 409);

            Guid? employeeReference = null;
            if (request.NextStage == RecruitmentStage.Hired)
            {
                if (!request.IdentityVerified || !request.EmployeeReference.HasValue ||
                    request.EmployeeReference.Value == Guid.Empty)
                    return Error<RecruitmentCandidateDto>(
                        "Identity verification and existing employee reference are required.", 409);
                if (row.UpdatedBy == user.UserId)
                    return Error<RecruitmentCandidateDto>("Offer issuer cannot confirm their own hire.", 403);
                var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.PublicId == request.EmployeeReference &&
                    x.State == EmployeeState.Active && !x.IsDeleted, ct);
                if (employee == null || employee.JoiningDate < row.AppliedOn)
                    return Error<RecruitmentCandidateDto>("Verified active employee not found.", 404);
                if (await db.RecruitmentCandidates.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.Id != row.Id &&
                    x.LinkedEmployeeId == employee.Id && !x.IsDeleted, ct))
                    return Error<RecruitmentCandidateDto>("Employee is already linked to recruitment.", 409);
                row.LinkedEmployeeId = employee.Id;
                employeeReference = employee.PublicId;
            }
            var previous = row.Stage;
            var now = clock.GetUtcNow().UtcDateTime;
            row.Stage = request.NextStage;
            if (request.NextStage == RecruitmentStage.Interviewed) row.InterviewOn = request.InterviewOn;
            row.UpdatedAt = now;
            row.UpdatedBy = user.UserId;
            db.RecruitmentStageHistories.Add(new RecruitmentStageHistory
            {
                TenantId = user.TenantId, RecruitmentCandidateId = row.Id,
                PreviousStage = previous, NewStage = row.Stage,
                TransitionAt = now, ActorUserId = user.UserId,
                Reason = reason, CreatedAt = now, CreatedBy = user.UserId
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ApiResponse<RecruitmentCandidateDto>.SuccessResponse(Map(row, employeeReference));
        }
        catch (DbUpdateConcurrencyException)
        { return Error<RecruitmentCandidateDto>("Recruitment stage changed concurrently.", 409); }
        catch (DbUpdateException)
        { return Error<RecruitmentCandidateDto>("Recruitment transition conflicts with a committed record.", 409); }
    }

    public async Task<ApiResponse<PagedResult<RecruitmentStageHistoryDto>>> HistoryAsync(
        Guid reference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Allowed) return Error<PagedResult<RecruitmentStageHistoryDto>>("Tenant administrator required.", 403);
        if (reference == Guid.Empty || page < 1 || pageSize is < 1 or > 50 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<RecruitmentStageHistoryDto>>("Invalid history query.");
        var candidateId = await db.RecruitmentCandidates.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (candidateId == 0)
            return Error<PagedResult<RecruitmentStageHistoryDto>>("Candidate not found.", 404);
        var q = db.RecruitmentStageHistories.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.RecruitmentCandidateId == candidateId && !x.IsDeleted);
        var count = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new RecruitmentStageHistoryDto {
                PreviousStage = x.PreviousStage, NewStage = x.NewStage,
                TransitionAt = x.TransitionAt, Reason = x.Reason
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<RecruitmentStageHistoryDto>>.SuccessResponse(
            new PagedResult<RecruitmentStageHistoryDto> {
                Page = page, PageSize = pageSize, TotalCount = count, Items = rows
            });
    }
}
