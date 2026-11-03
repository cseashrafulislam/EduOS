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

/// <summary>Published standards and submitted evidence are immutable. Reviewer approval is independent.</summary>
public sealed class AccreditationService(EduOSDbContext db, ResearchWorkflowAccess access,
    TimeProvider clock, ILogger<AccreditationService> logger) : IAccreditationService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Accreditation record unavailable.", 404);
    private static ApiResponse<T> Conflict<T>() => ApiResponse<T>.ErrorResponse("Record changed; refresh and retry.", 409);
    private static bool Text(string? value, int min, int max) =>
        value != null && value.Trim().Length >= min && value.Trim().Length <= max;
    private static bool Code(string code) => code.Length is >= 2 and <= 40 &&
        code.All(x => char.IsAsciiLetterUpper(x) || char.IsAsciiDigit(x) || x == '_');
    private static bool Author(AssessmentRow row, long actor) => row.OwnerUserId == actor;
    private static bool Checker(AssessmentRow row, IReadOnlySet<string> roles, long actor) =>
        ResearchWorkflowAccess.AccreditationCoordinator(roles) && row.OwnerUserId != actor;
    private static bool Reviewer(AssessmentRow row, IReadOnlySet<string> roles, long actor) =>
        row.ActorIsReviewer && row.OwnerUserId != actor &&
        ResearchWorkflowAccess.Has(roles, "ACCREDITATIONREVIEWER", "TEACHER", "PRINCIPAL");
    private static bool Visible(AssessmentRow row, IReadOnlySet<string> roles, long actor) =>
        Author(row, actor) || Checker(row, roles, actor) || Reviewer(row, roles, actor);
    private static bool Sensitive(AssessmentRow row, IReadOnlySet<string> roles, long actor) =>
        Author(row, actor) || Reviewer(row, roles, actor);

    public async Task<ApiResponse<List<ResearchCampusDto>>> CampusesAsync(CancellationToken ct = default)
    {
        if ((await access.RolesAsync(ct)).Count == 0) return Denied<List<ResearchCampusDto>>();
        var campuses = await db.Campuses.AsNoTracking().Where(c => c.TenantId == access.TenantId &&
            c.IsActive && !c.IsDeleted && db.UserCampusAccesses.Any(g => g.TenantId == access.TenantId &&
                g.UserId == access.UserId && g.CampusId == c.Id && g.IsActive && !g.IsDeleted))
            .OrderBy(c => c.Name).Take(100).Select(c => new ResearchCampusDto
            { Id = c.Id, Name = c.Name }).ToListAsync(ct);
        return ApiResponse<List<ResearchCampusDto>>.SuccessResponse(campuses);
    }

    public async Task<ApiResponse<List<ResearchProgramDto>>> ProgramsAsync(long campusId, CancellationToken ct = default)
    {
        if ((await access.RolesAsync(ct)).Count == 0 || !await access.CampusAsync(campusId, ct))
            return Denied<List<ResearchProgramDto>>();
        var programs = await (from pc in db.ProgramCampuses.AsNoTracking()
            join p in db.AcademicPrograms.AsNoTracking() on pc.AcademicProgramId equals p.Id
            where pc.TenantId == access.TenantId && p.TenantId == access.TenantId &&
                  pc.CampusId == campusId && pc.IsActive && !pc.IsDeleted && p.IsActive && !p.IsDeleted
            orderby p.Name
            select new ResearchProgramDto { Id = p.Id, Name = p.Name }).Take(100).ToListAsync(ct);
        return ApiResponse<List<ResearchProgramDto>>.SuccessResponse(programs);
    }

    public async Task<ApiResponse<List<ResearchReviewerDto>>> ReviewersAsync(long campusId, string? search, CancellationToken ct = default)
    {
        var roles = await access.RolesAsync(ct);
        if (!ResearchWorkflowAccess.AccreditationCoordinator(roles) || !await access.CampusAsync(campusId, ct) || search?.Length > 100)
            return Denied<List<ResearchReviewerDto>>();
        var term = search?.Trim() ?? "";
        var rows = await db.Users.AsNoTracking().Where(u => u.IsActive && !u.IsDeleted &&
            u.Id != access.UserId && (term == "" || u.FullName.Contains(term)) &&
            db.TenantMemberships.Any(m => m.TenantId == access.TenantId && m.UserId == u.Id &&
                m.Status == EduOS.Core.Enums.Domain.MembershipStatus.Active && !m.IsDeleted) &&
            db.UserCampusAccesses.Any(g => g.TenantId == access.TenantId && g.UserId == u.Id &&
                g.CampusId == campusId && g.IsActive && !g.IsDeleted) &&
            db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.IgnoreQueryFilters().Any(r =>
                r.Id == ur.RoleId && r.IsActive && !r.IsDeleted &&
                (r.TenantId == access.TenantId || (r.TenantId == null && !r.IsSystemRole &&
                    !db.TenantMemberships.Any(other => other.UserId == u.Id && other.TenantId != access.TenantId &&
                        other.Status == EduOS.Core.Enums.Domain.MembershipStatus.Active && !other.IsDeleted))) &&
                (r.NormalizedName == "ACCREDITATIONREVIEWER" || r.NormalizedName == "TEACHER" || r.NormalizedName == "PRINCIPAL"))))
            .OrderBy(u => u.FullName).ThenBy(u => u.Id).Take(50)
            .Select(u => new ResearchReviewerDto { UserId = u.Id, Name = u.FullName, CanReview = true })
            .ToListAsync(ct);
        return ApiResponse<List<ResearchReviewerDto>>.SuccessResponse(rows);
    }

    private const string AssessmentColumns = """
        SELECT a.Id, a.PublicId AS Reference, a.CampusId, a.AcademicProgramId,
          a.StandardId, s.Code AS StandardCode, f.Code AS FrameworkCode, f.VersionNo AS FrameworkVersion,
          s.ValidityDays, s.RequiredReviewerCount, a.OwnerUserId, a.ReviewerUserId, a.PeriodStart, a.PeriodEnd,
          a.CurrentRevision, a.EvidenceSummary, a.EvidenceSha256, a.StateCode,
          a.ApprovedAtUtc, a.ExpiresAtUtc, a.RowVersion, a.UpdatedAtUtc,
          (SELECT COUNT(*) FROM dbo.AccreditationReviewAssignments AS ra
             WHERE ra.TenantId = a.TenantId AND ra.AssessmentId = a.Id
               AND ra.RevisionNo = a.CurrentRevision) AS AssignedReviewerCount,
          (SELECT COUNT(*) FROM dbo.AccreditationReviewAssignments AS ra
             WHERE ra.TenantId = a.TenantId AND ra.AssessmentId = a.Id
               AND ra.RevisionNo = a.CurrentRevision AND ra.DecisionCode = N'Approved') AS ApprovalsReceived,
          CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.AccreditationReviewAssignments AS ra
             WHERE ra.TenantId = a.TenantId AND ra.AssessmentId = a.Id
               AND ra.RevisionNo = a.CurrentRevision AND ra.ReviewerUserId = {1})
             THEN 1 ELSE 0 END AS bit) AS ActorIsReviewer,
          CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.AccreditationReviewAssignments AS ra
             WHERE ra.TenantId = a.TenantId AND ra.AssessmentId = a.Id
               AND ra.RevisionNo = a.CurrentRevision AND ra.ReviewerUserId = {1}
               AND ra.DecisionCode = N'Pending') THEN 1 ELSE 0 END AS bit) AS ActorReviewPending
        FROM dbo.AccreditationAssessments AS a
        JOIN dbo.AccreditationStandards AS s ON s.Id = a.StandardId AND s.TenantId = a.TenantId
        JOIN dbo.AccreditationFrameworks AS f ON f.Id = s.FrameworkId AND f.TenantId = s.TenantId
        WHERE a.TenantId = {0}
        """;
    private const string CampusGrant = """
        EXISTS (SELECT 1 FROM dbo.UserCampusAccesses AS g
            JOIN dbo.Campuses AS c ON c.Id = g.CampusId AND c.TenantId = g.TenantId
            WHERE g.TenantId = {0} AND g.UserId = {1} AND g.CampusId = a.CampusId
              AND g.IsActive = 1 AND g.IsDeleted = 0 AND c.IsActive = 1 AND c.IsDeleted = 0)
        """;
    private async Task<AssessmentRow?> GetAsync(Guid reference, CancellationToken ct) =>
        (await db.Database.SqlQueryRaw<AssessmentRow>(AssessmentColumns + " AND a.PublicId = {2} AND " + CampusGrant,
            access.TenantId, access.UserId, reference).ToListAsync(ct)).SingleOrDefault();

    private static AccreditationFrameworkDto Framework(FrameworkRow row) => new()
    {
        Id = row.Id, Code = row.Code, Name = row.Name,
        VersionNo = row.VersionNo, StateCode = row.StateCode,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };
    private async Task<FrameworkRow?> FrameworkAsync(long id, CancellationToken ct) =>
        (await db.Database.SqlQueryRaw<FrameworkRow>("""
            SELECT Id, Code, Name, VersionNo, StateCode, CreateRequestId,
              CreateSha256, PublishRequestId, PublishSha256, CreatedByUserId, RowVersion
            FROM dbo.AccreditationFrameworks WHERE TenantId = {0} AND Id = {1}
            """, access.TenantId, id).ToListAsync(ct)).SingleOrDefault();

    public async Task<ApiResponse<List<AccreditationFrameworkDto>>> FrameworksAsync(CancellationToken ct = default)
    {
        var roles = await access.RolesAsync(ct);
        if (roles.Count == 0) return Denied<List<AccreditationFrameworkDto>>();
        var rows = await db.Database.SqlQueryRaw<FrameworkRow>("""
            SELECT TOP(100) Id, Code, Name, VersionNo, StateCode, CreateRequestId,
              CreateSha256, PublishRequestId, PublishSha256, CreatedByUserId, RowVersion
            FROM dbo.AccreditationFrameworks WHERE TenantId = {0} AND
              (StateCode = N'Published' OR {1} = 1)
            ORDER BY Code, VersionNo DESC
            """, access.TenantId, ResearchWorkflowAccess.AccreditationCoordinator(roles) ? 1 : 0).ToListAsync(ct);
        return ApiResponse<List<AccreditationFrameworkDto>>.SuccessResponse(rows.Select(Framework).ToList());
    }

    public async Task<ApiResponse<AccreditationFrameworkDto>> CreateFrameworkAsync(
        AccreditationFrameworkCreateDto input, CancellationToken ct = default)
    {
        var roles = await access.RolesAsync(ct);
        if (!ResearchWorkflowAccess.AccreditationCoordinator(roles)) return Denied<AccreditationFrameworkDto>();
        if (input == null) return ApiResponse<AccreditationFrameworkDto>.ErrorResponse("Framework required.", 400);
        var code = input.Code?.Trim().ToUpperInvariant() ?? "";
        var name = input.Name?.Trim() ?? "";
        if (input.ClientRequestId == Guid.Empty || !Code(code) || !Text(name, 5, 200) || input.VersionNo is < 1 or > 1000)
            return ApiResponse<AccreditationFrameworkDto>.ErrorResponse("Invalid framework version.", 400);
        var hash = Hash(JsonSerializer.Serialize(new object[] { code, name, input.VersionNo }));
        try
        {
            var existing = (await db.Database.SqlQueryRaw<FrameworkRow>("""
                SELECT Id, Code, Name, VersionNo, StateCode, CreateRequestId,
                  CreateSha256, PublishRequestId, PublishSha256, CreatedByUserId, RowVersion
                FROM dbo.AccreditationFrameworks WHERE TenantId = {0} AND CreateRequestId = {1}
                """, access.TenantId, input.ClientRequestId).ToListAsync(ct)).SingleOrDefault();
            if (existing != null) return existing.CreateSha256 == hash && existing.CreatedByUserId == access.UserId
                ? ApiResponse<AccreditationFrameworkDto>.SuccessResponse(Framework(existing)) : Conflict<AccreditationFrameworkDto>();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.AccreditationFrameworks
                  (TenantId, Code, Name, VersionNo, StateCode, CreateRequestId, CreateSha256,
                   CreatedByUserId, CreatedAtUtc)
                VALUES ({access.TenantId}, {code}, {name}, {input.VersionNo}, {"Draft"},
                        {input.ClientRequestId}, {hash}, {access.UserId}, {Now})
                """, ct);
            var row = (await db.Database.SqlQueryRaw<FrameworkRow>("""
                SELECT Id, Code, Name, VersionNo, StateCode, CreateRequestId,
                  CreateSha256, PublishRequestId, PublishSha256, CreatedByUserId, RowVersion
                FROM dbo.AccreditationFrameworks WHERE TenantId = {0} AND CreateRequestId = {1}
                """, access.TenantId, input.ClientRequestId).ToListAsync(ct)).Single();
            return ApiResponse<AccreditationFrameworkDto>.SuccessResponse(Framework(row));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        { logger.LogWarning(ex, "Framework version conflict for tenant {TenantId}", access.TenantId); return Conflict<AccreditationFrameworkDto>(); }
    }

    public async Task<ApiResponse<AccreditationStandardDto>> AddStandardAsync(
        AccreditationStandardCreateDto input, CancellationToken ct = default)
    {
        var roles = await access.RolesAsync(ct);
        if (!ResearchWorkflowAccess.AccreditationCoordinator(roles)) return Denied<AccreditationStandardDto>();
        if (input == null) return ApiResponse<AccreditationStandardDto>.ErrorResponse("Standard required.", 400);
        var code = input.Code?.Trim().ToUpperInvariant() ?? "";
        var title = input.Title?.Trim() ?? "";
        var rubric = input.Rubric?.Trim() ?? "";
        var evidence = input.RequiredEvidence?.Trim() ?? "";
        if (input.ClientRequestId == Guid.Empty || input.FrameworkId <= 0 || !Code(code) ||
            !Text(title, 5, 200) || !Text(rubric, 10, 2000) || !Text(evidence, 10, 2000) ||
            input.ValidityDays is < 1 or > 3650 || input.RequiredReviewerCount is < 1 or > 3)
            return ApiResponse<AccreditationStandardDto>.ErrorResponse("Invalid standard configuration.", 400);
        var hash = Hash(JsonSerializer.Serialize(new object[] { input.FrameworkId, code, title, rubric, evidence, input.ValidityDays, input.RequiredReviewerCount }));
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (!ResearchWorkflowAccess.AccreditationCoordinator(await access.RolesAsync(token))) return Denied<AccreditationStandardDto>();
                var previous = (await db.Database.SqlQueryRaw<StandardCreateRow>("""
                    SELECT Id, CreateSha256 FROM dbo.AccreditationStandards
                    WHERE TenantId = {0} AND CreateRequestId = {1}
                    """, access.TenantId, input.ClientRequestId).ToListAsync(token)).SingleOrDefault();
                if (previous != null)
                {
                    if (previous.CreateSha256 != hash) return Conflict<AccreditationStandardDto>();
                    var replay = await StandardAsync(previous.Id, token);
                    return replay == null ? Conflict<AccreditationStandardDto>() :
                        ApiResponse<AccreditationStandardDto>.SuccessResponse(replay);
                }
                var framework = await FrameworkAsync(input.FrameworkId, token);
                if (framework == null || framework.StateCode != "Draft") return Conflict<AccreditationStandardDto>();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO dbo.AccreditationStandards
                      (TenantId, FrameworkId, CreateRequestId, CreateSha256,
                       Code, Title, Rubric, RequiredEvidence, ValidityDays, RequiredReviewerCount)
                    VALUES ({access.TenantId}, {input.FrameworkId}, {input.ClientRequestId}, {hash},
                            {code}, {title}, {rubric}, {evidence}, {input.ValidityDays}, {input.RequiredReviewerCount})
                    """, token);
                var row = (await db.Database.SqlQueryRaw<StandardCreateRow>("""
                    SELECT Id, CreateSha256 FROM dbo.AccreditationStandards
                    WHERE TenantId = {0} AND CreateRequestId = {1}
                    """, access.TenantId, input.ClientRequestId).ToListAsync(token)).Single();
                return ApiResponse<AccreditationStandardDto>.SuccessResponse((await StandardAsync(row.Id, token))!);
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { logger.LogWarning(ex, "Standard conflict for tenant {TenantId}", access.TenantId); return Conflict<AccreditationStandardDto>(); }
    }

    private async Task<AccreditationStandardDto?> StandardAsync(long id, CancellationToken ct) =>
        (await db.Database.SqlQueryRaw<AccreditationStandardDto>("""
            SELECT Id, FrameworkId, Code, Title, Rubric, RequiredEvidence, ValidityDays, RequiredReviewerCount
            FROM dbo.AccreditationStandards WHERE TenantId = {0} AND Id = {1}
            """, access.TenantId, id).ToListAsync(ct)).SingleOrDefault();

    public async Task<ApiResponse<List<AccreditationStandardDto>>> StandardsAsync(long frameworkId, CancellationToken ct = default)
    {
        var roles = await access.RolesAsync(ct);
        if (roles.Count == 0 || frameworkId <= 0) return Denied<List<AccreditationStandardDto>>();
        var framework = await FrameworkAsync(frameworkId, ct);
        if (framework == null || (framework.StateCode != "Published" && !ResearchWorkflowAccess.AccreditationCoordinator(roles)))
            return Denied<List<AccreditationStandardDto>>();
        var rows = await db.Database.SqlQueryRaw<AccreditationStandardDto>("""
            SELECT TOP(200) Id, FrameworkId, Code, Title, Rubric, RequiredEvidence, ValidityDays, RequiredReviewerCount
            FROM dbo.AccreditationStandards WHERE TenantId = {0} AND FrameworkId = {1}
            ORDER BY Code, Id
            """, access.TenantId, frameworkId).ToListAsync(ct);
        return ApiResponse<List<AccreditationStandardDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<AccreditationFrameworkDto>> PublishFrameworkAsync(long frameworkId,
        AccreditationFrameworkPublishDto input, CancellationToken ct = default)
    {
        var roles = await access.RolesAsync(ct);
        if (!ResearchWorkflowAccess.AccreditationCoordinator(roles) || frameworkId <= 0 || input == null ||
            input.ClientRequestId == Guid.Empty || input.RowVersion?.Length != 12)
            return Denied<AccreditationFrameworkDto>();
        byte[] expected;
        try { expected = Convert.FromBase64String(input.RowVersion); }
        catch (FormatException) { return ApiResponse<AccreditationFrameworkDto>.ErrorResponse("Invalid version.", 400); }
        if (expected.Length != 8) return ApiResponse<AccreditationFrameworkDto>.ErrorResponse("Invalid version.", 400);
        var hash = Hash(JsonSerializer.Serialize(new object[] { frameworkId, input.RowVersion }));
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (!ResearchWorkflowAccess.AccreditationCoordinator(await access.RolesAsync(token)))
                    return Denied<AccreditationFrameworkDto>();
                var row = await FrameworkAsync(frameworkId, token);
                if (row == null || row.CreatedByUserId == access.UserId) return Denied<AccreditationFrameworkDto>();
                if (row.StateCode == "Published")
                    return row.PublishRequestId == input.ClientRequestId && row.PublishSha256 == hash
                        ? ApiResponse<AccreditationFrameworkDto>.SuccessResponse(Framework(row))
                        : Conflict<AccreditationFrameworkDto>();
                if (row.StateCode != "Draft" || !CryptographicOperations.FixedTimeEquals(expected, row.RowVersion))
                    return Conflict<AccreditationFrameworkDto>();
                var count = await db.Database.SqlQueryRaw<int>("""
                    SELECT COUNT(*) AS Value FROM dbo.AccreditationStandards
                    WHERE TenantId = {0} AND FrameworkId = {1}
                    """, access.TenantId, frameworkId).SingleAsync(token);
                if (count == 0) return ApiResponse<AccreditationFrameworkDto>.ErrorResponse("At least one standard required.", 400);
                var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE dbo.AccreditationFrameworks
                    SET StateCode = {"Published"}, PublishRequestId = {input.ClientRequestId},
                        PublishSha256 = {hash}, PublishedByUserId = {access.UserId}, PublishedAtUtc = {Now}
                    WHERE TenantId = {access.TenantId} AND Id = {frameworkId} AND StateCode = {"Draft"}
                      AND RowVersion = {expected}
                    """, token);
                if (changed != 1) return Conflict<AccreditationFrameworkDto>();
                return ApiResponse<AccreditationFrameworkDto>.SuccessResponse(Framework((await FrameworkAsync(frameworkId, token))!));
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { logger.LogWarning(ex, "Framework publish conflict for tenant {TenantId}", access.TenantId); return Conflict<AccreditationFrameworkDto>(); }
    }

    private sealed class FrameworkRow
    {
        public long Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public int VersionNo { get; set; }
        public string StateCode { get; set; } = "";
        public Guid CreateRequestId { get; set; }
        public string CreateSha256 { get; set; } = "";
        public Guid? PublishRequestId { get; set; }
        public string? PublishSha256 { get; set; }
        public long CreatedByUserId { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
    private sealed class StandardCreateRow
    {
        public long Id { get; set; }
        public string CreateSha256 { get; set; } = "";
    }

    private static string? Next(string state, string action) => (state, action) switch
    {
        ("Draft", "edit") => "Draft",
        ("Draft", "submit") => "Submitted",
        ("Submitted", "assign") => "Assigned",
        ("Assigned", "approve") => "Approved",
        ("Assigned", "reject") => "Rejected",
        ("Assigned", "request-revision") => "ChangesRequested",
        ("ChangesRequested", "revise") => "Submitted",
        ("Draft", "withdraw") or ("Submitted", "withdraw") or ("ChangesRequested", "withdraw") => "Withdrawn",
        _ => null
    };
    private static AccreditationAssessmentDto Map(AssessmentRow row, IReadOnlySet<string> roles, long actor) => new()
    {
        Reference = row.Reference, CampusId = row.CampusId, AcademicProgramId = row.AcademicProgramId,
        StandardId = row.StandardId, StandardCode = row.StandardCode,
        FrameworkCode = row.FrameworkCode, FrameworkVersion = row.FrameworkVersion,
        OwnerUserId = row.OwnerUserId, ReviewerUserId = row.ReviewerUserId,
        RequiredReviewerCount = row.RequiredReviewerCount, AssignedReviewerCount = row.AssignedReviewerCount,
        ApprovalsReceived = row.ApprovalsReceived,
        PeriodStart = row.PeriodStart, PeriodEnd = row.PeriodEnd,
        CurrentRevision = row.CurrentRevision, EvidenceSummary = Sensitive(row, roles, actor) ? row.EvidenceSummary : null,
        EvidenceSha256 = row.EvidenceSha256, StateCode = row.StateCode,
        ApprovedAtUtc = row.ApprovedAtUtc, ExpiresAtUtc = row.ExpiresAtUtc,
        RowVersion = Convert.ToBase64String(row.RowVersion), UpdatedAtUtc = row.UpdatedAtUtc,
        AllowedActions = new[] { "edit", "submit", "assign", "approve", "reject", "request-revision", "revise", "withdraw" }
            .Where(action => Next(row.StateCode, action) != null &&
                (action == "assign" ? Checker(row, roles, actor) :
                 action is "approve" or "reject" or "request-revision" ?
                     Reviewer(row, roles, actor) && row.ActorReviewPending : Author(row, actor)))
            .ToList()
    };
    private async Task<AssessmentEventRow?> PreviousAsync(Guid id, CancellationToken ct) =>
        (await db.Database.SqlQueryRaw<AssessmentEventRow>("""
            SELECT AssessmentId, ActorUserId, RequestSha256, ResultSnapshot
            FROM dbo.AccreditationEvents WHERE TenantId = {0} AND ClientRequestId = {1}
            """, access.TenantId, id).ToListAsync(ct)).SingleOrDefault();
    private async Task<AssessmentRow?> ByIdAsync(long id, CancellationToken ct) =>
        (await db.Database.SqlQueryRaw<AssessmentRow>(AssessmentColumns + " AND a.Id = {2} AND " + CampusGrant,
            access.TenantId, access.UserId, id).ToListAsync(ct)).SingleOrDefault();
    private ApiResponse<AccreditationAssessmentDto> Replay(AssessmentEventRow prior, AssessmentRow row, string hash)
    {
        if (prior.AssessmentId != row.Id || prior.ActorUserId != access.UserId || prior.RequestSha256 != hash)
            return Conflict<AccreditationAssessmentDto>();
        var dto = JsonSerializer.Deserialize<AccreditationAssessmentDto>(prior.ResultSnapshot);
        return dto == null ? Conflict<AccreditationAssessmentDto>() :
            ApiResponse<AccreditationAssessmentDto>.SuccessResponse(dto, "Previously completed command.");
    }
    private async Task EventAsync(AssessmentRow row, Guid id, string hash, string action,
        string from, string? reason, AccreditationAssessmentDto result, CancellationToken ct)
    {
        var snapshot = JsonSerializer.Serialize(result);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.AccreditationEvents
              (TenantId, AssessmentId, ClientRequestId, RequestSha256, ActorUserId,
               ActionCode, FromState, ToState, RevisionNo, Reason, ResultSnapshot, OccurredAtUtc)
            VALUES ({access.TenantId}, {row.Id}, {id}, {hash}, {access.UserId},
                    {action}, {from}, {result.StateCode}, {result.CurrentRevision}, {reason}, {snapshot}, {Now})
            """, ct);
    }

    public async Task<ApiResponse<AccreditationAssessmentDto>> CreateAsync(
        AccreditationAssessmentCreateDto input, CancellationToken ct = default)
    {
        if (input == null || input.ClientRequestId == Guid.Empty || input.CampusId <= 0 || input.StandardId <= 0 ||
            !Text(input.EvidenceSummary, 30, 3000) || input.PeriodStart.TimeOfDay != TimeSpan.Zero ||
            input.PeriodEnd.TimeOfDay != TimeSpan.Zero || input.PeriodStart.Year < 2000 ||
            input.PeriodEnd < input.PeriodStart || input.PeriodStart.Year > 9980 ||
            input.PeriodEnd > input.PeriodStart.AddYears(10))
            return ApiResponse<AccreditationAssessmentDto>.ErrorResponse("Invalid period or evidence summary.", 400);
        var evidence = input.EvidenceSummary.Trim();
        var hash = Hash(JsonSerializer.Serialize(new object?[] { "create", input.CampusId, input.AcademicProgramId,
            input.StandardId, input.PeriodStart.Date, input.PeriodEnd.Date, evidence }));
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var roles = await access.RolesAsync(token);
                if (!ResearchWorkflowAccess.Has(roles, "TEACHER", "ACCREDITATIONOFFICER", "ACCREDITATIONCOORDINATOR", "TENANTADMIN", "PRINCIPAL") ||
                    !await access.CampusAsync(input.CampusId, token)) return Denied<AccreditationAssessmentDto>();
                var prior = await PreviousAsync(input.ClientRequestId, token);
                if (prior != null)
                {
                    var old = await ByIdAsync(prior.AssessmentId, token);
                    return old == null || !Author(old, access.UserId) ? Denied<AccreditationAssessmentDto>() : Replay(prior, old, hash);
                }
                var standard = (await db.Database.SqlQueryRaw<StandardPublishedRow>("""
                    SELECT s.Id, s.ValidityDays FROM dbo.AccreditationStandards AS s
                    JOIN dbo.AccreditationFrameworks AS f ON f.Id = s.FrameworkId AND f.TenantId = s.TenantId
                    WHERE s.TenantId = {0} AND s.Id = {1} AND f.StateCode = N'Published'
                    """, access.TenantId, input.StandardId).ToListAsync(token)).SingleOrDefault();
                if (standard == null) return Denied<AccreditationAssessmentDto>();
                if (input.AcademicProgramId.HasValue && !await db.ProgramCampuses.AsNoTracking().AnyAsync(pc =>
                    pc.TenantId == access.TenantId && pc.CampusId == input.CampusId &&
                    pc.AcademicProgramId == input.AcademicProgramId && pc.IsActive && !pc.IsDeleted &&
                    db.AcademicPrograms.Any(p => p.Id == pc.AcademicProgramId && p.TenantId == access.TenantId &&
                        p.IsActive && !p.IsDeleted), token))
                    return ApiResponse<AccreditationAssessmentDto>.ErrorResponse("Program not active in this campus.", 400);
                var reference = Guid.NewGuid();
                var contentHash = Hash(evidence);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO dbo.AccreditationAssessments
                      (TenantId, PublicId, StandardId, CampusId, AcademicProgramId, OwnerUserId,
                       PeriodStart, PeriodEnd, CurrentRevision, EvidenceSummary, EvidenceSha256,
                       StateCode, CreatedAtUtc, UpdatedAtUtc)
                    VALUES ({access.TenantId}, {reference}, {input.StandardId}, {input.CampusId},
                            {input.AcademicProgramId}, {access.UserId}, {input.PeriodStart.Date},
                            {input.PeriodEnd.Date}, {0}, {evidence}, {contentHash}, {"Draft"}, {Now}, {Now})
                    """, token);
                var row = await GetAsync(reference, token) ?? throw new InvalidOperationException("Accreditation insert missing row.");
                var result = Map(row, roles, access.UserId);
                await EventAsync(row, input.ClientRequestId, hash, "create", "None", null, result, token);
                return ApiResponse<AccreditationAssessmentDto>.SuccessResponse(result);
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { logger.LogWarning(ex, "Accreditation create conflict for tenant {TenantId}", access.TenantId); return Conflict<AccreditationAssessmentDto>(); }
        catch (Exception ex)
        { logger.LogError(ex, "Accreditation create failed for tenant {TenantId}", access.TenantId); return ApiResponse<AccreditationAssessmentDto>.ErrorResponse("Evidence draft could not be saved.", 500); }
    }

    public async Task<ApiResponse<AccreditationAssessmentDto>> CommandAsync(Guid reference, string action,
        AccreditationAssessmentCommandDto input, CancellationToken ct = default)
    {
        action = action?.Trim().ToLowerInvariant() ?? "";
        if (reference == Guid.Empty || input == null || input.ClientRequestId == Guid.Empty ||
            action is not ("edit" or "submit" or "assign" or "approve" or "reject" or "request-revision" or "revise" or "withdraw") ||
            input.RowVersion?.Length != 12 || input.Reason?.Length > 500)
            return ApiResponse<AccreditationAssessmentDto>.ErrorResponse("Invalid accreditation command.", 400);
        byte[] expected;
        try { expected = Convert.FromBase64String(input.RowVersion); }
        catch (FormatException) { return ApiResponse<AccreditationAssessmentDto>.ErrorResponse("Invalid version.", 400); }
        if (expected.Length != 8) return ApiResponse<AccreditationAssessmentDto>.ErrorResponse("Invalid version.", 400);
        var evidence = input.EvidenceSummary?.Trim() ?? "";
        var reason = input.Reason?.Trim();
        var content = action is "edit" or "revise";
        if (content && !Text(evidence, 30, 3000))
            return ApiResponse<AccreditationAssessmentDto>.ErrorResponse("A valid evidence summary is required.", 400);
        if ((action is "approve" or "reject" or "request-revision" or "revise" or "withdraw") &&
            (reason?.Length ?? 0) < 10)
            return ApiResponse<AccreditationAssessmentDto>.ErrorResponse("Reason of at least 10 characters required.", 400);
        if ((action is "approve" or "reject" or "request-revision") && input.DeclaresNoConflict != true)
            return ApiResponse<AccreditationAssessmentDto>.ErrorResponse("Conflict-of-interest declaration required.", 400);
        var reviewerIds = input.ReviewerUserIds?.OrderBy(id => id).ToArray() ??
            (input.ReviewerUserId is > 0 ? new[] { input.ReviewerUserId.Value } : Array.Empty<long>());
        // Preserve hashes of pre-panel commands so retries remain valid after rolling deployment.
        var reviewerPayload = input.ReviewerUserIds == null ? (object?)input.ReviewerUserId : reviewerIds;
        var hash = Hash(JsonSerializer.Serialize(new object?[] { reference, action, input.RowVersion,
            reviewerPayload, evidence, reason, input.DeclaresNoConflict }));
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var roles = await access.RolesAsync(token);
                var row = await GetAsync(reference, token);
                if (row == null || roles.Count == 0 || !Visible(row, roles, access.UserId) ||
                    !await access.CampusAsync(row.CampusId, token)) return Denied<AccreditationAssessmentDto>();
                var reviewerAction = action is "approve" or "reject" or "request-revision";
                if (action == "assign" ? !Checker(row, roles, access.UserId) :
                    reviewerAction ? !Reviewer(row, roles, access.UserId) : !Author(row, access.UserId))
                    return Denied<AccreditationAssessmentDto>();
                var prior = await PreviousAsync(input.ClientRequestId, token);
                if (prior != null) return Replay(prior, row, hash);
                if (!CryptographicOperations.FixedTimeEquals(expected, row.RowVersion)) return Conflict<AccreditationAssessmentDto>();
                var next = Next(row.StateCode, action);
                if (next == null) return Conflict<AccreditationAssessmentDto>();
                if (reviewerAction && !row.ActorReviewPending) return Conflict<AccreditationAssessmentDto>();
                if (action == "assign")
                {
                    if (reviewerIds.Length != row.RequiredReviewerCount || reviewerIds.Distinct().Count() != reviewerIds.Length ||
                        reviewerIds.Any(id => id <= 0 || id == row.OwnerUserId || id == access.UserId))
                        return ApiResponse<AccreditationAssessmentDto>.ErrorResponse("Select the required number of distinct independent reviewers.", 400);
                    foreach (var id in reviewerIds)
                        if (!await access.EligibleAsync(id, row.CampusId,
                            new[] { "ACCREDITATIONREVIEWER", "TEACHER", "PRINCIPAL" }, token))
                            return ApiResponse<AccreditationAssessmentDto>.ErrorResponse("Reviewer not eligible for this campus.", 400);
                }
                // All assigned reviewers must approve; a single reject/revision request vetoes the panel.
                if (action == "approve" && row.ApprovalsReceived + 1 < row.RequiredReviewerCount)
                    next = "Assigned";
                var reviewer = action == "assign" ? (long?)null :
                    action == "revise" ? null : row.ReviewerUserId;
                var revision = row.CurrentRevision + (action is "submit" or "revise" ? 1 : 0);
                var summary = content ? evidence : row.EvidenceSummary;
                var contentHash = content ? Hash(evidence) : row.EvidenceSha256;
                var approval = action == "approve" && next == "Approved" ? Now : row.ApprovedAtUtc;
                var expiry = action == "approve" && next == "Approved" ? Now.AddDays(row.ValidityDays) : row.ExpiresAtUtc;
                var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE dbo.AccreditationAssessments
                    SET CurrentRevision = {revision}, EvidenceSummary = {summary}, EvidenceSha256 = {contentHash},
                        ReviewerUserId = {reviewer}, StateCode = {next},
                        ApprovedAtUtc = {approval}, ExpiresAtUtc = {expiry}, UpdatedAtUtc = {Now}
                    WHERE TenantId = {access.TenantId} AND Id = {row.Id} AND RowVersion = {expected}
                    """, token);
                if (changed != 1) return Conflict<AccreditationAssessmentDto>();
                if (action == "assign")
                    foreach (var id in reviewerIds)
                        await db.Database.ExecuteSqlInterpolatedAsync($"""
                            INSERT INTO dbo.AccreditationReviewAssignments
                              (TenantId, AssessmentId, RevisionNo, ReviewerUserId, AssignedByUserId, AssignedAtUtc, DecisionCode)
                            VALUES ({access.TenantId}, {row.Id}, {row.CurrentRevision}, {id}, {access.UserId}, {Now}, {"Pending"})
                            """, token);
                if (reviewerAction)
                {
                    var decision = action switch
                    {
                        "approve" => "Approved", "reject" => "Rejected", _ => "ChangesRequested"
                    };
                    var recorded = await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE dbo.AccreditationReviewAssignments SET DecisionCode = {decision},
                            DecisionReason = {reason}, DecidedAtUtc = {Now}
                        WHERE TenantId = {access.TenantId} AND AssessmentId = {row.Id}
                          AND RevisionNo = {row.CurrentRevision} AND ReviewerUserId = {access.UserId}
                          AND DecisionCode = {"Pending"}
                        """, token);
                    if (recorded != 1) return Conflict<AccreditationAssessmentDto>();
                }
                if (action is "submit" or "revise")
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO dbo.AccreditationEvidenceVersions
                          (TenantId, AssessmentId, RevisionNo, EvidenceSummary, EvidenceSha256,
                           SubmittedByUserId, SubmittedAtUtc)
                        VALUES ({access.TenantId}, {row.Id}, {revision}, {summary}, {contentHash}, {access.UserId}, {Now})
                        """, token);
                var updated = await GetAsync(reference, token) ?? throw new InvalidOperationException("Accreditation update missing row.");
                var result = Map(updated, roles, access.UserId);
                await EventAsync(updated, input.ClientRequestId, hash, action, row.StateCode, reason, result, token);
                return ApiResponse<AccreditationAssessmentDto>.SuccessResponse(result);
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { logger.LogWarning(ex, "Accreditation command conflict for tenant {TenantId}", access.TenantId); return Conflict<AccreditationAssessmentDto>(); }
        catch (Exception ex)
        { logger.LogError(ex, "Accreditation command failed for tenant {TenantId}", access.TenantId); return ApiResponse<AccreditationAssessmentDto>.ErrorResponse("Command failed without saving.", 500); }
    }

    public async Task<ApiResponse<AccreditationAssessmentDto>> DetailAsync(Guid reference, CancellationToken ct = default)
    {
        var roles = await access.RolesAsync(ct);
        if (reference == Guid.Empty || roles.Count == 0) return Denied<AccreditationAssessmentDto>();
        var row = await GetAsync(reference, ct);
        return row == null || !Visible(row, roles, access.UserId)
            ? Denied<AccreditationAssessmentDto>() : ApiResponse<AccreditationAssessmentDto>.SuccessResponse(Map(row, roles, access.UserId));
    }

    public async Task<ApiResponse<PagedResult<AccreditationAssessmentDto>>> ListAsync(long? campusId,
        int page, int pageSize, CancellationToken ct = default)
    {
        var roles = await access.RolesAsync(ct);
        if (roles.Count == 0) return Denied<PagedResult<AccreditationAssessmentDto>>();
        if (campusId is <= 0 || page is < 1 or > 100000 || pageSize is < 1 or > 50 || page > int.MaxValue / pageSize)
            return ApiResponse<PagedResult<AccreditationAssessmentDto>>.ErrorResponse("Invalid pagination.", 400);
        if (campusId.HasValue && !await access.CampusAsync(campusId.Value, ct))
            return Denied<PagedResult<AccreditationAssessmentDto>>();
        var filter = " AND " + CampusGrant + " AND ({2} = 0 OR a.CampusId = {2})" +
            (ResearchWorkflowAccess.AccreditationCoordinator(roles) ? "" :
                " AND (a.OwnerUserId = {1} OR EXISTS (SELECT 1 FROM dbo.AccreditationReviewAssignments AS ra WHERE ra.TenantId = a.TenantId AND ra.AssessmentId = a.Id AND ra.RevisionNo = a.CurrentRevision AND ra.ReviewerUserId = {1}))");
        var count = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM dbo.AccreditationAssessments AS a WHERE a.TenantId = {0}" + filter,
            access.TenantId, access.UserId, campusId ?? 0).SingleAsync(ct);
        var rows = await db.Database.SqlQueryRaw<AssessmentRow>(AssessmentColumns + filter +
            " ORDER BY a.UpdatedAtUtc DESC, a.Id DESC OFFSET {3} ROWS FETCH NEXT {4} ROWS ONLY",
            access.TenantId, access.UserId, campusId ?? 0, (page - 1) * pageSize, pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<AccreditationAssessmentDto>>.SuccessResponse(new()
        { Page = page, PageSize = pageSize, TotalCount = count, Items = rows.Select(x => Map(x, roles, access.UserId)).ToList() });
    }

    public async Task<ApiResponse<PagedResult<AccreditationAssessmentHistoryDto>>> HistoryAsync(Guid reference,
        int page, int pageSize, CancellationToken ct = default)
    {
        var detail = await DetailAsync(reference, ct);
        if (!detail.Success || page is < 1 or > 100000 || pageSize is < 1 or > 50 || page > int.MaxValue / pageSize)
            return Denied<PagedResult<AccreditationAssessmentHistoryDto>>();
        var query = " FROM dbo.AccreditationEvents AS e JOIN dbo.AccreditationAssessments AS a ON a.Id = e.AssessmentId AND a.TenantId = e.TenantId WHERE e.TenantId = {0} AND a.PublicId = {1}";
        var count = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value" + query,
            access.TenantId, reference).SingleAsync(ct);
        var rows = await db.Database.SqlQueryRaw<AccreditationAssessmentHistoryDto>(
            "SELECT e.ActionCode, e.FromState, e.ToState, e.RevisionNo, e.Reason, e.OccurredAtUtc" + query +
            " ORDER BY e.OccurredAtUtc DESC, e.Id DESC OFFSET {2} ROWS FETCH NEXT {3} ROWS ONLY",
            access.TenantId, reference, (page - 1) * pageSize, pageSize).ToListAsync(ct);
        // Coordinators can inspect status events without reading a confidential review reason.
        if (detail.Data?.EvidenceSummary == null)
            foreach (var item in rows) item.Reason = null;
        return ApiResponse<PagedResult<AccreditationAssessmentHistoryDto>>.SuccessResponse(new()
        { Page = page, PageSize = pageSize, TotalCount = count, Items = rows });
    }

    public async Task<ApiResponse<List<AccreditationReportRowDto>>> ReportAsync(long campusId,
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        var roles = await access.RolesAsync(ct);
        if (!ResearchWorkflowAccess.AccreditationCoordinator(roles) || !await access.CampusAsync(campusId, ct))
            return Denied<List<AccreditationReportRowDto>>();
        if (from.TimeOfDay != TimeSpan.Zero || to.TimeOfDay != TimeSpan.Zero || from.Year is < 2000 or > 9980 ||
            to < from || to > from.AddYears(10))
            return ApiResponse<List<AccreditationReportRowDto>>.ErrorResponse("Invalid report period.", 400);
        var rows = await db.Database.SqlQueryRaw<AccreditationReportRowDto>("""
            SELECT TOP(501) a.CampusId, a.AcademicProgramId, f.Code AS FrameworkCode,
              f.VersionNo AS FrameworkVersion, s.Code AS StandardCode,
              COUNT(*) AS Total,
              SUM(CASE WHEN a.StateCode = N'Approved' AND a.ExpiresAtUtc > {3} THEN 1 ELSE 0 END) AS Approved,
              SUM(CASE WHEN a.StateCode IN (N'Draft',N'Submitted',N'Assigned',N'ChangesRequested') THEN 1 ELSE 0 END) AS Pending,
              SUM(CASE WHEN a.StateCode = N'Approved' AND a.ExpiresAtUtc <= {3} THEN 1 ELSE 0 END) AS Expired
            FROM dbo.AccreditationAssessments AS a
            JOIN dbo.AccreditationStandards AS s ON s.Id = a.StandardId AND s.TenantId = a.TenantId
            JOIN dbo.AccreditationFrameworks AS f ON f.Id = s.FrameworkId AND f.TenantId = s.TenantId
            WHERE a.TenantId = {0} AND a.CampusId = {1} AND a.PeriodEnd >= {2} AND a.PeriodStart <= {4}
            GROUP BY a.CampusId, a.AcademicProgramId, f.Code, f.VersionNo, s.Code
            ORDER BY f.Code, f.VersionNo, s.Code, a.AcademicProgramId
            """, access.TenantId, campusId, from.Date, Now, to.Date).ToListAsync(ct);
        if (rows.Count > 500)
            return ApiResponse<List<AccreditationReportRowDto>>.ErrorResponse("Report has over 500 groups; narrow the period or scope.", 409);
        return ApiResponse<List<AccreditationReportRowDto>>.SuccessResponse(rows);
    }

    private sealed class StandardPublishedRow
    {
        public long Id { get; set; }
        public int ValidityDays { get; set; }
    }
    private sealed class AssessmentEventRow
    {
        public long AssessmentId { get; set; }
        public long ActorUserId { get; set; }
        public string RequestSha256 { get; set; } = "";
        public string ResultSnapshot { get; set; } = "";
    }
    private sealed class AssessmentRow
    {
        public long Id { get; set; }
        public Guid Reference { get; set; }
        public long CampusId { get; set; }
        public long? AcademicProgramId { get; set; }
        public long StandardId { get; set; }
        public string StandardCode { get; set; } = "";
        public string FrameworkCode { get; set; } = "";
        public int FrameworkVersion { get; set; }
        public int ValidityDays { get; set; }
        public int RequiredReviewerCount { get; set; }
        public int AssignedReviewerCount { get; set; }
        public int ApprovalsReceived { get; set; }
        public bool ActorIsReviewer { get; set; }
        public bool ActorReviewPending { get; set; }
        public long OwnerUserId { get; set; }
        public long? ReviewerUserId { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public int CurrentRevision { get; set; }
        public string EvidenceSummary { get; set; } = "";
        public string EvidenceSha256 { get; set; } = "";
        public string StateCode { get; set; } = "";
        public DateTime? ApprovedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public DateTime UpdatedAtUtc { get; set; }
    }
}
