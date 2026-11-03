using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Research;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Research;

/// <summary>Budget authorization only. No payment, ledger, cash or journal is created here.</summary>
public sealed class ResearchGrantService(EduOSDbContext db, ResearchWorkflowAccess access,
    TimeProvider clock, ILogger<ResearchGrantService> logger) : IResearchGrantService
{
    private const string GrantColumns = """
        SELECT g.Id, g.PublicId AS Reference, p.PublicId AS ProposalReference,
          p.CampusId, p.StateCode AS ProposalState, p.SupervisorUserId, p.ReviewerUserId,
          g.SchemeId, g.OwnerUserId, g.Title, s.CurrencyCode, s.MaxAward,
          g.RequestedAmount, g.ApprovedAmount, g.PendingAmount, g.BudgetVersion,
          g.StateCode, g.RowVersion, g.UpdatedAtUtc
        FROM dbo.ResearchGrants AS g
        JOIN dbo.ResearchProposals AS p ON p.Id = g.ProposalId AND p.TenantId = g.TenantId
        JOIN dbo.ResearchGrantSchemes AS s ON s.Id = g.SchemeId AND s.TenantId = g.TenantId
        WHERE g.TenantId = {0}
        """;
    private const string CampusGrant = """
        EXISTS (SELECT 1 FROM dbo.UserCampusAccesses AS x
          JOIN dbo.Campuses AS c ON c.Id = x.CampusId AND c.TenantId = x.TenantId
          WHERE x.TenantId = {0} AND x.UserId = {1} AND x.CampusId = p.CampusId
            AND x.IsActive = 1 AND x.IsDeleted = 0 AND c.IsActive = 1 AND c.IsDeleted = 0)
        """;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Grant unavailable.", 404);
    private static ApiResponse<T> Conflict<T>() => ApiResponse<T>.ErrorResponse("Grant changed; refresh and retry.", 409);
    private static bool Amount(decimal value, decimal maximum) => value > 0 && value <= maximum;
    private static bool HasReason(string? text) => text?.Trim().Length is >= 10 and <= 500;
    private static bool Author(GrantRow g, long actor) => g.OwnerUserId == actor;
    private static bool Checker(GrantRow g, IReadOnlySet<string> roles, long actor) =>
        ResearchWorkflowAccess.ResearchCoordinator(roles) && g.OwnerUserId != actor &&
        g.SupervisorUserId != actor && g.ReviewerUserId != actor;

    private async Task<GrantRow?> GetAsync(Guid reference, CancellationToken ct)
    {
        var rows = await db.Database.SqlQueryRaw<GrantRow>(GrantColumns +
            " AND g.PublicId = {2} AND " + CampusGrant,
            access.TenantId, access.UserId, reference).ToListAsync(ct);
        return rows.SingleOrDefault();
    }
    private static ResearchGrantDto Map(GrantRow g, IReadOnlySet<string> roles, long actor) => new()
    {
        Reference = g.Reference, ProposalReference = g.ProposalReference, CampusId = g.CampusId,
        SchemeId = g.SchemeId, OwnerUserId = g.OwnerUserId, Title = g.Title,
        CurrencyCode = g.CurrencyCode, RequestedAmount = g.RequestedAmount,
        ApprovedAmount = g.ApprovedAmount, PendingAmount = Author(g, actor) || Checker(g, roles, actor) ? g.PendingAmount : null,
        BudgetVersion = g.BudgetVersion, StateCode = g.StateCode, RowVersion = Convert.ToBase64String(g.RowVersion),
        UpdatedAtUtc = g.UpdatedAtUtc,
        AllowedActions = new[] { "submit", "award", "reject", "request-amend", "approve-amend",
            "reject-amend", "milestone-add", "milestone-complete", "request-close", "close", "withdraw" }
            .Where(a => Next(g.StateCode, a) != null &&
                (a is "award" or "reject" or "approve-amend" or "reject-amend" or "close"
                    ? Checker(g, roles, actor) : Author(g, actor))).ToList()
    };
    private static string? Next(string state, string action) => (state, action) switch
    {
        ("Draft", "submit") => "Submitted",
        ("Draft", "withdraw") or ("Submitted", "withdraw") => "Withdrawn",
        ("Submitted", "award") => "Active",
        ("Submitted", "reject") => "Rejected",
        ("Active", "request-amend") => "AmendmentPending",
        ("AmendmentPending", "approve-amend") or ("AmendmentPending", "reject-amend") => "Active",
        ("Active", "milestone-add") or ("Active", "milestone-complete") => "Active",
        ("Active", "request-close") => "ClosurePending",
        ("ClosurePending", "close") => "Closed",
        _ => null
    };
    private async Task<GrantEventRow?> PreviousAsync(Guid id, CancellationToken ct) =>
        (await db.Database.SqlQueryRaw<GrantEventRow>("""
            SELECT GrantId, ActorUserId, RequestSha256, ResultSnapshot
            FROM dbo.ResearchGrantEvents WHERE TenantId = {0} AND ClientRequestId = {1}
            """, access.TenantId, id).ToListAsync(ct)).SingleOrDefault();

    private async Task EventAsync(GrantRow g, Guid id, string hash, string action, string from,
        string? reason, ResearchGrantDto result, CancellationToken ct)
    {
        var snapshot = JsonSerializer.Serialize(result);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.ResearchGrantEvents
              (TenantId, GrantId, ClientRequestId, RequestSha256, ActorUserId,
               ActionCode, FromState, ToState, Reason, ResultSnapshot, OccurredAtUtc)
            VALUES ({access.TenantId}, {g.Id}, {id}, {hash}, {access.UserId},
                    {action}, {from}, {result.StateCode}, {reason}, {snapshot}, {Now})
            """, ct);
    }
    private ApiResponse<ResearchGrantDto> Replay(GrantEventRow e, GrantRow g, string hash)
    {
        if (e.GrantId != g.Id || e.ActorUserId != access.UserId || e.RequestSha256 != hash)
            return Conflict<ResearchGrantDto>();
        var snapshot = JsonSerializer.Deserialize<ResearchGrantDto>(e.ResultSnapshot);
        return snapshot == null ? Conflict<ResearchGrantDto>() :
            ApiResponse<ResearchGrantDto>.SuccessResponse(snapshot, "Previously completed command.");
    }

    public async Task<ApiResponse<List<ResearchGrantSchemeDto>>> SchemesAsync(CancellationToken ct = default)
    {
        if ((await access.RolesAsync(ct)).Count == 0) return Denied<List<ResearchGrantSchemeDto>>();
        var schemes = await db.Database.SqlQueryRaw<ResearchGrantSchemeDto>("""
            SELECT Id, Code, Name, CurrencyCode, MaxAward, IsActive
            FROM dbo.ResearchGrantSchemes WHERE TenantId = {0} AND IsActive = 1
            ORDER BY Name, Id
            """, access.TenantId).ToListAsync(ct);
        return ApiResponse<List<ResearchGrantSchemeDto>>.SuccessResponse(schemes.Take(100).ToList());
    }

    public async Task<ApiResponse<ResearchGrantSchemeDto>> CreateSchemeAsync(
        ResearchGrantSchemeCreateDto input, CancellationToken ct = default)
    {
        var roles = await access.RolesAsync(ct);
        if (!ResearchWorkflowAccess.ResearchCoordinator(roles)) return Denied<ResearchGrantSchemeDto>();
        if (input == null || input.ClientRequestId == Guid.Empty)
            return ApiResponse<ResearchGrantSchemeDto>.ErrorResponse("A unique request ID is required.", 400);
        var code = input.Code?.Trim().ToUpperInvariant() ?? "";
        var name = input.Name?.Trim() ?? "";
        var currency = input.CurrencyCode?.Trim().ToUpperInvariant() ?? "";
        if (code.Length is < 2 or > 40 || !code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_') ||
            name.Length is < 3 or > 200 || currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper) ||
            !Amount(input.MaxAward, 99999999999999999.99m))
            return ApiResponse<ResearchGrantSchemeDto>.ErrorResponse("Invalid scheme settings.", 400);
        var hash = Hash(JsonSerializer.Serialize(new object[] { code, name, currency, input.MaxAward }));
        try
        {
            var previous = (await db.Database.SqlQueryRaw<SchemeCreateRow>("""
                SELECT Id, CreateSha256, CreatedByUserId FROM dbo.ResearchGrantSchemes
                WHERE TenantId = {0} AND CreateRequestId = {1}
                """, access.TenantId, input.ClientRequestId).ToListAsync(ct)).SingleOrDefault();
            if (previous != null)
            {
                if (previous.CreateSha256 != hash || previous.CreatedByUserId != access.UserId)
                    return Conflict<ResearchGrantSchemeDto>();
                var replay = (await db.Database.SqlQueryRaw<ResearchGrantSchemeDto>("""
                    SELECT Id, Code, Name, CurrencyCode, MaxAward, IsActive
                    FROM dbo.ResearchGrantSchemes WHERE TenantId = {0} AND Id = {1}
                    """, access.TenantId, previous.Id).ToListAsync(ct)).SingleOrDefault();
                return replay == null ? Conflict<ResearchGrantSchemeDto>() :
                    ApiResponse<ResearchGrantSchemeDto>.SuccessResponse(replay);
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.ResearchGrantSchemes
                  (TenantId, Code, CreateRequestId, CreateSha256, Name, CurrencyCode, MaxAward,
                   IsActive, CreatedByUserId, CreatedAtUtc)
                VALUES ({access.TenantId}, {code}, {input.ClientRequestId}, {hash},
                        {name}, {currency}, {input.MaxAward}, {true}, {access.UserId}, {Now})
                """, ct);
            var scheme = (await db.Database.SqlQueryRaw<ResearchGrantSchemeDto>("""
                SELECT Id, Code, Name, CurrencyCode, MaxAward, IsActive
                FROM dbo.ResearchGrantSchemes WHERE TenantId = {0} AND Code = {1}
                """, access.TenantId, code).ToListAsync(ct)).Single();
            return ApiResponse<ResearchGrantSchemeDto>.SuccessResponse(scheme);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        { return ApiResponse<ResearchGrantSchemeDto>.ErrorResponse("Scheme code already exists.", 409); }
    }

    public async Task<ApiResponse<ResearchGrantDto>> CreateAsync(ResearchGrantCreateDto input, CancellationToken ct = default)
    {
        if (input == null || input.ClientRequestId == Guid.Empty || input.ProposalReference == Guid.Empty || input.SchemeId <= 0 ||
            (input.Title?.Trim().Length ?? 0) is < 5 or > 200 || input.RequestedAmount <= 0)
            return ApiResponse<ResearchGrantDto>.ErrorResponse("Invalid grant application.", 400);
        var title = input.Title.Trim();
        var hash = Hash(JsonSerializer.Serialize(new object[] { "create", input.ProposalReference, input.SchemeId, title, input.RequestedAmount }));
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var roles = await access.RolesAsync(token);
                if (!ResearchWorkflowAccess.Has(roles, "TEACHER", "RESEARCHER", "RESEARCHCOORDINATOR", "TENANTADMIN", "PRINCIPAL"))
                    return Denied<ResearchGrantDto>();
                var prior = await PreviousAsync(input.ClientRequestId, token);
                if (prior != null)
                {
                    var old = (await db.Database.SqlQueryRaw<GrantRow>(GrantColumns +
                        " AND g.Id = {2} AND " + CampusGrant,
                        access.TenantId, access.UserId, prior.GrantId).ToListAsync(token)).SingleOrDefault();
                    return old == null || !Author(old, access.UserId) ? Denied<ResearchGrantDto>() : Replay(prior, old, hash);
                }
                var proposal = (await db.Database.SqlQueryRaw<ProposalGrantRow>("""
                    SELECT p.Id, p.CampusId, p.OwnerUserId, p.StateCode
                    FROM dbo.ResearchProposals AS p WHERE p.TenantId = {0} AND p.PublicId = {1}
                    """, access.TenantId, input.ProposalReference).ToListAsync(token)).SingleOrDefault();
                if (proposal == null || proposal.OwnerUserId != access.UserId ||
                    proposal.StateCode != "EthicsApproved" || !await access.CampusAsync(proposal.CampusId, token))
                    return Denied<ResearchGrantDto>();
                var scheme = (await db.Database.SqlQueryRaw<ResearchGrantSchemeDto>("""
                    SELECT Id, Code, Name, CurrencyCode, MaxAward, IsActive
                    FROM dbo.ResearchGrantSchemes WHERE TenantId = {0} AND Id = {1} AND IsActive = 1
                    """, access.TenantId, input.SchemeId).ToListAsync(token)).SingleOrDefault();
                if (scheme == null || !Amount(input.RequestedAmount, scheme.MaxAward))
                    return ApiResponse<ResearchGrantDto>.ErrorResponse("Requested amount exceeds the scheme limit.", 400);
                var reference = Guid.NewGuid();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO dbo.ResearchGrants
                      (TenantId, PublicId, ProposalId, SchemeId, OwnerUserId, Title,
                       RequestedAmount, BudgetVersion, StateCode, CreatedAtUtc, UpdatedAtUtc)
                    VALUES ({access.TenantId}, {reference}, {proposal.Id}, {input.SchemeId}, {access.UserId},
                            {title}, {input.RequestedAmount}, {0}, {"Draft"}, {Now}, {Now})
                    """, token);
                var row = await GetAsync(reference, token) ?? throw new InvalidOperationException("Grant insert not found.");
                var result = Map(row, roles, access.UserId);
                await EventAsync(row, input.ClientRequestId, hash, "create", "None", null, result, token);
                return ApiResponse<ResearchGrantDto>.SuccessResponse(result);
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { logger.LogWarning(ex, "Research grant create conflict for tenant {TenantId}", access.TenantId); return Conflict<ResearchGrantDto>(); }
        catch (Exception ex)
        { logger.LogError(ex, "Research grant create failed for tenant {TenantId}", access.TenantId); return ApiResponse<ResearchGrantDto>.ErrorResponse("Grant unavailable.", 500); }
    }

    public async Task<ApiResponse<ResearchGrantDto>> CommandAsync(Guid reference, string action,
        ResearchGrantCommandDto input, CancellationToken ct = default)
    {
        action = action?.Trim().ToLowerInvariant() ?? "";
        var valid = new[] { "submit", "award", "reject", "request-amend", "approve-amend", "reject-amend",
            "milestone-add", "milestone-complete", "request-close", "close", "withdraw" };
        if (reference == Guid.Empty || input == null || input.ClientRequestId == Guid.Empty || !valid.Contains(action) ||
            input.RowVersion?.Length != 12 || input.Reason?.Length > 500 || input.Outcome?.Length > 2000)
            return ApiResponse<ResearchGrantDto>.ErrorResponse("Invalid grant command.", 400);
        byte[] expected;
        try { expected = Convert.FromBase64String(input.RowVersion); }
        catch (FormatException) { return ApiResponse<ResearchGrantDto>.ErrorResponse("Invalid version.", 400); }
        if (expected.Length != 8) return ApiResponse<ResearchGrantDto>.ErrorResponse("Invalid version.", 400);
        var reason = input.Reason?.Trim();
        if ((action is "award" or "reject" or "request-amend" or "approve-amend" or "reject-amend" or "request-close" or "close") && !HasReason(reason))
            return ApiResponse<ResearchGrantDto>.ErrorResponse("Reason of at least 10 characters required.", 400);
        if (action == "milestone-add" && ((input.MilestoneTitle?.Trim().Length ?? 0) is < 5 or > 200 ||
            input.DueDate == null || input.DueDate.Value.TimeOfDay != TimeSpan.Zero))
            return ApiResponse<ResearchGrantDto>.ErrorResponse("Valid milestone title and due date required.", 400);
        if (action == "milestone-complete" && (input.MilestoneId is not > 0 ||
            (input.Outcome?.Trim().Length ?? 0) is < 10 or > 2000))
            return ApiResponse<ResearchGrantDto>.ErrorResponse("Milestone and outcome required.", 400);
        var hash = Hash(JsonSerializer.Serialize(new object?[] { reference, action, input.RowVersion,
            input.Amount, input.MilestoneTitle?.Trim(), input.DueDate, input.MilestoneId, input.Outcome?.Trim(), reason }));
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var roles = await access.RolesAsync(token);
                var row = await GetAsync(reference, token);
                if (row == null || roles.Count == 0 || !await access.CampusAsync(row.CampusId, token) ||
                    !(Author(row, access.UserId) || Checker(row, roles, access.UserId))) return Denied<ResearchGrantDto>();
                var checkerAction = action is "award" or "reject" or "approve-amend" or "reject-amend" or "close";
                if (checkerAction ? !Checker(row, roles, access.UserId) : !Author(row, access.UserId))
                    return Denied<ResearchGrantDto>();
                var prior = await PreviousAsync(input.ClientRequestId, token);
                if (prior != null) return Replay(prior, row, hash);
                if (!CryptographicOperations.FixedTimeEquals(expected, row.RowVersion)) return Conflict<ResearchGrantDto>();
                var next = Next(row.StateCode, action);
                if (next == null) return Conflict<ResearchGrantDto>();
                if (row.ProposalState != "EthicsApproved") return Conflict<ResearchGrantDto>();
                var award = row.ApprovedAmount;
                var pending = row.PendingAmount;
                var budgetVersion = row.BudgetVersion;
                if (action == "award")
                {
                    if (input.Amount is not > 0 || !Amount(input.Amount.Value, row.MaxAward))
                        return ApiResponse<ResearchGrantDto>.ErrorResponse("Award outside scheme limit.", 400);
                    award = input.Amount; budgetVersion++;
                }
                if (action == "request-amend")
                {
                    if (input.Amount is not > 0 || !Amount(input.Amount.Value, row.MaxAward) || input.Amount == award)
                        return ApiResponse<ResearchGrantDto>.ErrorResponse("Invalid budget amendment.", 400);
                    pending = input.Amount;
                }
                if (action == "approve-amend")
                {
                    if (pending is not > 0) return Conflict<ResearchGrantDto>();
                    award = pending; pending = null; budgetVersion++;
                }
                if (action == "reject-amend") pending = null;
                if (action == "request-close")
                {
                    var incomplete = await db.Database.SqlQueryRaw<int>("""
                        SELECT COUNT(*) AS Value FROM dbo.ResearchGrantMilestones
                        WHERE TenantId = {0} AND GrantId = {1} AND CompletedAtUtc IS NULL
                        """, access.TenantId, row.Id).SingleAsync(token);
                    var total = await db.Database.SqlQueryRaw<int>("""
                        SELECT COUNT(*) AS Value FROM dbo.ResearchGrantMilestones
                        WHERE TenantId = {0} AND GrantId = {1}
                        """, access.TenantId, row.Id).SingleAsync(token);
                    if (incomplete > 0 || total == 0)
                        return ApiResponse<ResearchGrantDto>.ErrorResponse("Complete at least one milestone before closure.", 409);
                }
                if (action == "milestone-complete")
                {
                    var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE dbo.ResearchGrantMilestones
                        SET Outcome = {input.Outcome!.Trim()}, CompletedByUserId = {access.UserId}, CompletedAtUtc = {Now}
                        WHERE TenantId = {access.TenantId} AND GrantId = {row.Id}
                          AND Id = {input.MilestoneId} AND CompletedAtUtc IS NULL
                        """, token);
                    if (changed != 1) return Conflict<ResearchGrantDto>();
                }
                if (action == "milestone-add")
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO dbo.ResearchGrantMilestones
                        (TenantId, GrantId, Title, DueDate, CreatedAtUtc)
                        VALUES ({access.TenantId}, {row.Id}, {input.MilestoneTitle!.Trim()}, {input.DueDate!.Value.Date}, {Now})
                        """, token);
                var updatedRows = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE dbo.ResearchGrants SET StateCode = {next}, ApprovedAmount = {award},
                      PendingAmount = {pending}, BudgetVersion = {budgetVersion}, UpdatedAtUtc = {Now}
                    WHERE TenantId = {access.TenantId} AND Id = {row.Id} AND RowVersion = {expected}
                    """, token);
                if (updatedRows != 1) return Conflict<ResearchGrantDto>();
                if (action is "award" or "approve-amend")
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO dbo.ResearchGrantBudgets
                          (TenantId, GrantId, VersionNo, ApprovedAmount, CurrencyCode,
                           ApprovedByUserId, Reason, ApprovedAtUtc)
                        VALUES ({access.TenantId}, {row.Id}, {budgetVersion}, {award}, {row.CurrencyCode},
                                {access.UserId}, {reason!}, {Now})
                        """, token);
                var updated = await GetAsync(reference, token) ?? throw new InvalidOperationException("Grant disappeared.");
                var result = Map(updated, roles, access.UserId);
                await EventAsync(updated, input.ClientRequestId, hash, action, row.StateCode, reason, result, token);
                return ApiResponse<ResearchGrantDto>.SuccessResponse(result);
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { logger.LogWarning(ex, "Research grant conflict for tenant {TenantId}", access.TenantId); return Conflict<ResearchGrantDto>(); }
        catch (Exception ex)
        { logger.LogError(ex, "Research grant operation failed for tenant {TenantId}", access.TenantId); return ApiResponse<ResearchGrantDto>.ErrorResponse("Grant command failed without saving.", 500); }
    }

    public async Task<ApiResponse<ResearchGrantDto>> DetailAsync(Guid reference, CancellationToken ct = default)
    {
        var roles = await access.RolesAsync(ct);
        if (roles.Count == 0 || reference == Guid.Empty) return Denied<ResearchGrantDto>();
        var row = await GetAsync(reference, ct);
        return row == null || !(Author(row, access.UserId) || Checker(row, roles, access.UserId))
            ? Denied<ResearchGrantDto>() : ApiResponse<ResearchGrantDto>.SuccessResponse(Map(row, roles, access.UserId));
    }

    public async Task<ApiResponse<PagedResult<ResearchGrantDto>>> ListAsync(long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        var roles = await access.RolesAsync(ct);
        if (roles.Count == 0) return Denied<PagedResult<ResearchGrantDto>>();
        if (campusId is <= 0 || page is < 1 or > 100000 || pageSize is < 1 or > 50 || page > int.MaxValue / pageSize)
            return ApiResponse<PagedResult<ResearchGrantDto>>.ErrorResponse("Invalid pagination.", 400);
        if (campusId.HasValue && !await access.CampusAsync(campusId.Value, ct))
            return Denied<PagedResult<ResearchGrantDto>>();
        var condition = " AND " + CampusGrant + " AND ({2} = 0 OR p.CampusId = {2})" +
            (ResearchWorkflowAccess.ResearchCoordinator(roles)
                ? " AND (g.OwnerUserId = {1} OR (g.OwnerUserId <> {1} AND (p.SupervisorUserId IS NULL OR p.SupervisorUserId <> {1}) AND (p.ReviewerUserId IS NULL OR p.ReviewerUserId <> {1})))"
                : " AND g.OwnerUserId = {1}");
        var count = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS Value FROM dbo.ResearchGrants AS g
            JOIN dbo.ResearchProposals AS p ON p.Id = g.ProposalId AND p.TenantId = g.TenantId
            WHERE g.TenantId = {0}
            """ + condition, access.TenantId, access.UserId, campusId ?? 0).SingleAsync(ct);
        var rows = await db.Database.SqlQueryRaw<GrantRow>(GrantColumns + condition +
            " ORDER BY g.UpdatedAtUtc DESC, g.Id DESC OFFSET {3} ROWS FETCH NEXT {4} ROWS ONLY",
            access.TenantId, access.UserId, campusId ?? 0, (page - 1) * pageSize, pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<ResearchGrantDto>>.SuccessResponse(new()
        { Page = page, PageSize = pageSize, TotalCount = count, Items = rows.Select(x => Map(x, roles, access.UserId)).ToList() });
    }

    public async Task<ApiResponse<List<ResearchGrantMilestoneDto>>> MilestonesAsync(Guid reference, CancellationToken ct = default)
    {
        var detail = await DetailAsync(reference, ct);
        if (!detail.Success) return Denied<List<ResearchGrantMilestoneDto>>();
        var milestones = await db.Database.SqlQueryRaw<ResearchGrantMilestoneDto>("""
            SELECT m.Id, m.Title, m.DueDate, m.Outcome, m.CompletedAtUtc
            FROM dbo.ResearchGrantMilestones AS m
            JOIN dbo.ResearchGrants AS g ON g.Id = m.GrantId AND g.TenantId = m.TenantId
            WHERE m.TenantId = {0} AND g.PublicId = {1}
            ORDER BY m.DueDate, m.Id
            """, access.TenantId, reference).ToListAsync(ct);
        return ApiResponse<List<ResearchGrantMilestoneDto>>.SuccessResponse(milestones.Take(200).ToList());
    }

    public async Task<ApiResponse<PagedResult<ResearchGrantHistoryDto>>> HistoryAsync(Guid reference,
        int page, int pageSize, CancellationToken ct = default)
    {
        var detail = await DetailAsync(reference, ct);
        if (!detail.Success || page is < 1 or > 100000 || pageSize is < 1 or > 50 || page > int.MaxValue / pageSize)
            return Denied<PagedResult<ResearchGrantHistoryDto>>();
        var query = " FROM dbo.ResearchGrantEvents AS e JOIN dbo.ResearchGrants AS g ON g.Id = e.GrantId AND g.TenantId = e.TenantId WHERE e.TenantId = {0} AND g.PublicId = {1}";
        var count = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value" + query,
            access.TenantId, reference).SingleAsync(ct);
        var rows = await db.Database.SqlQueryRaw<ResearchGrantHistoryDto>(
            "SELECT e.ActionCode, e.FromState, e.ToState, e.Reason, e.OccurredAtUtc" + query +
            " ORDER BY e.OccurredAtUtc DESC, e.Id DESC OFFSET {2} ROWS FETCH NEXT {3} ROWS ONLY",
            access.TenantId, reference, (page - 1) * pageSize, pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<ResearchGrantHistoryDto>>.SuccessResponse(new()
        { Page = page, PageSize = pageSize, TotalCount = count, Items = rows });
    }

    private sealed class ProposalGrantRow
    {
        public long Id { get; set; }
        public long CampusId { get; set; }
        public long OwnerUserId { get; set; }
        public string StateCode { get; set; } = "";
    }
    private sealed class SchemeCreateRow
    {
        public long Id { get; set; }
        public string CreateSha256 { get; set; } = "";
        public long CreatedByUserId { get; set; }
    }
    private sealed class GrantRow
    {
        public long Id { get; set; }
        public Guid Reference { get; set; }
        public Guid ProposalReference { get; set; }
        public long CampusId { get; set; }
        public string ProposalState { get; set; } = "";
        public long? SupervisorUserId { get; set; }
        public long? ReviewerUserId { get; set; }
        public long SchemeId { get; set; }
        public long OwnerUserId { get; set; }
        public string Title { get; set; } = "";
        public string CurrencyCode { get; set; } = "";
        public decimal MaxAward { get; set; }
        public decimal RequestedAmount { get; set; }
        public decimal? ApprovedAmount { get; set; }
        public decimal? PendingAmount { get; set; }
        public int BudgetVersion { get; set; }
        public string StateCode { get; set; } = "";
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public DateTime UpdatedAtUtc { get; set; }
    }
    private sealed class GrantEventRow
    {
        public long GrantId { get; set; }
        public long ActorUserId { get; set; }
        public string RequestSha256 { get; set; } = "";
        public string ResultSnapshot { get; set; } = "";
    }
}
