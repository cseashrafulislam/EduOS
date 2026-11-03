using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Research;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Research;

/// <summary>Outcome versions and audit links; accounting remains authoritative in Finance GL.</summary>
public sealed class ResearchQualityExtensionService(EduOSDbContext db, ResearchWorkflowAccess access,
    TimeProvider clock, ILogger<ResearchQualityExtensionService> logger) : IResearchQualityExtensionService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Record unavailable.", 404);
    private static ApiResponse<T> Conflict<T>() => ApiResponse<T>.ErrorResponse("A conflicting request or record already exists.", 409);
    private static bool Code(string value) => value.Length is >= 2 and <= 40 &&
        value.All(x => char.IsAsciiLetterUpper(x) || char.IsAsciiDigit(x) || x == '_');
    private static bool Coordinator(IReadOnlySet<string> roles) => ResearchWorkflowAccess.AccreditationCoordinator(roles);
    private static bool GrantCoordinator(IReadOnlySet<string> roles) => ResearchWorkflowAccess.ResearchCoordinator(roles);

    private async Task<bool> ProgramInCampusAsync(long programId, long campusId, CancellationToken ct) =>
        await access.CampusAsync(campusId, ct) && await db.ProgramCampuses.AsNoTracking().AnyAsync(pc =>
            pc.TenantId == access.TenantId && pc.CampusId == campusId && pc.AcademicProgramId == programId &&
            pc.IsActive && !pc.IsDeleted && db.AcademicPrograms.Any(p => p.TenantId == access.TenantId &&
                p.Id == programId && p.IsActive && !p.IsDeleted), ct);

    private Task<bool> ActiveProgramAsync(long programId, CancellationToken ct) =>
        db.AcademicPrograms.AsNoTracking().AnyAsync(p => p.TenantId == access.TenantId &&
            p.Id == programId && p.IsActive && !p.IsDeleted &&
            db.ProgramCampuses.Any(pc => pc.TenantId == access.TenantId &&
                pc.AcademicProgramId == p.Id && pc.IsActive && !pc.IsDeleted &&
                db.Campuses.Any(c => c.TenantId == access.TenantId && c.Id == pc.CampusId &&
                    c.IsActive && !c.IsDeleted) &&
                db.UserCampusAccesses.Any(c => c.TenantId == access.TenantId && c.UserId == access.UserId &&
                    c.CampusId == pc.CampusId && c.IsActive && !c.IsDeleted)), ct);

    public async Task<ApiResponse<ProgramOutcomeDto>> CreateOutcomeAsync(ProgramOutcomeCreateDto input,
        CancellationToken ct = default)
    {
        if (!Coordinator(await access.RolesAsync(ct))) return Denied<ProgramOutcomeDto>();
        var code = input?.Code?.Trim().ToUpperInvariant() ?? "";
        var statement = input?.Statement?.Trim() ?? "";
        if (input == null || input.ClientRequestId == Guid.Empty || input.AcademicProgramId <= 0 ||
            !Code(code) || statement.Length is < 10 or > 1000 || input.VersionNo is < 1 or > 1000)
            return ApiResponse<ProgramOutcomeDto>.ErrorResponse("Invalid program outcome version.", 400);
        if (!await ActiveProgramAsync(input.AcademicProgramId, ct)) return Denied<ProgramOutcomeDto>();
        var hash = Hash(new object[] { input.AcademicProgramId, code, input.VersionNo, statement });
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (!Coordinator(await access.RolesAsync(token)) || !await ActiveProgramAsync(input.AcademicProgramId, token))
                    return Denied<ProgramOutcomeDto>();
                var previous = (await db.Database.SqlQueryRaw<OutcomeCreatedRow>("""
                    SELECT Id, CreateSha256, CreatedByUserId FROM dbo.AccreditationProgramOutcomes
                    WHERE TenantId = {0} AND CreateRequestId = {1}
                    """, access.TenantId, input.ClientRequestId).ToListAsync(token)).SingleOrDefault();
                if (previous != null)
                {
                    if (previous.CreateSha256 != hash || previous.CreatedByUserId != access.UserId)
                        return Conflict<ProgramOutcomeDto>();
                    var replay = await OutcomeAsync(previous.Id, token);
                    return replay == null ? Conflict<ProgramOutcomeDto>() : ApiResponse<ProgramOutcomeDto>.SuccessResponse(replay);
                }
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO dbo.AccreditationProgramOutcomes
                      (TenantId, AcademicProgramId, Code, VersionNo, Statement, CreateRequestId,
                       CreateSha256, CreatedByUserId, CreatedAtUtc)
                    VALUES ({access.TenantId}, {input.AcademicProgramId}, {code}, {input.VersionNo},
                            {statement}, {input.ClientRequestId}, {hash}, {access.UserId}, {Now})
                    """, token);
                var result = (await db.Database.SqlQueryRaw<ProgramOutcomeDto>("""
                    SELECT Id, AcademicProgramId, Code, VersionNo, Statement
                    FROM dbo.AccreditationProgramOutcomes
                    WHERE TenantId = {0} AND CreateRequestId = {1}
                    """, access.TenantId, input.ClientRequestId).ToListAsync(token)).Single();
                return ApiResponse<ProgramOutcomeDto>.SuccessResponse(result);
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { logger.LogWarning(ex, "Outcome version conflict tenant {Tenant}", access.TenantId); return Conflict<ProgramOutcomeDto>(); }
    }

    private async Task<ProgramOutcomeDto?> OutcomeAsync(long id, CancellationToken ct) =>
        (await db.Database.SqlQueryRaw<ProgramOutcomeDto>("""
            SELECT Id, AcademicProgramId, Code, VersionNo, Statement
            FROM dbo.AccreditationProgramOutcomes WHERE TenantId = {0} AND Id = {1}
            """, access.TenantId, id).ToListAsync(ct)).SingleOrDefault();

    public async Task<ApiResponse<List<ProgramOutcomeDto>>> OutcomesAsync(long campusId, long programId,
        CancellationToken ct = default)
    {
        if ((await access.RolesAsync(ct)).Count == 0 || programId <= 0 ||
            !await ProgramInCampusAsync(programId, campusId, ct)) return Denied<List<ProgramOutcomeDto>>();
        var rows = await db.Database.SqlQueryRaw<ProgramOutcomeDto>("""
            SELECT TOP(250) Id, AcademicProgramId, Code, VersionNo, Statement
            FROM dbo.AccreditationProgramOutcomes WHERE TenantId = {0} AND AcademicProgramId = {1}
            ORDER BY Code, VersionNo DESC
            """, access.TenantId, programId).ToListAsync(ct);
        return ApiResponse<List<ProgramOutcomeDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<StandardOutcomeMapDto>> MapOutcomeAsync(StandardOutcomeMapCreateDto input,
        CancellationToken ct = default)
    {
        if (!Coordinator(await access.RolesAsync(ct))) return Denied<StandardOutcomeMapDto>();
        if (input == null || input.ClientRequestId == Guid.Empty || input.StandardId <= 0 || input.OutcomeId <= 0)
            return ApiResponse<StandardOutcomeMapDto>.ErrorResponse("Standard and outcome are required.", 400);
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (!Coordinator(await access.RolesAsync(token))) return Denied<StandardOutcomeMapDto>();
                var existing = (await db.Database.SqlQueryRaw<OutcomeMapReplayRow>("""
                    SELECT StandardId, OutcomeId FROM dbo.AccreditationStandardOutcomeMaps
                    WHERE TenantId = {0} AND CreateRequestId = {1}
                    """, access.TenantId, input.ClientRequestId).ToListAsync(token)).SingleOrDefault();
                if (existing != null && (existing.StandardId != input.StandardId || existing.OutcomeId != input.OutcomeId))
                    return Conflict<StandardOutcomeMapDto>();
                if (existing == null)
                {
                    var draft = await db.Database.SqlQueryRaw<int>("""
                        SELECT COUNT(*) AS Value FROM dbo.AccreditationStandards AS s
                        JOIN dbo.AccreditationFrameworks AS f ON f.TenantId = s.TenantId AND f.Id = s.FrameworkId
                        WHERE s.TenantId = {0} AND s.Id = {1} AND f.StateCode = N'Draft'
                        """, access.TenantId, input.StandardId).SingleAsync(token);
                    var outcome = await OutcomeAsync(input.OutcomeId, token);
                    if (draft != 1 || outcome == null || !await ActiveProgramAsync(outcome.AcademicProgramId, token))
                        return Denied<StandardOutcomeMapDto>();
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO dbo.AccreditationStandardOutcomeMaps
                          (TenantId, StandardId, OutcomeId, CreateRequestId, CreatedByUserId, CreatedAtUtc)
                        VALUES ({access.TenantId}, {input.StandardId}, {input.OutcomeId},
                                {input.ClientRequestId}, {access.UserId}, {Now})
                        """, token);
                }
                var row = (await db.Database.SqlQueryRaw<StandardOutcomeMapDto>("""
                    SELECT m.StandardId, m.OutcomeId, o.AcademicProgramId, o.Code AS OutcomeCode,
                      o.VersionNo, o.Statement FROM dbo.AccreditationStandardOutcomeMaps AS m
                    JOIN dbo.AccreditationProgramOutcomes AS o ON o.TenantId = m.TenantId AND o.Id = m.OutcomeId
                    WHERE m.TenantId = {0} AND m.StandardId = {1} AND m.OutcomeId = {2}
                    """, access.TenantId, input.StandardId, input.OutcomeId).ToListAsync(token)).Single();
                return ApiResponse<StandardOutcomeMapDto>.SuccessResponse(row);
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { logger.LogWarning(ex, "Standard outcome mapping conflict tenant {Tenant}", access.TenantId); return Conflict<StandardOutcomeMapDto>(); }
    }

    public async Task<ApiResponse<List<StandardOutcomeMapDto>>> StandardOutcomesAsync(long standardId,
        CancellationToken ct = default)
    {
        if ((await access.RolesAsync(ct)).Count == 0 || standardId <= 0) return Denied<List<StandardOutcomeMapDto>>();
        var rows = await db.Database.SqlQueryRaw<StandardOutcomeMapDto>("""
            SELECT TOP(250) m.StandardId, m.OutcomeId, o.AcademicProgramId,
              o.Code AS OutcomeCode, o.VersionNo, o.Statement
            FROM dbo.AccreditationStandardOutcomeMaps AS m
            JOIN dbo.AccreditationProgramOutcomes AS o ON o.TenantId = m.TenantId AND o.Id = m.OutcomeId
            JOIN dbo.AccreditationStandards AS s ON s.TenantId = m.TenantId AND s.Id = m.StandardId
            JOIN dbo.AccreditationFrameworks AS f ON f.TenantId = s.TenantId AND f.Id = s.FrameworkId
            WHERE m.TenantId = {0} AND m.StandardId = {1}
              AND (f.StateCode = N'Published' OR {2} = 1)
              AND EXISTS (SELECT 1 FROM dbo.ProgramCampuses AS pc
                  JOIN dbo.UserCampusAccesses AS grantAccess ON grantAccess.TenantId = pc.TenantId
                    AND grantAccess.CampusId = pc.CampusId
                  JOIN dbo.Campuses AS campus ON campus.TenantId = pc.TenantId AND campus.Id = pc.CampusId
                  WHERE pc.TenantId = o.TenantId AND pc.AcademicProgramId = o.AcademicProgramId
                    AND pc.IsActive = 1 AND pc.IsDeleted = 0 AND campus.IsActive = 1 AND campus.IsDeleted = 0
                    AND grantAccess.UserId = {3} AND grantAccess.IsActive = 1 AND grantAccess.IsDeleted = 0)
            ORDER BY o.AcademicProgramId, o.Code, o.VersionNo DESC
            """, access.TenantId, standardId,
            Coordinator(await access.RolesAsync(ct)) ? 1 : 0, access.UserId).ToListAsync(ct);
        return ApiResponse<List<StandardOutcomeMapDto>>.SuccessResponse(rows);
    }

    private async Task<AssessmentEvidenceScopeRow?> AssessmentScopeAsync(Guid reference, CancellationToken ct) =>
        (await db.Database.SqlQueryRaw<AssessmentEvidenceScopeRow>("""
            SELECT a.Id, a.CampusId, a.AcademicProgramId, a.OwnerUserId, a.StateCode
            FROM dbo.AccreditationAssessments AS a
            WHERE a.TenantId = {0} AND a.PublicId = {1}
            """, access.TenantId, reference).ToListAsync(ct)).SingleOrDefault();

    public async Task<ApiResponse<AcademicEvidenceDto>> LinkAcademicEvidenceAsync(Guid assessment,
        AcademicPublicationEvidenceLinkDto input, CancellationToken ct = default)
    {
        if (assessment == Guid.Empty || input == null || input.ClientRequestId == Guid.Empty ||
            input.ResultPublicationId <= 0) return ApiResponse<AcademicEvidenceDto>.ErrorResponse("Invalid published result reference.", 400);
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var row = await AssessmentScopeAsync(assessment, token);
                if (row == null || row.OwnerUserId != access.UserId ||
                    row.StateCode is not ("Draft" or "ChangesRequested") ||
                    (await access.RolesAsync(token)).Count == 0 || !await access.CampusAsync(row.CampusId, token))
                    return Denied<AcademicEvidenceDto>();
                var existing = (await db.Database.SqlQueryRaw<AcademicLinkReplayRow>("""
                    SELECT ResultPublicationId FROM dbo.AccreditationAcademicEvidenceLinks
                    WHERE TenantId = {0} AND CreateRequestId = {1} AND AssessmentId = {2}
                    """, access.TenantId, input.ClientRequestId, row.Id).ToListAsync(token)).SingleOrDefault();
                if (existing != null && existing.ResultPublicationId != input.ResultPublicationId)
                    return Conflict<AcademicEvidenceDto>();
                if (existing == null)
                {
                    var publication = await (from p in db.ResultPublications.AsNoTracking()
                        join batch in db.AcademicBatches.AsNoTracking() on p.AcademicBatchId equals batch.Id
                        join exam in db.Assessments.AsNoTracking() on p.AssessmentId equals exam.Id
                        where p.TenantId == access.TenantId && batch.TenantId == access.TenantId &&
                            exam.TenantId == access.TenantId && !p.IsDeleted && !batch.IsDeleted && !exam.IsDeleted &&
                            p.Id == input.ResultPublicationId && p.State == ResultPublicationState.Published &&
                            p.PublishedAt != null && p.PublishedAt <= Now &&
                            batch.CampusId == row.CampusId && exam.CampusId == row.CampusId &&
                            batch.AcademicProgramId == row.AcademicProgramId && p.AssessmentId == exam.Id
                        select new { p.VersionNo, p.PublishedAt }).SingleOrDefaultAsync(token);
                    if (row.AcademicProgramId == null || publication == null || publication.PublishedAt == null)
                        return Denied<AcademicEvidenceDto>();
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO dbo.AccreditationAcademicEvidenceLinks
                          (TenantId, AssessmentId, ResultPublicationId, PublicationVersionNo, PublishedAtUtc,
                           CreateRequestId, CreatedByUserId, CreatedAtUtc)
                        VALUES ({access.TenantId}, {row.Id}, {input.ResultPublicationId}, {publication.VersionNo},
                                {publication.PublishedAt.Value}, {input.ClientRequestId}, {access.UserId}, {Now})
                        """, token);
                }
                var result = (await db.Database.SqlQueryRaw<AcademicEvidenceDto>("""
                    SELECT ResultPublicationId, PublicationVersionNo, PublishedAtUtc,
                      CreatedAtUtc AS LinkedAtUtc FROM dbo.AccreditationAcademicEvidenceLinks
                    WHERE TenantId = {0} AND AssessmentId = {1} AND ResultPublicationId = {2}
                    """, access.TenantId, row.Id, input.ResultPublicationId).ToListAsync(token)).Single();
                return ApiResponse<AcademicEvidenceDto>.SuccessResponse(result);
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { logger.LogWarning(ex, "Academic result link conflict tenant {Tenant}", access.TenantId); return Conflict<AcademicEvidenceDto>(); }
    }

    public async Task<ApiResponse<List<AcademicEvidenceDto>>> AcademicEvidenceAsync(Guid assessment,
        CancellationToken ct = default)
    {
        var row = await AssessmentScopeAsync(assessment, ct);
        var roles = await access.RolesAsync(ct);
        var assignedReviewer = row != null && ResearchWorkflowAccess.Has(roles,
            "ACCREDITATIONREVIEWER", "TEACHER", "PRINCIPAL") &&
            await db.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS Value FROM dbo.AccreditationReviewAssignments AS ra
                JOIN dbo.AccreditationAssessments AS a ON a.TenantId = ra.TenantId AND a.Id = ra.AssessmentId
                WHERE ra.TenantId = {0} AND ra.AssessmentId = {1} AND ra.ReviewerUserId = {2}
                  AND ra.RevisionNo = a.CurrentRevision
                """, access.TenantId, row.Id, access.UserId).SingleAsync(ct) > 0;
        if (row == null || roles.Count == 0 || !await access.CampusAsync(row.CampusId, ct) ||
            (row.OwnerUserId != access.UserId && !Coordinator(roles) && !assignedReviewer))
            return Denied<List<AcademicEvidenceDto>>();
        var items = await db.Database.SqlQueryRaw<AcademicEvidenceDto>("""
            SELECT TOP(100) ResultPublicationId, PublicationVersionNo, PublishedAtUtc,
              CreatedAtUtc AS LinkedAtUtc FROM dbo.AccreditationAcademicEvidenceLinks
            WHERE TenantId = {0} AND AssessmentId = {1} ORDER BY CreatedAtUtc DESC
            """, access.TenantId, row.Id).ToListAsync(ct);
        return ApiResponse<List<AcademicEvidenceDto>>.SuccessResponse(items);
    }

    public async Task<ApiResponse<GrantFinanceAccountDto>> ConfigureFinanceAccountAsync(
        GrantFinanceAccountCreateDto input, CancellationToken ct = default)
    {
        if (!GrantCoordinator(await access.RolesAsync(ct))) return Denied<GrantFinanceAccountDto>();
        if (input == null || input.ClientRequestId == Guid.Empty || input.AccountId <= 0 || input.SchemeId <= 0 ||
            input.FlowCode is not ("Disbursement" or "Expense" or "Refund") ||
            input.NormalSide is not ("Debit" or "Credit"))
            return ApiResponse<GrantFinanceAccountDto>.ErrorResponse("Invalid Finance account classification.", 400);
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (!GrantCoordinator(await access.RolesAsync(token))) return Denied<GrantFinanceAccountDto>();
                var exists = (await db.Database.SqlQueryRaw<FinanceMappingReplayRow>("""
                    SELECT SchemeId, AccountId, FlowCode, NormalSide
                    FROM dbo.ResearchGrantFinanceAccounts WHERE TenantId = {0} AND CreateRequestId = {1}
                    """, access.TenantId, input.ClientRequestId).ToListAsync(token)).SingleOrDefault();
                if (exists != null && (exists.SchemeId != input.SchemeId || exists.AccountId != input.AccountId ||
                    exists.FlowCode != input.FlowCode || exists.NormalSide != input.NormalSide)) return Conflict<GrantFinanceAccountDto>();
                if (exists == null)
                {
                    var scheme = (await db.Database.SqlQueryRaw<FinanceSchemeRow>("""
                        SELECT Id, CurrencyCode FROM dbo.ResearchGrantSchemes WHERE TenantId = {0} AND Id = {1}
                        """, access.TenantId, input.SchemeId).ToListAsync(token)).SingleOrDefault();
                    var valid = scheme != null && await db.Accounts.AsNoTracking().AnyAsync(a =>
                        a.TenantId == access.TenantId && a.Id == input.AccountId &&
                        a.CurrencyCode == scheme.CurrencyCode && a.IsActive && !a.IsDeleted &&
                        !a.IsGroup && a.AllowsPosting, token);
                    if (!valid) return Denied<GrantFinanceAccountDto>();
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO dbo.ResearchGrantFinanceAccounts
                          (TenantId, SchemeId, AccountId, FlowCode, NormalSide,
                           CreateRequestId, CreatedByUserId, CreatedAtUtc)
                        VALUES ({access.TenantId}, {input.SchemeId}, {input.AccountId},
                                {input.FlowCode}, {input.NormalSide}, {input.ClientRequestId}, {access.UserId}, {Now})
                        """, token);
                }
                var result = (await db.Database.SqlQueryRaw<GrantFinanceAccountDto>("""
                    SELECT TOP(1) r.Id, r.SchemeId, r.AccountId, a.Code AS AccountCode,
                      a.Name AS AccountName, r.FlowCode, r.NormalSide
                    FROM dbo.ResearchGrantFinanceAccounts AS r
                    JOIN dbo.Accounts AS a ON a.Id = r.AccountId AND a.TenantId = r.TenantId
                    WHERE r.TenantId = {0} AND r.SchemeId = {1} AND r.AccountId = {2}
                    """, access.TenantId, input.SchemeId, input.AccountId).ToListAsync(token)).Single();
                return ApiResponse<GrantFinanceAccountDto>.SuccessResponse(result);
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { logger.LogWarning(ex, "Grant Finance mapping conflict tenant {Tenant}", access.TenantId); return Conflict<GrantFinanceAccountDto>(); }
    }

    public async Task<ApiResponse<List<GrantFinanceAccountDto>>> FinanceAccountsAsync(long schemeId,
        CancellationToken ct = default)
    {
        if (!GrantCoordinator(await access.RolesAsync(ct)) || schemeId <= 0) return Denied<List<GrantFinanceAccountDto>>();
        var rows = await db.Database.SqlQueryRaw<GrantFinanceAccountDto>("""
            SELECT TOP(200) r.Id, r.SchemeId, r.AccountId, a.Code AS AccountCode,
              a.Name AS AccountName, r.FlowCode, r.NormalSide
            FROM dbo.ResearchGrantFinanceAccounts AS r
            JOIN dbo.Accounts AS a ON a.TenantId = r.TenantId AND a.Id = r.AccountId
            WHERE r.TenantId = {0} AND r.SchemeId = {1} ORDER BY r.FlowCode, a.Code
            """, access.TenantId, schemeId).ToListAsync(ct);
        return ApiResponse<List<GrantFinanceAccountDto>>.SuccessResponse(rows);
    }

    private async Task<GrantScopeRow?> GrantScopeAsync(Guid grant, CancellationToken ct) =>
        (await db.Database.SqlQueryRaw<GrantScopeRow>("""
            SELECT g.Id, g.SchemeId, s.CurrencyCode, g.ApprovedAmount, g.StateCode,
              p.CampusId FROM dbo.ResearchGrants AS g
            JOIN dbo.ResearchGrantSchemes AS s ON s.TenantId = g.TenantId AND s.Id = g.SchemeId
            JOIN dbo.ResearchProposals AS p ON p.TenantId = g.TenantId AND p.Id = g.ProposalId
            WHERE g.TenantId = {0} AND g.PublicId = {1}
            """, access.TenantId, grant).ToListAsync(ct)).SingleOrDefault();

    public async Task<ApiResponse<GrantJournalLinkDto>> LinkJournalAsync(Guid grant,
        GrantJournalLinkCreateDto input, CancellationToken ct = default)
    {
        if (grant == Guid.Empty || input == null || input.ClientRequestId == Guid.Empty || input.JournalId <= 0)
            return ApiResponse<GrantJournalLinkDto>.ErrorResponse("Journal and request ID required.", 400);
        if (!GrantCoordinator(await access.RolesAsync(ct))) return Denied<GrantJournalLinkDto>();
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (!GrantCoordinator(await access.RolesAsync(token))) return Denied<GrantJournalLinkDto>();
                var row = await GrantScopeAsync(grant, token);
                if (row == null || row.StateCode is not ("Active" or "AmendmentPending" or "ClosurePending" or "Closed") ||
                    !await access.CampusAsync(row.CampusId, token)) return Denied<GrantJournalLinkDto>();
                var previous = (await db.Database.SqlQueryRaw<JournalLinkReplayRow>("""
                    SELECT GrantId, JournalId FROM dbo.ResearchGrantJournalLinks
                    WHERE TenantId = {0} AND CreateRequestId = {1}
                    """, access.TenantId, input.ClientRequestId).ToListAsync(token)).SingleOrDefault();
                if (previous != null && (previous.GrantId != row.Id || previous.JournalId != input.JournalId))
                    return Conflict<GrantJournalLinkDto>();
                var journal = await db.Journals.AsNoTracking().SingleOrDefaultAsync(j =>
                    j.TenantId == access.TenantId && j.Id == input.JournalId && !j.IsDeleted &&
                    j.SourceType == "Manual" && j.SourceId == null && j.ReversalOfJournalId == null &&
                    j.CurrencyCode == row.CurrencyCode &&
                    (j.State == JournalState.Posted || j.State == JournalState.Reversed), token);
                if (journal == null) return Denied<GrantJournalLinkDto>();
                if (previous == null)
                {
                    var configured = await db.Database.SqlQueryRaw<int>("""
                        SELECT COUNT(*) AS Value FROM dbo.JournalLines AS l
                        JOIN dbo.ResearchGrantFinanceAccounts AS r ON r.TenantId = l.TenantId AND r.AccountId = l.AccountId
                        WHERE l.TenantId = {0} AND l.JournalId = {1} AND r.SchemeId = {2} AND l.IsDeleted = 0
                        """, access.TenantId, input.JournalId, row.SchemeId).SingleAsync(token);
                    if (configured == 0) return ApiResponse<GrantJournalLinkDto>.ErrorResponse("Configure a grant Finance account appearing on this posted journal first.", 400);
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO dbo.ResearchGrantJournalLinks
                          (TenantId, GrantId, JournalId, CreateRequestId, CreatedByUserId, CreatedAtUtc)
                        VALUES ({access.TenantId}, {row.Id}, {input.JournalId},
                                {input.ClientRequestId}, {access.UserId}, {Now})
                        """, token);
                }
                return ApiResponse<GrantJournalLinkDto>.SuccessResponse(new()
                { JournalId = journal.Id, JournalNumber = journal.JournalNumber,
                    JournalDate = journal.JournalDate, CurrencyCode = journal.CurrencyCode });
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { logger.LogWarning(ex, "Grant journal link conflict tenant {Tenant}", access.TenantId); return Conflict<GrantJournalLinkDto>(); }
    }

    public async Task<ApiResponse<GrantFinanceReportDto>> FinanceReportAsync(Guid grant,
        CancellationToken ct = default)
    {
        if (!GrantCoordinator(await access.RolesAsync(ct))) return Denied<GrantFinanceReportDto>();
        var row = await GrantScopeAsync(grant, ct);
        if (row == null || !await access.CampusAsync(row.CampusId, ct)) return Denied<GrantFinanceReportDto>();
        // Both reversed originals and posted reversal journals belong to the GL: signed net movement cancels.
        var aggregate = await db.Database.SqlQueryRaw<GrantFinanceAggregateRow>("""
            SELECT
              COALESCE(SUM(CASE WHEN r.FlowCode = N'Disbursement' THEN
                CASE WHEN r.NormalSide = N'Debit' THEN l.DebitAmount - l.CreditAmount
                  ELSE l.CreditAmount - l.DebitAmount END ELSE 0 END), 0) AS Disbursed,
              COALESCE(SUM(CASE WHEN r.FlowCode = N'Expense' THEN
                CASE WHEN r.NormalSide = N'Debit' THEN l.DebitAmount - l.CreditAmount
                  ELSE l.CreditAmount - l.DebitAmount END ELSE 0 END), 0) AS Expenses,
              COALESCE(SUM(CASE WHEN r.FlowCode = N'Refund' THEN
                CASE WHEN r.NormalSide = N'Debit' THEN l.DebitAmount - l.CreditAmount
                  ELSE l.CreditAmount - l.DebitAmount END ELSE 0 END), 0) AS Refunds
            FROM dbo.ResearchGrantJournalLinks AS link
            JOIN dbo.Journals AS original ON original.TenantId = link.TenantId AND original.Id = link.JournalId
            JOIN dbo.Journals AS j ON j.TenantId = link.TenantId AND
                (j.Id = original.Id OR j.ReversalOfJournalId = original.Id)
            JOIN dbo.JournalLines AS l ON l.TenantId = j.TenantId AND l.JournalId = j.Id AND l.IsDeleted = 0
            JOIN dbo.ResearchGrantFinanceAccounts AS r ON r.TenantId = l.TenantId AND r.AccountId = l.AccountId
              AND r.SchemeId = {2}
            WHERE link.TenantId = {0} AND link.GrantId = {1} AND original.IsDeleted = 0 AND j.IsDeleted = 0
              AND j.State IN (4, 6)
            """, access.TenantId, row.Id, row.SchemeId).SingleAsync(ct);
        var counts = await db.Database.SqlQueryRaw<GrantFinanceCountsRow>("""
            SELECT COUNT(*) AS LinkedJournals,
              COALESCE(SUM(CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.JournalLines AS l
                JOIN dbo.ResearchGrantFinanceAccounts AS r ON r.TenantId = l.TenantId
                  AND r.AccountId = l.AccountId AND r.SchemeId = {2}
                WHERE l.TenantId = link.TenantId AND l.JournalId = link.JournalId AND l.IsDeleted = 0)
                THEN 1 ELSE 0 END), 0) AS MissingCount
            FROM dbo.ResearchGrantJournalLinks AS link
            WHERE link.TenantId = {0} AND link.GrantId = {1}
            """, access.TenantId, row.Id, row.SchemeId).SingleAsync(ct);
        var budget = row.ApprovedAmount ?? 0;
        return ApiResponse<GrantFinanceReportDto>.SuccessResponse(new()
        {
            GrantReference = grant, CurrencyCode = row.CurrencyCode, ApprovedBudget = budget,
            Disbursed = aggregate.Disbursed, Expenses = aggregate.Expenses, Refunds = aggregate.Refunds,
            AvailableBudget = budget - aggregate.Expenses + aggregate.Refunds,
            Undisbursed = budget - aggregate.Disbursed + aggregate.Refunds,
            LinkedJournals = counts.LinkedJournals, JournalsMissingAccountMapping = counts.MissingCount
        });
    }

    private sealed class OutcomeCreatedRow
    {
        public long Id { get; set; }
        public string CreateSha256 { get; set; } = "";
        public long CreatedByUserId { get; set; }
    }
    private sealed class OutcomeMapReplayRow
    {
        public long StandardId { get; set; }
        public long OutcomeId { get; set; }
    }
    private sealed class AssessmentEvidenceScopeRow
    {
        public long Id { get; set; }
        public long CampusId { get; set; }
        public long? AcademicProgramId { get; set; }
        public long OwnerUserId { get; set; }
        public string StateCode { get; set; } = "";
    }
    private sealed class AcademicLinkReplayRow { public long ResultPublicationId { get; set; } }
    private sealed class FinanceSchemeRow
    {
        public long Id { get; set; }
        public string CurrencyCode { get; set; } = "";
    }
    private sealed class FinanceMappingReplayRow
    {
        public long SchemeId { get; set; }
        public long AccountId { get; set; }
        public string FlowCode { get; set; } = "";
        public string NormalSide { get; set; } = "";
    }
    private sealed class GrantScopeRow
    {
        public long Id { get; set; }
        public long SchemeId { get; set; }
        public string CurrencyCode { get; set; } = "";
        public decimal? ApprovedAmount { get; set; }
        public string StateCode { get; set; } = "";
        public long CampusId { get; set; }
    }
    private sealed class JournalLinkReplayRow
    {
        public long GrantId { get; set; }
        public long JournalId { get; set; }
    }
    private sealed class GrantFinanceAggregateRow
    {
        public decimal Disbursed { get; set; }
        public decimal Expenses { get; set; }
        public decimal Refunds { get; set; }
    }
    private sealed class GrantFinanceCountsRow
    {
        public int LinkedJournals { get; set; }
        public int MissingCount { get; set; }
    }
}
