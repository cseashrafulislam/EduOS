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

/// <summary>Research outputs are immutable submissions with one independent, immutable review.</summary>
public sealed class ResearchGrantOutputService(EduOSDbContext db, ResearchWorkflowAccess access,
    TimeProvider clock, ILogger<ResearchGrantOutputService> logger) : IResearchGrantOutputService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Research output unavailable.", 404);
    private static ApiResponse<T> Conflict<T>() => ApiResponse<T>.ErrorResponse("Research output changed; refresh.", 409);
    private static bool Kind(string x) => x is "Publication" or "Dataset" or "Patent" or
        "Prototype" or "CommunityImpact" or "Other";
    private static bool ValidUrl(string? url)
    {
        if (url == null) return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
            parsed.Scheme == Uri.UriSchemeHttps && parsed.HostNameType == UriHostNameType.Dns &&
            parsed.Host.Contains('.') && parsed.UserInfo.Length == 0;
    }
    private static bool Author(GrantRow row, long user, IReadOnlySet<string> roles) =>
        row.OwnerUserId == user && ResearchWorkflowAccess.Has(roles,
            "TEACHER", "RESEARCHER", "RESEARCHCOORDINATOR", "TENANTADMIN", "PRINCIPAL");
    private static bool Checker(GrantRow row, long user, IReadOnlySet<string> roles) =>
        ResearchWorkflowAccess.ResearchCoordinator(roles) && row.OwnerUserId != user &&
        row.SupervisorUserId != user && row.ReviewerUserId != user;

    private async Task<(GrantRow? Grant, bool CanSubmit, bool CanReview)> GrantAsync(Guid reference, CancellationToken ct)
    {
        if (reference == Guid.Empty || !access.Authenticated) return (null, false, false);
        var roles = await access.RolesAsync(ct);
        if (roles.Count == 0) return (null, false, false);
        var rows = await db.Database.SqlQueryRaw<GrantRow>("""
            SELECT g.Id, g.PublicId AS Reference, p.CampusId, g.OwnerUserId,
                p.SupervisorUserId, p.ReviewerUserId, g.StateCode, p.StateCode AS ProposalState
            FROM dbo.ResearchGrants AS g
            JOIN dbo.ResearchProposals AS p ON p.Id = g.ProposalId AND p.TenantId = g.TenantId
            WHERE g.TenantId = {0} AND g.PublicId = {1}
            """, access.TenantId, reference).ToListAsync(ct);
        var grant = rows.SingleOrDefault();
        if (grant == null || !await access.CampusAsync(grant.CampusId, ct))
            return (null, false, false);
        var author = Author(grant, access.UserId, roles);
        var checker = Checker(grant, access.UserId, roles);
        return author || checker ? (grant, author, checker) : (null, false, false);
    }

    private const string OutputColumns = """
        SELECT o.Id, o.GrantId, o.PublicId AS Reference, g.PublicId AS GrantReference,
            o.SubmitRequestId, o.SubmitSha256, o.KindCode, o.Title, o.Summary,
            o.ReferenceUrl, o.MilestoneId, o.OccurredOn, o.StateCode,
            o.SubmittedByUserId, o.SubmittedAtUtc, o.DecisionRequestId,
            o.DecisionSha256, o.DecisionByUserId, o.DecisionReason,
            o.DecisionAtUtc, o.RowVersion
        FROM dbo.ResearchGrantOutputs AS o
        JOIN dbo.ResearchGrants AS g ON g.Id = o.GrantId AND g.TenantId = o.TenantId
        WHERE o.TenantId = {0}
        """;
    private async Task<OutputRow?> ByReferenceAsync(long grantId, Guid outputReference, CancellationToken ct) =>
        (await db.Database.SqlQueryRaw<OutputRow>(OutputColumns +
            " AND o.GrantId = {1} AND o.PublicId = {2}",
            access.TenantId, grantId, outputReference).ToListAsync(ct)).SingleOrDefault();
    private static ResearchGrantOutputDto Map(OutputRow row, bool reviewer) => new()
    {
        Reference = row.Reference, GrantReference = row.GrantReference, KindCode = row.KindCode,
        Title = row.Title, Summary = row.Summary, ReferenceUrl = row.ReferenceUrl,
        MilestoneId = row.MilestoneId, OccurredOn = row.OccurredOn,
        StateCode = row.StateCode, SubmittedByUserId = row.SubmittedByUserId,
        SubmittedAtUtc = row.SubmittedAtUtc, DecisionByUserId = row.DecisionByUserId,
        DecisionAtUtc = row.DecisionAtUtc, DecisionReason = row.DecisionReason,
        RowVersion = Convert.ToBase64String(row.RowVersion),
        CanReview = reviewer && row.StateCode == "Submitted"
    };

    public async Task<ApiResponse<ResearchGrantOutputDto>> SubmitAsync(Guid grantReference,
        ResearchGrantOutputSubmitDto input, CancellationToken ct = default)
    {
        if (input == null || input.ClientRequestId == Guid.Empty || !Kind(input.KindCode) ||
            (input.Title?.Trim().Length ?? 0) is < 5 or > 200 ||
            (input.Summary?.Trim().Length ?? 0) is < 20 or > 2000 ||
            input.MilestoneId is <= 0 || input.ReferenceUrl?.Length > 500 ||
            input.OccurredOn.TimeOfDay != TimeSpan.Zero || input.OccurredOn.Year < 2000 ||
            input.OccurredOn.Date > Now.Date)
            return ApiResponse<ResearchGrantOutputDto>.ErrorResponse("Invalid outcome evidence.", 400);
        var title = input.Title?.Trim() ?? "";
        var summary = input.Summary?.Trim() ?? "";
        var url = string.IsNullOrWhiteSpace(input.ReferenceUrl) ? null : input.ReferenceUrl.Trim();
        if (!ValidUrl(url) || (url == null && !input.MilestoneId.HasValue))
            return ApiResponse<ResearchGrantOutputDto>.ErrorResponse("Supply an HTTPS reference or completed milestone.", 400);
        var hash = Hash(JsonSerializer.Serialize(new object?[] { grantReference, input.KindCode,
            title, summary, url, input.MilestoneId, input.OccurredOn.Date }));
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var scope = await GrantAsync(grantReference, token);
                var grant = scope.Grant;
                if (grant == null || !scope.CanSubmit) return Denied<ResearchGrantOutputDto>();
                var prior = (await db.Database.SqlQueryRaw<OutputRow>(OutputColumns +
                    " AND o.SubmitRequestId = {1}", access.TenantId, input.ClientRequestId)
                    .ToListAsync(token)).SingleOrDefault();
                if (prior != null)
                    return prior.GrantId == grant.Id && prior.SubmittedByUserId == access.UserId &&
                        prior.SubmitSha256 == hash
                        ? ApiResponse<ResearchGrantOutputDto>.SuccessResponse(Map(prior, false), "Previously submitted outcome.")
                        : Conflict<ResearchGrantOutputDto>();
                if (grant.StateCode != "Active" || grant.ProposalState != "EthicsApproved")
                    return Conflict<ResearchGrantOutputDto>();
                if (input.MilestoneId.HasValue)
                {
                    var completed = await db.Database.SqlQueryRaw<int>("""
                        SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.ResearchGrantMilestones
                            WHERE TenantId = {0} AND GrantId = {1} AND Id = {2}
                            AND CompletedAtUtc IS NOT NULL) THEN 1 ELSE 0 END AS Value
                        """, access.TenantId, grant.Id, input.MilestoneId.Value).SingleAsync(token);
                    if (completed != 1)
                        return ApiResponse<ResearchGrantOutputDto>.ErrorResponse("Select a completed grant milestone.", 400);
                }
                var count = await db.Database.SqlQueryRaw<int>("""
                    SELECT COUNT(*) AS Value FROM dbo.ResearchGrantOutputs
                    WHERE TenantId = {0} AND GrantId = {1}
                    """, access.TenantId, grant.Id).SingleAsync(token);
                if (count >= 200)
                    return ApiResponse<ResearchGrantOutputDto>.ErrorResponse("Maximum 200 outcomes per grant.", 409);
                var outputRef = Guid.NewGuid();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO dbo.ResearchGrantOutputs
                      (TenantId, GrantId, PublicId, SubmitRequestId, SubmitSha256,
                       MilestoneId, KindCode, Title, Summary, ReferenceUrl, OccurredOn,
                       SubmittedByUserId, SubmittedAtUtc, StateCode)
                    VALUES ({access.TenantId}, {grant.Id}, {outputRef}, {input.ClientRequestId}, {hash},
                            {input.MilestoneId}, {input.KindCode}, {title}, {summary}, {url},
                            {input.OccurredOn.Date}, {access.UserId}, {Now}, {"Submitted"})
                    """, token);
                var row = await ByReferenceAsync(grant.Id, outputRef, token) ??
                    throw new InvalidOperationException("Submitted outcome is missing.");
                return ApiResponse<ResearchGrantOutputDto>.SuccessResponse(Map(row, false));
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { logger.LogWarning(ex, "Research output submission collision for tenant {TenantId}", access.TenantId); return Conflict<ResearchGrantOutputDto>(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { logger.LogError(ex, "Research output submission failed for tenant {TenantId}", access.TenantId); return ApiResponse<ResearchGrantOutputDto>.ErrorResponse("Outcome could not be submitted.", 503); }
    }

    public async Task<ApiResponse<ResearchGrantOutputDto>> ReviewAsync(Guid grantReference, Guid outputReference,
        ResearchGrantOutputReviewDto input, CancellationToken ct = default)
    {
        if (outputReference == Guid.Empty || input == null || input.ClientRequestId == Guid.Empty ||
            input.RowVersion?.Length != 12 || input.Decision is not ("verify" or "reject") ||
            (input.Reason?.Trim().Length ?? 0) is < 10 or > 500 || !input.DeclaresNoConflict)
            return ApiResponse<ResearchGrantOutputDto>.ErrorResponse("Independent review, reason and current version required.", 400);
        byte[] expected;
        try { expected = Convert.FromBase64String(input.RowVersion); }
        catch (FormatException) { return ApiResponse<ResearchGrantOutputDto>.ErrorResponse("Invalid row version.", 400); }
        if (expected.Length != 8) return ApiResponse<ResearchGrantOutputDto>.ErrorResponse("Invalid row version.", 400);
        var reason = input.Reason?.Trim() ?? "";
        var hash = Hash(JsonSerializer.Serialize(new object[] { grantReference, outputReference,
            input.Decision, input.RowVersion, reason, input.DeclaresNoConflict }));
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var scope = await GrantAsync(grantReference, token);
                var grant = scope.Grant;
                if (grant == null || !scope.CanReview) return Denied<ResearchGrantOutputDto>();
                var row = await ByReferenceAsync(grant.Id, outputReference, token);
                if (row == null || row.SubmittedByUserId == access.UserId) return Denied<ResearchGrantOutputDto>();
                if (row.DecisionRequestId == input.ClientRequestId)
                    return row.DecisionByUserId == access.UserId && row.DecisionSha256 == hash
                        ? ApiResponse<ResearchGrantOutputDto>.SuccessResponse(Map(row, true), "Previously reviewed outcome.")
                        : Conflict<ResearchGrantOutputDto>();
                if (row.StateCode != "Submitted" || grant.StateCode is not ("Active" or "ClosurePending") ||
                    grant.ProposalState != "EthicsApproved" ||
                    !CryptographicOperations.FixedTimeEquals(expected, row.RowVersion))
                    return Conflict<ResearchGrantOutputDto>();
                var state = input.Decision == "verify" ? "Verified" : "Rejected";
                var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE dbo.ResearchGrantOutputs
                    SET StateCode = {state}, DecisionRequestId = {input.ClientRequestId},
                        DecisionSha256 = {hash}, DecisionByUserId = {access.UserId},
                        DecisionReason = {reason}, DecisionAtUtc = {Now}
                    WHERE TenantId = {access.TenantId} AND GrantId = {grant.Id}
                      AND PublicId = {outputReference} AND StateCode = {"Submitted"} AND RowVersion = {expected}
                    """, token);
                if (changed != 1) return Conflict<ResearchGrantOutputDto>();
                var verified = await ByReferenceAsync(grant.Id, outputReference, token) ??
                    throw new InvalidOperationException("Reviewed outcome is missing.");
                return ApiResponse<ResearchGrantOutputDto>.SuccessResponse(Map(verified, true));
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { logger.LogWarning(ex, "Research outcome review collision for tenant {TenantId}", access.TenantId); return Conflict<ResearchGrantOutputDto>(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { logger.LogError(ex, "Research outcome review failed for tenant {TenantId}", access.TenantId); return ApiResponse<ResearchGrantOutputDto>.ErrorResponse("Review could not be saved.", 503); }
    }

    public async Task<ApiResponse<PagedResult<ResearchGrantOutputDto>>> ListAsync(Guid grantReference,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 50 || page > int.MaxValue / pageSize)
            return ApiResponse<PagedResult<ResearchGrantOutputDto>>.ErrorResponse("Invalid pagination.", 400);
        var scope = await GrantAsync(grantReference, ct);
        if (scope.Grant == null) return Denied<PagedResult<ResearchGrantOutputDto>>();
        var count = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS Value FROM dbo.ResearchGrantOutputs
            WHERE TenantId = {0} AND GrantId = {1}
            """, access.TenantId, scope.Grant.Id).SingleAsync(ct);
        var rows = await db.Database.SqlQueryRaw<OutputRow>(OutputColumns +
            " AND o.GrantId = {1} ORDER BY o.SubmittedAtUtc DESC, o.Id DESC OFFSET {2} ROWS FETCH NEXT {3} ROWS ONLY",
            access.TenantId, scope.Grant.Id, (page - 1) * pageSize, pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<ResearchGrantOutputDto>>.SuccessResponse(new()
        { Page = page, PageSize = pageSize, TotalCount = count, Items = rows.Select(x => Map(x, scope.CanReview)).ToList() });
    }

    public async Task<ApiResponse<List<ResearchGrantOutcomeReportDto>>> ReportAsync(long campusId,
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        if (!ResearchWorkflowAccess.ResearchCoordinator(await access.RolesAsync(ct)) ||
            !await access.CampusAsync(campusId, ct))
            return Denied<List<ResearchGrantOutcomeReportDto>>();
        if (from.TimeOfDay != TimeSpan.Zero || to.TimeOfDay != TimeSpan.Zero ||
            from.Year is < 2000 or > 9980 || to < from || to > from.AddYears(10))
            return ApiResponse<List<ResearchGrantOutcomeReportDto>>.ErrorResponse("Invalid report period.", 400);
        var rows = await db.Database.SqlQueryRaw<ResearchGrantOutcomeReportDto>("""
            SELECT o.KindCode, COUNT(*) AS Total,
                SUM(CASE WHEN o.StateCode = N'Submitted' THEN 1 ELSE 0 END) AS Submitted,
                SUM(CASE WHEN o.StateCode = N'Verified' THEN 1 ELSE 0 END) AS Verified,
                SUM(CASE WHEN o.StateCode = N'Rejected' THEN 1 ELSE 0 END) AS Rejected
            FROM dbo.ResearchGrantOutputs AS o
            JOIN dbo.ResearchGrants AS g ON g.Id = o.GrantId AND g.TenantId = o.TenantId
            JOIN dbo.ResearchProposals AS p ON p.Id = g.ProposalId AND p.TenantId = g.TenantId
            WHERE o.TenantId = {0} AND p.CampusId = {1} AND o.OccurredOn >= {2} AND o.OccurredOn <= {3}
            GROUP BY o.KindCode ORDER BY o.KindCode
            """, access.TenantId, campusId, from.Date, to.Date).ToListAsync(ct);
        return ApiResponse<List<ResearchGrantOutcomeReportDto>>.SuccessResponse(rows);
    }

    private sealed class GrantRow
    {
        public long Id { get; set; }
        public Guid Reference { get; set; }
        public long CampusId { get; set; }
        public long OwnerUserId { get; set; }
        public long? SupervisorUserId { get; set; }
        public long? ReviewerUserId { get; set; }
        public string StateCode { get; set; } = "";
        public string ProposalState { get; set; } = "";
    }
    private sealed class OutputRow
    {
        public long Id { get; set; }
        public long GrantId { get; set; }
        public Guid Reference { get; set; }
        public Guid GrantReference { get; set; }
        public Guid SubmitRequestId { get; set; }
        public string SubmitSha256 { get; set; } = "";
        public string KindCode { get; set; } = "";
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public string? ReferenceUrl { get; set; }
        public long? MilestoneId { get; set; }
        public DateTime OccurredOn { get; set; }
        public string StateCode { get; set; } = "";
        public long SubmittedByUserId { get; set; }
        public DateTime SubmittedAtUtc { get; set; }
        public Guid? DecisionRequestId { get; set; }
        public string? DecisionSha256 { get; set; }
        public long? DecisionByUserId { get; set; }
        public string? DecisionReason { get; set; }
        public DateTime? DecisionAtUtc { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}
