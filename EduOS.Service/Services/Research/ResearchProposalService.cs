using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Research;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Research;

/// <summary>
/// Single-database, serializable proposal workflow. Submitted revisions and
/// decisions are immutable; neither document storage nor Finance is duplicated.
/// </summary>
public sealed class ResearchProposalService(
    EduOSDbContext db, ICurrentUserService user, TimeProvider clock,
    ILogger<ResearchProposalService> logger) : IResearchProposalService
{
    private const string Columns = """
        SELECT p.Id, p.PublicId AS Reference, p.CampusId, p.AcademicProgramId,
               p.OwnerUserId, p.Title, p.Abstract, p.Protocol, p.ContentSha256,
               p.CurrentRevision, p.StateCode, p.SupervisorUserId,
               p.ReviewerUserId, p.RiskCode, p.RowVersion, p.UpdatedAtUtc
        FROM dbo.ResearchProposals AS p WHERE p.TenantId = {0}
        """;
    private const string CampusGrant = """
        EXISTS (SELECT 1 FROM dbo.UserCampusAccesses AS g
            JOIN dbo.Campuses AS c ON c.Id = g.CampusId AND c.TenantId = g.TenantId
            WHERE g.TenantId = {0} AND g.UserId = {1} AND g.CampusId = p.CampusId
              AND g.IsActive = 1 AND g.IsDeleted = 0 AND c.IsActive = 1 AND c.IsDeleted = 0)
        """;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private bool TenantValid => user.IsAuthenticated && user.TenantId > 0 && user.UserId > 0 && db.CurrentTenantId == user.TenantId;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Research record not available.", 404);
    private static ApiResponse<T> Conflict<T>() => ApiResponse<T>.ErrorResponse("Proposal changed or command conflicts; reload.", 409);
    private static bool Role(IReadOnlySet<string> roles, params string[] names) => names.Any(roles.Contains);
    private static bool Coordinator(IReadOnlySet<string> roles) => Role(roles, "TENANTADMIN", "PRINCIPAL", "RESEARCHCOORDINATOR");
    private static bool Ethics(IReadOnlySet<string> roles) => Role(roles, "ETHICSREVIEWER");

    private async Task<HashSet<string>> LiveRolesAsync(CancellationToken ct)
    {
        if (!TenantValid ||
            !await db.Users.AsNoTracking().AnyAsync(x => x.Id == user.UserId && x.IsActive && !x.IsDeleted, ct) ||
            !await db.TenantMemberships.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.UserId == user.UserId && x.Status == MembershipStatus.Active && !x.IsDeleted, ct))
            return new(StringComparer.OrdinalIgnoreCase);
        var roles = await (from ur in db.UserRoles.AsNoTracking()
                           join r in db.Roles.IgnoreQueryFilters().AsNoTracking() on ur.RoleId equals r.Id
                           where ur.UserId == user.UserId && r.IsActive && !r.IsDeleted &&
                                 (r.TenantId == user.TenantId || (r.TenantId == null && r.IsSystemRole))
                           select r.NormalizedName).ToListAsync(ct);
        return roles.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private Task<bool> CampusAsync(long campusId, CancellationToken ct) =>
        db.Campuses.AsNoTracking().AnyAsync(c => c.Id == campusId && c.TenantId == user.TenantId &&
            c.IsActive && !c.IsDeleted && db.UserCampusAccesses.Any(g => g.TenantId == user.TenantId &&
                g.UserId == user.UserId && g.CampusId == campusId && g.IsActive && !g.IsDeleted), ct);

    public async Task<ApiResponse<List<ResearchCampusDto>>> CampusesAsync(CancellationToken ct = default)
    {
        var roles = await LiveRolesAsync(ct);
        if (!Role(roles, "TEACHER", "RESEARCHER", "RESEARCHCOORDINATOR", "TENANTADMIN", "PRINCIPAL", "RESEARCHREVIEWER", "RESEARCHSUPERVISOR", "ETHICSREVIEWER"))
            return Denied<List<ResearchCampusDto>>();
        var campuses = await db.Campuses.AsNoTracking().Where(c => c.TenantId == user.TenantId &&
            c.IsActive && !c.IsDeleted && db.UserCampusAccesses.Any(g => g.TenantId == user.TenantId &&
                g.UserId == user.UserId && g.CampusId == c.Id && g.IsActive && !g.IsDeleted))
            .OrderBy(c => c.Name).Take(100).Select(c => new ResearchCampusDto
            { Id = c.Id, Name = c.Name }).ToListAsync(ct);
        return ApiResponse<List<ResearchCampusDto>>.SuccessResponse(campuses);
    }

    public async Task<ApiResponse<List<ResearchProgramDto>>> ProgramsAsync(long campusId, CancellationToken ct = default)
    {
        var roles = await LiveRolesAsync(ct);
        if (roles.Count == 0 || !await CampusAsync(campusId, ct))
            return Denied<List<ResearchProgramDto>>();
        var programs = await (from pc in db.ProgramCampuses.AsNoTracking()
                              join p in db.AcademicPrograms.AsNoTracking() on pc.AcademicProgramId equals p.Id
                              where pc.TenantId == user.TenantId && p.TenantId == user.TenantId &&
                                    pc.CampusId == campusId && pc.IsActive && !pc.IsDeleted &&
                                    p.IsActive && !p.IsDeleted
                              orderby p.Name
                              select new ResearchProgramDto { Id = p.Id, Name = p.Name })
            .Take(100).ToListAsync(ct);
        return ApiResponse<List<ResearchProgramDto>>.SuccessResponse(programs);
    }

    public async Task<ApiResponse<List<ResearchReviewerDto>>> ReviewersAsync(
        long campusId, string? search, CancellationToken ct = default)
    {
        var roles = await LiveRolesAsync(ct);
        if (!Coordinator(roles) || campusId <= 0 || search?.Length > 100 ||
            !await CampusAsync(campusId, ct)) return Denied<List<ResearchReviewerDto>>();
        var term = (search ?? "").Trim();
        var candidates = await db.Users.AsNoTracking().Where(u => u.IsActive && !u.IsDeleted &&
            u.Id != user.UserId && (term == "" || u.FullName.Contains(term)) &&
            db.TenantMemberships.Any(m => m.TenantId == user.TenantId && m.UserId == u.Id &&
                !m.IsDeleted && m.Status == MembershipStatus.Active) &&
            db.UserCampusAccesses.Any(g => g.TenantId == user.TenantId && g.UserId == u.Id &&
                g.CampusId == campusId && g.IsActive && !g.IsDeleted))
            .OrderBy(u => u.FullName).ThenBy(u => u.Id).Take(50)
            .Select(u => new ResearchReviewerDto
            {
                UserId = u.Id, Name = u.FullName,
                CanSupervise = db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.IgnoreQueryFilters().Any(r =>
                    r.Id == ur.RoleId && r.IsActive && !r.IsDeleted &&
                    (r.TenantId == user.TenantId || (r.TenantId == null && r.IsSystemRole)) &&
                    (r.NormalizedName == "TEACHER" || r.NormalizedName == "PRINCIPAL" || r.NormalizedName == "RESEARCHSUPERVISOR"))),
                CanReview = db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.IgnoreQueryFilters().Any(r =>
                    r.Id == ur.RoleId && r.IsActive && !r.IsDeleted &&
                    (r.TenantId == user.TenantId || (r.TenantId == null && r.IsSystemRole)) &&
                    (r.NormalizedName == "TEACHER" || r.NormalizedName == "RESEARCHREVIEWER")))
            }).ToListAsync(ct);
        return ApiResponse<List<ResearchReviewerDto>>.SuccessResponse(candidates.Where(x => x.CanSupervise || x.CanReview).ToList());
    }

    private async Task<bool> EligibleAsync(long candidate, long campusId, string[] roleCodes, CancellationToken ct) =>
        candidate > 0 && await db.Users.AsNoTracking().AnyAsync(u => u.Id == candidate && u.IsActive && !u.IsDeleted &&
            db.TenantMemberships.Any(m => m.TenantId == user.TenantId && m.UserId == u.Id &&
                !m.IsDeleted && m.Status == MembershipStatus.Active) &&
            db.UserCampusAccesses.Any(g => g.TenantId == user.TenantId && g.UserId == u.Id &&
                g.CampusId == campusId && g.IsActive && !g.IsDeleted) &&
            db.UserRoles.Any(ur => ur.UserId == u.Id &&
                db.Roles.IgnoreQueryFilters().Any(r => r.Id == ur.RoleId && r.IsActive && !r.IsDeleted &&
                    (r.TenantId == user.TenantId || (r.TenantId == null && r.IsSystemRole)) &&
                    roleCodes.Contains(r.NormalizedName!))), ct);

    private ResearchProposalDto Map(ProposalRow p, IReadOnlySet<string> roles) => new()
    {
        Reference = p.Reference, CampusId = p.CampusId, AcademicProgramId = p.AcademicProgramId,
        OwnerUserId = p.OwnerUserId, Title = p.Title, Abstract = Sensitive(p, roles) ? p.Abstract : null,
        Protocol = Sensitive(p, roles) ? p.Protocol : null, ContentSha256 = p.ContentSha256,
        StateCode = p.StateCode, CurrentRevision = p.CurrentRevision,
        SupervisorUserId = p.SupervisorUserId, ReviewerUserId = p.ReviewerUserId,
        RiskCode = p.RiskCode, RowVersion = Convert.ToBase64String(p.RowVersion),
        UpdatedAtUtc = p.UpdatedAtUtc,
        AllowedActions = new[] { "edit", "submit", "revise", "assign", "supervisor-approve",
            "supervisor-revise", "review-recommend", "review-revise", "ethics-approve",
            "ethics-reject", "withdraw" }
            .Where(a => NextState(p, a) != null && AuthorisedAction(p, a, roles)).ToList()
    };
    private bool Sensitive(ProposalRow p, IReadOnlySet<string> roles) =>
        (p.OwnerUserId == user.UserId && Role(roles, "TEACHER", "RESEARCHER", "RESEARCHCOORDINATOR", "TENANTADMIN", "PRINCIPAL")) ||
        (p.SupervisorUserId == user.UserId && Role(roles, "TEACHER", "PRINCIPAL", "RESEARCHSUPERVISOR")) ||
        (p.ReviewerUserId == user.UserId && Role(roles, "TEACHER", "RESEARCHREVIEWER")) ||
        (Ethics(roles) && p.StateCode == "ReviewRecommended");
    private bool Visible(ProposalRow p, IReadOnlySet<string> roles) =>
        Sensitive(p, roles) || Coordinator(roles);

    private async Task<ProposalRow?> GetAsync(Guid reference, CancellationToken ct)
    {
        var rows = await db.Database.SqlQueryRaw<ProposalRow>(
            Columns + " AND p.PublicId = {2} AND " + CampusGrant,
            user.TenantId, user.UserId, reference).ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    private async Task<EventRow?> PriorAsync(Guid requestId, CancellationToken ct)
    {
        var rows = await db.Database.SqlQueryRaw<EventRow>("""
            SELECT e.ProposalId, e.ActorUserId, e.RequestSha256, e.ResultSnapshot
            FROM dbo.ResearchProposalEvents AS e
            WHERE e.TenantId = {0} AND e.ClientRequestId = {1}
            """, user.TenantId, requestId).ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    private static ApiResponse<ResearchProposalDto> Replay(EventRow previous, long proposalId,
        long actor, string requestHash)
    {
        if (previous.ProposalId != proposalId || previous.ActorUserId != actor || previous.RequestSha256 != requestHash)
            return Conflict<ResearchProposalDto>();
        var snapshot = JsonSerializer.Deserialize<ResearchProposalDto>(previous.ResultSnapshot);
        return snapshot == null ? Conflict<ResearchProposalDto>() :
            ApiResponse<ResearchProposalDto>.SuccessResponse(snapshot, "Previously completed command.");
    }

    private async Task RecordAsync(ProposalRow current, Guid commandId, string requestHash,
        string action, string from, string? reason, ResearchProposalDto result, CancellationToken ct)
    {
        var snapshot = JsonSerializer.Serialize(result);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.ResearchProposalEvents
            (TenantId, ProposalId, ClientRequestId, RequestSha256, ActorUserId,
             ActionCode, FromState, ToState, RevisionNo, Reason, ResultSnapshot, OccurredAtUtc)
            VALUES ({user.TenantId}, {current.Id}, {commandId}, {requestHash}, {user.UserId},
                    {action}, {from}, {result.StateCode}, {result.CurrentRevision},
                    {reason}, {snapshot}, {Now})
            """, ct);
    }

    private static bool Content(string title, string abstractText, string protocol) =>
        title.Length is >= 5 and <= 200 && abstractText.Length is >= 30 and <= 3000 &&
        protocol.Length is >= 30 and <= 6000 &&
        !title.Any(char.IsControl) && !abstractText.Any(char.IsControl) && !protocol.Any(char.IsControl);
    private static string ContentHash(string title, string abstractText, string protocol) =>
        Hash(JsonSerializer.Serialize(new[] { title, abstractText, protocol }));

    public async Task<ApiResponse<ResearchProposalDto>> CreateAsync(
        ResearchProposalCreateDto input, CancellationToken ct = default)
    {
        if (input == null || input.ClientRequestId == Guid.Empty || input.CampusId <= 0)
            return ApiResponse<ResearchProposalDto>.ErrorResponse("Campus and request ID required.");
        var title = input.Title?.Trim() ?? "";
        var abstractText = input.Abstract?.Trim() ?? "";
        var protocol = input.Protocol?.Trim() ?? "";
        if (!Content(title, abstractText, protocol))
            return ApiResponse<ResearchProposalDto>.ErrorResponse("Valid title, abstract and protocol required.");
        var hash = Hash(JsonSerializer.Serialize(new object?[] { "create", input.CampusId, input.AcademicProgramId,
            title, abstractText, protocol }));
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var roles = await LiveRolesAsync(token);
                if (!Role(roles, "TEACHER", "RESEARCHER", "RESEARCHCOORDINATOR", "TENANTADMIN", "PRINCIPAL") ||
                    !await CampusAsync(input.CampusId, token)) return Denied<ResearchProposalDto>();
                var prior = await PriorAsync(input.ClientRequestId, token);
                if (prior != null)
                {
                    // An old create request may be replayed only with its exact original payload.
                    var existing = await db.Database.SqlQueryRaw<ProposalRow>(Columns +
                        " AND p.Id = {2} AND " + CampusGrant, user.TenantId, user.UserId, prior.ProposalId).ToListAsync(token);
                    if (existing.Count == 0 || existing[0].OwnerUserId != user.UserId)
                        return Denied<ResearchProposalDto>();
                    return Replay(prior, existing[0].Id, user.UserId, hash);
                }
                if (input.AcademicProgramId.HasValue && !await db.ProgramCampuses.AsNoTracking().AnyAsync(pc =>
                    pc.TenantId == user.TenantId && pc.AcademicProgramId == input.AcademicProgramId &&
                    pc.CampusId == input.CampusId && pc.IsActive && !pc.IsDeleted &&
                    db.AcademicPrograms.Any(ap => ap.Id == pc.AcademicProgramId && ap.IsActive && !ap.IsDeleted), token))
                    return ApiResponse<ResearchProposalDto>.ErrorResponse("Academic program not available in this campus.", 400);
                var reference = Guid.NewGuid();
                var contentHash = ContentHash(title, abstractText, protocol);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO dbo.ResearchProposals
                    (TenantId, PublicId, CampusId, AcademicProgramId, CreateRequestId,
                     OwnerUserId, Title, Abstract, Protocol, ContentSha256,
                     CurrentRevision, StateCode, CreatedAtUtc, UpdatedAtUtc)
                    VALUES ({user.TenantId}, {reference}, {input.CampusId}, {input.AcademicProgramId},
                            {input.ClientRequestId}, {user.UserId}, {title}, {abstractText}, {protocol},
                            {contentHash}, {0}, {"Draft"}, {Now}, {Now})
                    """, token);
                var row = await GetAsync(reference, token) ?? throw new InvalidOperationException("Research create missing row.");
                var result = Map(row, roles);
                await RecordAsync(row, input.ClientRequestId, hash, "create", "None", null, result, token);
                return ApiResponse<ResearchProposalDto>.SuccessResponse(result, "Research proposal draft created.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        {
            logger.LogWarning(ex, "Research create conflict for tenant {TenantId}", user.TenantId);
            return Conflict<ResearchProposalDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Research create failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<ResearchProposalDto>.ErrorResponse("Proposal could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<ResearchProposalDto>> CommandAsync(Guid reference, string action,
        ResearchProposalCommandDto input, CancellationToken ct = default)
    {
        action = action?.Trim().ToLowerInvariant() ?? "";
        var accepted = new HashSet<string>(StringComparer.Ordinal) { "edit", "submit", "revise", "assign",
            "supervisor-approve", "supervisor-revise", "review-recommend", "review-revise",
            "ethics-approve", "ethics-reject", "withdraw" };
        if (reference == Guid.Empty || input == null || input.ClientRequestId == Guid.Empty ||
            !accepted.Contains(action) || input.Reason?.Length > 500 || input.RiskCode?.Length > 16)
            return ApiResponse<ResearchProposalDto>.ErrorResponse("Invalid proposal command.");
        if (input.RowVersion?.Length is not 12)
            return ApiResponse<ResearchProposalDto>.ErrorResponse("Invalid row version.");
        byte[] expected;
        try { expected = Convert.FromBase64String(input.RowVersion ?? ""); }
        catch (FormatException) { return ApiResponse<ResearchProposalDto>.ErrorResponse("Invalid row version."); }
        if (expected.Length != 8) return ApiResponse<ResearchProposalDto>.ErrorResponse("Invalid row version.");
        var reason = input.Reason?.Trim();
        var title = input.Title?.Trim() ?? "";
        var abstractText = input.Abstract?.Trim() ?? "";
        var protocol = input.Protocol?.Trim() ?? "";
        var isContent = action is "edit" or "revise";
        if (isContent && !Content(title, abstractText, protocol))
            return ApiResponse<ResearchProposalDto>.ErrorResponse("Valid title, abstract and protocol required.");
        if ((action is "revise" or "withdraw" or "supervisor-revise" or "review-revise" or "ethics-reject") &&
            (reason?.Length ?? 0) < 10)
            return ApiResponse<ResearchProposalDto>.ErrorResponse("An explanation of at least 10 characters is required.");
        if (action.StartsWith("ethics-", StringComparison.Ordinal) &&
            (input.DeclaresNoConflict != true || (reason?.Length ?? 0) < 15 ||
             input.RiskCode is not ("Low" or "Moderate" or "High")))
            return ApiResponse<ResearchProposalDto>.ErrorResponse("Independent ethics declaration, risk and reason required.");
        var requestHash = Hash(JsonSerializer.Serialize(new object?[] { reference, action, input.RowVersion,
            title, abstractText, protocol, input.SupervisorUserId, input.ReviewerUserId,
            input.RiskCode, reason, input.DeclaresNoConflict }));
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var roles = await LiveRolesAsync(token);
                if (roles.Count == 0) return Denied<ResearchProposalDto>();
                var row = await GetAsync(reference, token);
                if (row == null || !Visible(row, roles) || !await CampusAsync(row.CampusId, token))
                    return Denied<ResearchProposalDto>();
                var prior = await PriorAsync(input.ClientRequestId, token);
                if (prior != null)
                {
                    // Replay is never allowed after assignment or campus access revocation.
                    if (!AuthorisedAction(row, action, roles)) return Denied<ResearchProposalDto>();
                    return Replay(prior, row.Id, user.UserId, requestHash);
                }
                if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, expected))
                    return Conflict<ResearchProposalDto>();
                if (!AuthorisedAction(row, action, roles)) return Denied<ResearchProposalDto>();
                var before = row.StateCode;
                var next = NextState(row, action);
                if (next == null) return Conflict<ResearchProposalDto>();
                if (action == "assign")
                {
                    if (input.SupervisorUserId is not > 0 || input.ReviewerUserId is not > 0 ||
                        input.SupervisorUserId == row.OwnerUserId || input.ReviewerUserId == row.OwnerUserId ||
                        input.SupervisorUserId == input.ReviewerUserId ||
                        !await EligibleAsync(input.SupervisorUserId.Value, row.CampusId,
                            new[] { "TEACHER", "PRINCIPAL", "RESEARCHSUPERVISOR" }, token) ||
                        !await EligibleAsync(input.ReviewerUserId.Value, row.CampusId,
                            new[] { "TEACHER", "RESEARCHREVIEWER" }, token))
                        return ApiResponse<ResearchProposalDto>.ErrorResponse("Select two different eligible campus reviewers.", 400);
                }
                if (action is "supervisor-approve" or "supervisor-revise" &&
                    !await EligibleAsync(user.UserId, row.CampusId,
                        new[] { "TEACHER", "PRINCIPAL", "RESEARCHSUPERVISOR" }, token))
                    return Denied<ResearchProposalDto>();
                if (action is "review-recommend" or "review-revise" &&
                    !await EligibleAsync(user.UserId, row.CampusId,
                        new[] { "TEACHER", "RESEARCHREVIEWER" }, token))
                    return Denied<ResearchProposalDto>();
                var contentHash = isContent ? ContentHash(title, abstractText, protocol) : row.ContentSha256;
                var newRevision = row.CurrentRevision + (action is "submit" or "revise" ? 1 : 0);
                var supervisor = action == "assign" ? input.SupervisorUserId :
                    action == "revise" ? null : row.SupervisorUserId;
                var reviewer = action == "assign" ? input.ReviewerUserId :
                    action == "revise" ? null : row.ReviewerUserId;
                var risk = action.StartsWith("ethics-", StringComparison.Ordinal) ? input.RiskCode :
                    action == "revise" ? null : row.RiskCode;
                var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE dbo.ResearchProposals SET
                        Title = {isContent ? title : row.Title},
                        Abstract = {isContent ? abstractText : row.Abstract},
                        Protocol = {isContent ? protocol : row.Protocol},
                        ContentSha256 = {contentHash}, CurrentRevision = {newRevision},
                        StateCode = {next}, SupervisorUserId = {supervisor},
                        ReviewerUserId = {reviewer}, RiskCode = {risk}, UpdatedAtUtc = {Now}
                    WHERE TenantId = {user.TenantId} AND Id = {row.Id} AND RowVersion = {expected}
                    """, token);
                if (changed != 1) return Conflict<ResearchProposalDto>();
                if (action is "submit" or "revise")
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO dbo.ResearchProposalRevisions
                        (TenantId, ProposalId, RevisionNo, Title, Abstract, Protocol,
                         ContentSha256, SubmittedByUserId, SubmittedAtUtc)
                        VALUES ({user.TenantId}, {row.Id}, {newRevision},
                                {action == "revise" ? title : row.Title},
                                {action == "revise" ? abstractText : row.Abstract},
                                {action == "revise" ? protocol : row.Protocol},
                                {contentHash}, {user.UserId}, {Now})
                        """, token);
                var updated = await GetAsync(reference, token) ?? throw new InvalidOperationException("Research update missing row.");
                var result = Map(updated, roles);
                await RecordAsync(updated, input.ClientRequestId, requestHash, action, before, reason, result, token);
                return ApiResponse<ResearchProposalDto>.SuccessResponse(result, "Proposal command completed.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        {
            logger.LogWarning(ex, "Research state conflict for tenant {TenantId}", user.TenantId);
            return Conflict<ResearchProposalDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Research command failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<ResearchProposalDto>.ErrorResponse("Proposal command failed without saving.", 500);
        }
    }

    private bool AuthorisedAction(ProposalRow row, string action, IReadOnlySet<string> roles) => action switch
    {
        "edit" or "submit" or "revise" or "withdraw" => row.OwnerUserId == user.UserId &&
            Role(roles, "TEACHER", "RESEARCHER", "RESEARCHCOORDINATOR", "TENANTADMIN", "PRINCIPAL"),
        "assign" => Coordinator(roles) && row.OwnerUserId != user.UserId,
        "supervisor-approve" or "supervisor-revise" => row.SupervisorUserId == user.UserId && row.OwnerUserId != user.UserId,
        "review-recommend" or "review-revise" => row.ReviewerUserId == user.UserId &&
            row.OwnerUserId != user.UserId && row.SupervisorUserId != user.UserId,
        "ethics-approve" or "ethics-reject" => Ethics(roles) && row.OwnerUserId != user.UserId &&
            row.SupervisorUserId != user.UserId && row.ReviewerUserId != user.UserId,
        _ => false
    };
    private static string? NextState(ProposalRow p, string action) => (p.StateCode, action) switch
    {
        ("Draft", "edit") => "Draft",
        ("Draft", "submit") => "Submitted",
        ("Submitted", "assign") => "Assigned",
        ("Assigned", "supervisor-approve") => "SupervisorApproved",
        ("Assigned", "supervisor-revise") => "ChangesRequested",
        ("SupervisorApproved", "review-recommend") => "ReviewRecommended",
        ("SupervisorApproved", "review-revise") => "ChangesRequested",
        ("ReviewRecommended", "ethics-approve") => "EthicsApproved",
        ("ReviewRecommended", "ethics-reject") => "EthicsRejected",
        ("ChangesRequested", "revise") or ("EthicsRejected", "revise") => "Submitted",
        ("Draft", "withdraw") or ("Submitted", "withdraw") or ("ChangesRequested", "withdraw") => "Withdrawn",
        _ => null
    };

    public async Task<ApiResponse<ResearchProposalDto>> DetailAsync(Guid reference, CancellationToken ct = default)
    {
        var roles = await LiveRolesAsync(ct);
        if (reference == Guid.Empty || roles.Count == 0) return Denied<ResearchProposalDto>();
        var row = await GetAsync(reference, ct);
        return row == null || !Visible(row, roles) ? Denied<ResearchProposalDto>() :
            ApiResponse<ResearchProposalDto>.SuccessResponse(Map(row, roles));
    }

    public async Task<ApiResponse<PagedResult<ResearchProposalDto>>> ListAsync(
        long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        var roles = await LiveRolesAsync(ct);
        if (roles.Count == 0) return Denied<PagedResult<ResearchProposalDto>>();
        if (campusId is <= 0 || page is < 1 or > 100000 || pageSize is < 1 or > 50)
            return ApiResponse<PagedResult<ResearchProposalDto>>.ErrorResponse("Invalid paging filter.");
        if (campusId.HasValue && !await CampusAsync(campusId.Value, ct))
            return Denied<PagedResult<ResearchProposalDto>>();
        var visibility = " AND (p.OwnerUserId = {1} OR p.SupervisorUserId = {1} OR p.ReviewerUserId = {1}" +
            (Coordinator(roles) ? " OR 1 = 1" : "") +
            (Ethics(roles) ? " OR p.StateCode = N'ReviewRecommended'" : "") + ")";
        var filters = " AND " + CampusGrant + " AND ({2} = 0 OR p.CampusId = {2})" + visibility;
        try
        {
            var count = await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM dbo.ResearchProposals AS p WHERE p.TenantId = {0}" + filters,
                user.TenantId, user.UserId, campusId ?? 0).SingleAsync(ct);
            var rows = await db.Database.SqlQueryRaw<ProposalRow>(Columns + filters +
                " ORDER BY p.UpdatedAtUtc DESC, p.Id DESC OFFSET {3} ROWS FETCH NEXT {4} ROWS ONLY",
                user.TenantId, user.UserId, campusId ?? 0, (page - 1) * pageSize, pageSize).ToListAsync(ct);
            return ApiResponse<PagedResult<ResearchProposalDto>>.SuccessResponse(new()
            {
                Page = page, PageSize = pageSize, TotalCount = count,
                Items = rows.Select(p => Map(p, roles)).ToList()
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Research list failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<PagedResult<ResearchProposalDto>>.ErrorResponse("Research list unavailable.", 500);
        }
    }

    public async Task<ApiResponse<PagedResult<ResearchProposalHistoryDto>>> HistoryAsync(
        Guid reference, int page, int pageSize, CancellationToken ct = default)
    {
        var roles = await LiveRolesAsync(ct);
        if (reference == Guid.Empty || roles.Count == 0 || page is < 1 or > 100000 || pageSize is < 1 or > 50)
            return Denied<PagedResult<ResearchProposalHistoryDto>>();
        var proposal = await GetAsync(reference, ct);
        if (proposal == null || !Visible(proposal, roles)) return Denied<PagedResult<ResearchProposalHistoryDto>>();
        var query = "FROM dbo.ResearchProposalEvents WHERE TenantId = {0} AND ProposalId = {1}";
        var count = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value " + query,
            user.TenantId, proposal.Id).SingleAsync(ct);
        var rows = await db.Database.SqlQueryRaw<ResearchProposalHistoryDto>(
            "SELECT ActionCode, FromState, ToState, RevisionNo, Reason, OccurredAtUtc " + query +
            " ORDER BY OccurredAtUtc DESC, Id DESC OFFSET {2} ROWS FETCH NEXT {3} ROWS ONLY",
            user.TenantId, proposal.Id, (page - 1) * pageSize, pageSize).ToListAsync(ct);
        // Coordinator-only readers get status history, not confidential reviewer rationale.
        if (!Sensitive(proposal, roles))
            foreach (var row in rows) row.Reason = null;
        return ApiResponse<PagedResult<ResearchProposalHistoryDto>>.SuccessResponse(new()
        { Page = page, PageSize = pageSize, TotalCount = count, Items = rows });
    }

    private sealed class ProposalRow
    {
        public long Id { get; set; }
        public Guid Reference { get; set; }
        public long CampusId { get; set; }
        public long? AcademicProgramId { get; set; }
        public long OwnerUserId { get; set; }
        public string Title { get; set; } = "";
        public string Abstract { get; set; } = "";
        public string Protocol { get; set; } = "";
        public string ContentSha256 { get; set; } = "";
        public int CurrentRevision { get; set; }
        public string StateCode { get; set; } = "";
        public long? SupervisorUserId { get; set; }
        public long? ReviewerUserId { get; set; }
        public string? RiskCode { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public DateTime UpdatedAtUtc { get; set; }
    }
    private sealed class EventRow
    {
        public long ProposalId { get; set; }
        public long ActorUserId { get; set; }
        public string RequestSha256 { get; set; } = "";
        public string ResultSnapshot { get; set; } = "";
    }
}
