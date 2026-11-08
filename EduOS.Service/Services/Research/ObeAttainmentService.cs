using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Research;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.AssessmentAdministration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Research;

/// <summary>
/// OBE is a versioned interpretation of canonical published course-grade evidence,
/// never a second result engine. This release uses course-grade CLO proxies;
/// assessment-component rubric attainment must not be inferred from editable marks.
/// </summary>
public sealed class ObeAttainmentService(EduOSDbContext db, ResearchWorkflowAccess access,
    TimeProvider clock, ILogger<ObeAttainmentService> logger) : IObeAttainmentService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Record unavailable.", 404);
    private static ApiResponse<T> Conflict<T>() => ApiResponse<T>.ErrorResponse("Conflicting request or version.", 409);
    private static ApiResponse<T> Invalid<T>(string message) => ApiResponse<T>.ErrorResponse(message, 400);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool Code(string code) => code.Length is >= 2 and <= 40 &&
        code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_');
    private static bool Coordinator(IReadOnlySet<string> roles) =>
        ResearchWorkflowAccess.AccreditationCoordinator(roles);

    private async Task<bool> ProgramScopeAsync(long campus, long program, CancellationToken ct) =>
        access.Authenticated && campus > 0 && program > 0 && await access.CampusAsync(campus, ct) &&
        await db.ProgramCampuses.AsNoTracking().AnyAsync(pc => pc.TenantId == access.TenantId &&
            pc.CampusId == campus && pc.AcademicProgramId == program && pc.IsActive && !pc.IsDeleted &&
            db.AcademicPrograms.Any(p => p.TenantId == access.TenantId && p.Id == program &&
                p.IsActive && !p.IsDeleted), ct);

    public async Task<ApiResponse<List<ObeCurriculumCourseDto>>> CoursesAsync(long campusId, long programId,
        CancellationToken ct = default)
    {
        if (!Coordinator(await access.RolesAsync(ct)) || !await ProgramScopeAsync(campusId, programId, ct))
            return Denied<List<ObeCurriculumCourseDto>>();
        var rows = await (from cs in db.CurriculumSubjects.AsNoTracking()
            join curriculum in db.AcademicCurricula.AsNoTracking() on cs.AcademicCurriculumId equals curriculum.Id
            join subject in db.Subjects.AsNoTracking() on cs.SubjectId equals subject.Id
            where cs.TenantId == access.TenantId && curriculum.TenantId == access.TenantId &&
                subject.TenantId == access.TenantId && curriculum.AcademicProgramId == programId &&
                cs.IsActive && !cs.IsDeleted && curriculum.IsActive && !curriculum.IsDeleted &&
                subject.IsActive && !subject.IsDeleted
            orderby subject.Code, curriculum.VersionNo descending
            select new ObeCurriculumCourseDto { CurriculumSubjectId = cs.Id,
                SubjectCode = subject.Code, SubjectName = subject.Name }).Take(250).ToListAsync(ct);
        return ApiResponse<List<ObeCurriculumCourseDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<List<ObePolicyDto>>> PoliciesAsync(long campusId, long programId,
        CancellationToken ct = default)
    {
        if (!Coordinator(await access.RolesAsync(ct)) || !await ProgramScopeAsync(campusId, programId, ct))
            return Denied<List<ObePolicyDto>>();
        var rows = await db.Database.SqlQueryRaw<ObePolicyDto>("""
            SELECT TOP(100) Id, CampusId, AcademicProgramId, VersionNo, CreatedAtUtc
            FROM dbo.ObePolicies WHERE TenantId = {0} AND CampusId = {1} AND AcademicProgramId = {2}
            ORDER BY VersionNo DESC
            """, access.TenantId, campusId, programId).ToListAsync(ct);
        return ApiResponse<List<ObePolicyDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<List<ObePublishedBatchDto>>> PublicationsAsync(long campusId, long programId,
        CancellationToken ct = default)
    {
        if (!Coordinator(await access.RolesAsync(ct)) || !await ProgramScopeAsync(campusId, programId, ct))
            return Denied<List<ObePublishedBatchDto>>();
        var rows = await (from p in db.ResultPublications.AsNoTracking()
            join batch in db.AcademicBatches.AsNoTracking() on p.AcademicBatchId equals batch.Id
            join exam in db.Assessments.AsNoTracking() on p.AssessmentId equals exam.Id
            where p.TenantId == access.TenantId && batch.TenantId == access.TenantId &&
                exam.TenantId == access.TenantId && !p.IsDeleted && !batch.IsDeleted && !exam.IsDeleted &&
                batch.CampusId == campusId && batch.AcademicProgramId == programId &&
                exam.CampusId == campusId && p.State == ResultPublicationState.Published &&
                p.HasCourseGradeSnapshots && p.PublishedAt != null
            orderby p.PublishedAt descending, p.Id descending
            select new ObePublishedBatchDto { ResultPublicationId = p.Id, VersionNo = p.VersionNo,
                AssessmentName = exam.Name, BatchName = batch.Name,
                PublishedAtUtc = p.PublishedAt!.Value }).Take(100).ToListAsync(ct);
        return ApiResponse<List<ObePublishedBatchDto>>.SuccessResponse(rows);
    }

    private async Task<ObePolicyDetailDto?> ReadPolicyAsync(long id, CancellationToken ct)
    {
        var policy = (await db.Database.SqlQueryRaw<ObePolicyDto>("""
            SELECT Id, CampusId, AcademicProgramId, VersionNo, CreatedAtUtc
            FROM dbo.ObePolicies WHERE TenantId = {0} AND Id = {1}
            """, access.TenantId, id).ToListAsync(ct)).SingleOrDefault();
        if (policy == null) return null;
        var courses = await db.Database.SqlQueryRaw<ObeCourseOutcomeDto>("""
            SELECT Id, CurriculumSubjectId, Code, Statement FROM dbo.ObeCourseOutcomes
            WHERE TenantId = {0} AND PolicyId = {1} ORDER BY Code
            """, access.TenantId, id).ToListAsync(ct);
        var contributions = await db.Database.SqlQueryRaw<ObePloContributionDto>("""
            SELECT m.CourseOutcomeId, m.ProgramOutcomeId, o.Code AS ProgramOutcomeCode,
                m.WeightPercent FROM dbo.ObePloContributions AS m
            JOIN dbo.AccreditationProgramOutcomes AS o ON o.TenantId = m.TenantId
                AND o.Id = m.ProgramOutcomeId
            WHERE m.TenantId = {0} AND m.PolicyId = {1}
            ORDER BY m.ProgramOutcomeId, m.CourseOutcomeId
            """, access.TenantId, id).ToListAsync(ct);
        return new ObePolicyDetailDto { Policy = policy, CourseOutcomes = courses,
            Contributions = contributions };
    }

    public async Task<ApiResponse<ObePolicyDetailDto>> PolicyAsync(long policyId, CancellationToken ct = default)
    {
        if (!Coordinator(await access.RolesAsync(ct))) return Denied<ObePolicyDetailDto>();
        var detail = await ReadPolicyAsync(policyId, ct);
        return detail != null && await ProgramScopeAsync(detail.Policy.CampusId, detail.Policy.AcademicProgramId, ct)
            ? ApiResponse<ObePolicyDetailDto>.SuccessResponse(detail) : Denied<ObePolicyDetailDto>();
    }

    public async Task<ApiResponse<ObePolicyDetailDto>> CreatePolicyAsync(ObePolicyCreateDto input,
        CancellationToken ct = default)
    {
        if (!Coordinator(await access.RolesAsync(ct))) return Denied<ObePolicyDetailDto>();
        if (input == null || input.ClientRequestId == Guid.Empty || input.VersionNo is < 1 or > 1000 ||
            input.CourseOutcomes == null || input.Contributions == null ||
            input.CourseOutcomes.Count is < 1 or > 60 || input.Contributions.Count is < 1 or > 180)
            return Invalid<ObePolicyDetailDto>("Invalid version or mapping size.");
        if (input.CourseOutcomes.Any(c => c == null) || input.Contributions.Any(c => c == null))
            return Invalid<ObePolicyDetailDto>("Invalid course outcome or mapping row.");
        var courses = input.CourseOutcomes.Select(c => new ObeCourseOutcomeInputDto
        { CurriculumSubjectId = c.CurriculumSubjectId, Code = (c.Code ?? "").Trim().ToUpperInvariant(),
            Statement = (c.Statement ?? "").Trim() }).OrderBy(c => c.Code).ToList();
        var links = input.Contributions.Select(c => new ObePloContributionInputDto
        { CourseOutcomeCode = (c.CourseOutcomeCode ?? "").Trim().ToUpperInvariant(),
            ProgramOutcomeId = c.ProgramOutcomeId, WeightPercent = c.WeightPercent })
            .OrderBy(c => c.ProgramOutcomeId).ThenBy(c => c.CourseOutcomeCode).ToList();
        if (courses.Any(c => c.CurriculumSubjectId <= 0 || !Code(c.Code) || c.Statement.Length is < 10 or > 1000) ||
            courses.Select(c => c.Code).Distinct().Count() != courses.Count ||
            courses.Select(c => c.CurriculumSubjectId).Distinct().Count() != courses.Count ||
            links.Any(x => !courses.Any(c => c.Code == x.CourseOutcomeCode) || x.ProgramOutcomeId <= 0 ||
                x.WeightPercent <= 0 || x.WeightPercent > 100 || decimal.Round(x.WeightPercent, 4) != x.WeightPercent) ||
            links.Select(x => (x.ProgramOutcomeId, x.CourseOutcomeCode)).Distinct().Count() != links.Count ||
            links.GroupBy(x => x.ProgramOutcomeId).Any(g => g.Sum(x => x.WeightPercent) != 100m) ||
            courses.Any(c => !links.Any(x => x.CourseOutcomeCode == c.Code)))
            return Invalid<ObePolicyDetailDto>("CLO mappings must be unique, valid, and each PLO must total 100%. ");
        if (!await ProgramScopeAsync(input.CampusId, input.AcademicProgramId, ct))
            return Denied<ObePolicyDetailDto>();
        var courseIds = courses.Select(c => c.CurriculumSubjectId).ToArray();
        var validCourses = await (from cs in db.CurriculumSubjects.AsNoTracking()
            join cur in db.AcademicCurricula.AsNoTracking() on cs.AcademicCurriculumId equals cur.Id
            where cs.TenantId == access.TenantId && cur.TenantId == access.TenantId &&
                cur.AcademicProgramId == input.AcademicProgramId && cs.IsActive && !cs.IsDeleted &&
                cur.IsActive && !cur.IsDeleted && courseIds.Contains(cs.Id)
            select cs.Id).Distinct().CountAsync(ct);
        var validOutcomes = await db.Database.SqlQueryRaw<ProgramOutcomeDto>("""
            SELECT Id, AcademicProgramId, Code, VersionNo, Statement FROM dbo.AccreditationProgramOutcomes
            WHERE TenantId = {0} AND AcademicProgramId = {1}
            """, access.TenantId, input.AcademicProgramId).ToListAsync(ct);
        if (validCourses != courses.Count || links.Select(x => x.ProgramOutcomeId).Distinct()
            .Any(id => !validOutcomes.Any(o => o.Id == id)) ||
            validOutcomes.Where(o => links.Any(x => x.ProgramOutcomeId == o.Id))
                .GroupBy(o => o.Code).Any(g => g.Count() > 1)) return Denied<ObePolicyDetailDto>();
        var signature = Hash(JsonSerializer.Serialize(new { input.CampusId, input.AcademicProgramId,
            input.VersionNo, courses, links }));
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (!Coordinator(await access.RolesAsync(token)) ||
                    !await ProgramScopeAsync(input.CampusId, input.AcademicProgramId, token))
                    return Denied<ObePolicyDetailDto>();
                var previous = (await db.Database.SqlQueryRaw<PolicyReplayRow>("""
                    SELECT Id, InputSha256, CreatedByUserId FROM dbo.ObePolicies
                    WHERE TenantId = {0} AND CreateRequestId = {1}
                    """, access.TenantId, input.ClientRequestId).ToListAsync(token)).SingleOrDefault();
                if (previous != null)
                {
                    if (previous.InputSha256 != signature || previous.CreatedByUserId != access.UserId)
                        return Conflict<ObePolicyDetailDto>();
                    return ApiResponse<ObePolicyDetailDto>.SuccessResponse((await ReadPolicyAsync(previous.Id, token))!);
                }
                // Revalidate policy ownership inside the serializable transaction.
                var courseCount = await (from cs in db.CurriculumSubjects.AsNoTracking()
                    join cur in db.AcademicCurricula.AsNoTracking() on cs.AcademicCurriculumId equals cur.Id
                    where cs.TenantId == access.TenantId && cur.TenantId == access.TenantId &&
                        cur.AcademicProgramId == input.AcademicProgramId && cs.IsActive && !cs.IsDeleted &&
                        cur.IsActive && !cur.IsDeleted && courseIds.Contains(cs.Id)
                    select cs.Id).Distinct().CountAsync(token);
                var allowedOutcomes = await db.Database.SqlQueryRaw<ProgramOutcomeDto>("""
                    SELECT Id, AcademicProgramId, Code, VersionNo, Statement FROM dbo.AccreditationProgramOutcomes
                    WHERE TenantId = {0} AND AcademicProgramId = {1}
                    """, access.TenantId, input.AcademicProgramId).ToListAsync(token);
                if (courseCount != courses.Count || links.Any(x => !allowedOutcomes.Any(o => o.Id == x.ProgramOutcomeId)) ||
                    allowedOutcomes.Where(o => links.Any(x => x.ProgramOutcomeId == o.Id))
                        .GroupBy(o => o.Code).Any(g => g.Count() > 1))
                    return Denied<ObePolicyDetailDto>();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO dbo.ObePolicies
                        (TenantId, CampusId, AcademicProgramId, VersionNo, CreateRequestId,
                         InputSha256, CreatedByUserId, CreatedAtUtc)
                    VALUES ({access.TenantId}, {input.CampusId}, {input.AcademicProgramId}, {input.VersionNo},
                        {input.ClientRequestId}, {signature}, {access.UserId}, {Now})
                    """, token);
                var id = await db.Database.SqlQueryRaw<long>("""
                    SELECT Id AS Value FROM dbo.ObePolicies WHERE TenantId = {0} AND CreateRequestId = {1}
                    """, access.TenantId, input.ClientRequestId).SingleAsync(token);
                var newIds = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var course in courses)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO dbo.ObeCourseOutcomes (TenantId, PolicyId, CurriculumSubjectId, Code, Statement)
                        VALUES ({access.TenantId}, {id}, {course.CurriculumSubjectId}, {course.Code}, {course.Statement})
                        """, token);
                }
                var saved = await db.Database.SqlQueryRaw<ObeCourseOutcomeDto>("""
                    SELECT Id, CurriculumSubjectId, Code, Statement FROM dbo.ObeCourseOutcomes
                    WHERE TenantId = {0} AND PolicyId = {1}
                    """, access.TenantId, id).ToListAsync(token);
                foreach (var c in saved) newIds[c.Code] = c.Id;
                foreach (var link in links)
                {
                    var cloId = newIds[link.CourseOutcomeCode];
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO dbo.ObePloContributions
                            (TenantId, PolicyId, CourseOutcomeId, ProgramOutcomeId, WeightPercent)
                        VALUES ({access.TenantId}, {id}, {cloId}, {link.ProgramOutcomeId}, {link.WeightPercent})
                        """, token);
                }
                return ApiResponse<ObePolicyDetailDto>.SuccessResponse((await ReadPolicyAsync(id, token))!);
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { logger.LogWarning(ex, "OBE policy conflict for tenant {TenantId}", access.TenantId); return Conflict<ObePolicyDetailDto>(); }
    }

    private async Task<ApiResponse<ObePreviewDto>> ComputeAsync(ObePolicyDetailDto policy,
        long publicationId, CancellationToken ct)
    {
        var scope = await (from p in db.ResultPublications.AsNoTracking()
            join batch in db.AcademicBatches.AsNoTracking() on p.AcademicBatchId equals batch.Id
            join assessment in db.Assessments.AsNoTracking() on p.AssessmentId equals assessment.Id
            where p.TenantId == access.TenantId && batch.TenantId == access.TenantId &&
                assessment.TenantId == access.TenantId && p.Id == publicationId && !p.IsDeleted &&
                !batch.IsDeleted && !assessment.IsDeleted &&
                p.State == ResultPublicationState.Published && p.HasCourseGradeSnapshots &&
                p.PublishedAt != null && p.PublishedAt <= Now &&
                batch.CampusId == policy.Policy.CampusId && assessment.CampusId == policy.Policy.CampusId &&
                batch.AcademicProgramId == policy.Policy.AcademicProgramId
            select new { p.VersionNo, p.AssessmentId, p.AcademicBatchId,
                assessment.AcademicYearId }).SingleOrDefaultAsync(ct);
        if (scope == null) return Denied<ObePreviewDto>();
        var totalSummaries = await db.StudentResultSummaries.AsNoTracking().CountAsync(x =>
            x.TenantId == access.TenantId && x.ResultPublicationId == publicationId && !x.IsDeleted, ct);
        var summaries = await (from summary in db.StudentResultSummaries.AsNoTracking()
            join enrollment in db.StudentEnrollments.AsNoTracking()
                on new { summary.TenantId, Id = summary.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in db.Students.AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where summary.TenantId == access.TenantId && !summary.IsDeleted &&
                !enrollment.IsDeleted && !student.IsDeleted &&
                summary.ResultPublicationId == publicationId &&
                summary.AssessmentId == scope.AssessmentId &&
                summary.PublicationVersionNo == scope.VersionNo &&
                enrollment.AcademicBatchId == scope.AcademicBatchId &&
                enrollment.CampusId == policy.Policy.CampusId &&
                enrollment.AcademicProgramId == policy.Policy.AcademicProgramId &&
                enrollment.AcademicYearId == scope.AcademicYearId
            select new { summary.StudentEnrollmentId, summary.CourseCount, summary.IsWithheld })
            .Take(10001).ToListAsync(ct);
        if (summaries.Count is 0 or > 10000 || summaries.Count != totalSummaries ||
            summaries.Any(x => x.IsWithheld || x.CourseCount == null || x.CourseCount < 1) ||
            summaries.Select(x => x.StudentEnrollmentId).Distinct().Count() != summaries.Count)
            return ApiResponse<ObePreviewDto>.ErrorResponse("Publication roster or course-grade snapshots are incomplete or exceed the interactive report limit.", 422);
        var snapshotCounts = await db.StudentResultCourseSnapshots.AsNoTracking().Where(x =>
            x.TenantId == access.TenantId && x.ResultPublicationId == publicationId && !x.IsDeleted)
            .GroupBy(x => x.StudentEnrollmentId)
            .Select(g => new { EnrollmentId = g.Key, Count = g.Count() }).Take(10001).ToListAsync(ct);
        var actualCounts = snapshotCounts.ToDictionary(x => x.EnrollmentId, x => x.Count);
        if (snapshotCounts.Count != summaries.Count || summaries.Any(x =>
            !actualCounts.TryGetValue(x.StudentEnrollmentId, out var count) || count != x.CourseCount))
            return ApiResponse<ObePreviewDto>.ErrorResponse("Published course-grade reconciliation failed.", 422);
        var courseIds = policy.CourseOutcomes.Select(x => x.CurriculumSubjectId).ToArray();
        var rows = await db.StudentResultCourseSnapshots.AsNoTracking().Where(x =>
            x.TenantId == access.TenantId && x.ResultPublicationId == publicationId && !x.IsDeleted &&
            courseIds.Contains(x.CurriculumSubjectId))
            .Select(x => new { x.StudentEnrollmentId, x.CurriculumSubjectId, x.FullMarks,
                x.ObtainedMarks, x.IsAbsent, x.IsWithheld }).Take(50001).ToListAsync(ct);
        if (rows.Count is 0 or > 50000)
            return ApiResponse<ObePreviewDto>.ErrorResponse("Published evidence exceeds the interactive limit.", 422);
        var validCourseProvenance = await (from grade in db.StudentResultCourseSnapshots.AsNoTracking()
            join enrollment in db.StudentEnrollments.AsNoTracking()
                on new { grade.TenantId, Id = grade.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join subject in db.CurriculumSubjects.AsNoTracking()
                on new { grade.TenantId, Id = grade.CurriculumSubjectId }
                equals new { subject.TenantId, subject.Id }
            where grade.TenantId == access.TenantId && !grade.IsDeleted && !enrollment.IsDeleted &&
                !subject.IsDeleted && grade.ResultPublicationId == publicationId &&
                enrollment.AcademicBatchId == scope.AcademicBatchId &&
                subject.AcademicCurriculumId == enrollment.AcademicCurriculumId &&
                courseIds.Contains(grade.CurriculumSubjectId)
            select grade.Id).CountAsync(ct);
        if (validCourseProvenance != rows.Count)
            return ApiResponse<ObePreviewDto>.ErrorResponse("Course evidence does not match the enrolled curriculum.", 422);
        var courseByClo = policy.CourseOutcomes.ToDictionary(x => x.Id, x => x.CurriculumSubjectId);
        var mapping = policy.Contributions.Select(x => new PublishedOutcomeAttainmentPolicy.Mapping(
            x.ProgramOutcomeId, courseByClo[x.CourseOutcomeId], x.WeightPercent)).ToList();
        var evidence = rows.Select(x => new PublishedOutcomeAttainmentPolicy.Evidence(
            publicationId, scope.VersionNo, x.StudentEnrollmentId, x.CurriculumSubjectId,
            x.FullMarks, x.ObtainedMarks, x.IsAbsent, x.IsWithheld, true, true)).ToList();
        try
        {
            var roster = summaries.Select(x => x.StudentEnrollmentId).ToArray();
            var outcomes = PublishedOutcomeAttainmentPolicy.CalculateCompleteCohortForRoster(
                evidence, mapping, roster);
            var digest = Hash(JsonSerializer.Serialize(new { publicationId, scope.VersionNo,
                PolicyId = policy.Policy.Id, Evidence = evidence.OrderBy(x => x.EnrollmentId)
                    .ThenBy(x => x.CurriculumSubjectId).ToArray(),
                Mappings = mapping.OrderBy(x => x.OutcomeId).ThenBy(x => x.CurriculumSubjectId).ToArray() }));
            return ApiResponse<ObePreviewDto>.SuccessResponse(new ObePreviewDto
            {
                PolicyId = policy.Policy.Id, ResultPublicationId = publicationId,
                PublicationVersionNo = scope.VersionNo, LearnerCount = roster.Length,
                EvidenceSha256 = digest,
                CourseOutcomes = policy.CourseOutcomes.Select(clo => new ObeCloAttainmentDto
                {
                    CourseOutcomeId = clo.Id, CourseOutcomeCode = clo.Code,
                    CurriculumSubjectId = clo.CurriculumSubjectId,
                    AttainmentPercent = decimal.Round(rows.Where(x => x.CurriculumSubjectId == clo.CurriculumSubjectId)
                        .Average(x => x.ObtainedMarks / x.FullMarks * 100m), 2, MidpointRounding.AwayFromZero)
                }).ToList(),
                Outcomes = outcomes.Select(x => new ObeOutcomeAttainmentDto
                { ProgramOutcomeId = x.OutcomeId, ProgramOutcomeCode = policy.Contributions.First(c =>
                    c.ProgramOutcomeId == x.OutcomeId).ProgramOutcomeCode,
                    AttainmentPercent = x.AttainmentPercent, EvidenceCount = x.EvidenceCount }).ToList()
            });
        }
        catch (ArgumentException ex)
        { return ApiResponse<ObePreviewDto>.ErrorResponse(ex.Message, 422); }
    }

    public async Task<ApiResponse<ObePreviewDto>> PreviewAsync(long policyId, long publicationId,
        CancellationToken ct = default)
    {
        var result = await PolicyAsync(policyId, ct);
        return result.Success && result.Data != null ? await ComputeAsync(result.Data, publicationId, ct)
            : Denied<ObePreviewDto>();
    }

    private sealed class PolicyReplayRow
    {
        public long Id { get; set; }
        public string InputSha256 { get; set; } = "";
        public long CreatedByUserId { get; set; }
    }

    private sealed class ReportRecord
    {
        public Guid Reference { get; set; }
        public long PolicyId { get; set; }
        public long CampusId { get; set; }
        public long AcademicProgramId { get; set; }
        public long ResultPublicationId { get; set; }
        public int PublicationVersionNo { get; set; }
        public string StateCode { get; set; } = "";
        public long SubmittedByUserId { get; set; }
        public DateTime SubmittedAtUtc { get; set; }
        public long? ReviewedByUserId { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
        public Guid? ReviewRequestId { get; set; }
        public string? ReviewReason { get; set; }
        public string SnapshotJson { get; set; } = "";
        public string SnapshotSha256 { get; set; } = "";
    }

    private const string ReportSelect = """
        SELECT r.Reference, r.PolicyId, p.CampusId, p.AcademicProgramId,
          r.ResultPublicationId, r.PublicationVersionNo, r.StateCode, r.SubmittedByUserId,
          r.SubmittedAtUtc, r.ReviewedByUserId, r.ReviewedAtUtc, r.ReviewRequestId,
          r.ReviewReason, r.SnapshotJson, r.SnapshotSha256
        FROM dbo.ObeAttainmentReports AS r
        JOIN dbo.ObePolicies AS p ON p.TenantId = r.TenantId AND p.Id = r.PolicyId
        """;

    private static ObeReportDto? MapReport(ReportRecord record)
    {
        if (Hash(record.SnapshotJson) != record.SnapshotSha256) return null;
        var snapshot = JsonSerializer.Deserialize<ObePreviewDto>(record.SnapshotJson);
        if (snapshot == null || snapshot.PolicyId != record.PolicyId ||
            snapshot.ResultPublicationId != record.ResultPublicationId ||
            snapshot.PublicationVersionNo != record.PublicationVersionNo) return null;
        return new ObeReportDto { Reference = record.Reference, PolicyId = record.PolicyId,
            CampusId = record.CampusId, AcademicProgramId = record.AcademicProgramId,
            ResultPublicationId = record.ResultPublicationId, PublicationVersionNo = record.PublicationVersionNo,
            StateCode = record.StateCode, SubmittedByUserId = record.SubmittedByUserId,
            SubmittedAtUtc = record.SubmittedAtUtc, ReviewedByUserId = record.ReviewedByUserId,
            ReviewedAtUtc = record.ReviewedAtUtc, ReviewReason = record.ReviewReason, Snapshot = snapshot };
    }

    private async Task<ReportRecord?> ReadReportAsync(Guid reference, CancellationToken ct) =>
        (await db.Database.SqlQueryRaw<ReportRecord>(ReportSelect + """
             WHERE r.TenantId = {0} AND r.Reference = {1}
             """, access.TenantId, reference).ToListAsync(ct)).SingleOrDefault();

    public async Task<ApiResponse<ObeReportDto>> ReportAsync(Guid reference, CancellationToken ct = default)
    {
        if (reference == Guid.Empty || !Coordinator(await access.RolesAsync(ct))) return Denied<ObeReportDto>();
        var row = await ReadReportAsync(reference, ct);
        if (row == null || !await ProgramScopeAsync(row.CampusId, row.AcademicProgramId, ct))
            return Denied<ObeReportDto>();
        try
        {
            var result = MapReport(row);
            return result == null ? ApiResponse<ObeReportDto>.ErrorResponse("Report integrity check failed.", 409)
                : ApiResponse<ObeReportDto>.SuccessResponse(result);
        }
        catch (JsonException)
        { return ApiResponse<ObeReportDto>.ErrorResponse("Report integrity check failed.", 409); }
    }

    public async Task<ApiResponse<List<ObeReportDto>>> ReportsAsync(long campusId, long programId,
        CancellationToken ct = default)
    {
        if (!Coordinator(await access.RolesAsync(ct)) || !await ProgramScopeAsync(campusId, programId, ct))
            return Denied<List<ObeReportDto>>();
        var rows = await db.Database.SqlQueryRaw<ReportRecord>(ReportSelect + """
            WHERE r.TenantId = {0} AND p.CampusId = {1} AND p.AcademicProgramId = {2}
            ORDER BY r.SubmittedAtUtc DESC, r.Reference
            OFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY
            """, access.TenantId, campusId, programId).ToListAsync(ct);
        try
        {
            var reports = rows.Select(MapReport).ToList();
            return reports.Any(x => x == null)
                ? ApiResponse<List<ObeReportDto>>.ErrorResponse("Report integrity check failed.", 409)
                : ApiResponse<List<ObeReportDto>>.SuccessResponse(reports.Select(x => x!).ToList());
        }
        catch (JsonException)
        { return ApiResponse<List<ObeReportDto>>.ErrorResponse("Report integrity check failed.", 409); }
    }

    public async Task<ApiResponse<ObeReportDto>> SubmitAsync(long policyId, ObeSubmitDto input,
        CancellationToken ct = default)
    {
        if (input == null || input.ClientRequestId == Guid.Empty || input.ResultPublicationId <= 0)
            return Invalid<ObeReportDto>("Publication and retry request ID are required.");
        if (!Coordinator(await access.RolesAsync(ct))) return Denied<ObeReportDto>();
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var policyResult = await PolicyAsync(policyId, token);
                if (!policyResult.Success || policyResult.Data == null) return Denied<ObeReportDto>();
                var prior = (await db.Database.SqlQueryRaw<Guid>("""
                    SELECT Reference AS Value FROM dbo.ObeAttainmentReports
                    WHERE TenantId = {0} AND SubmitRequestId = {1}
                    """, access.TenantId, input.ClientRequestId).ToListAsync(token)).SingleOrDefault();
                if (prior != Guid.Empty)
                {
                    var earlier = await ReadReportAsync(prior, token);
                    return earlier != null && earlier.PolicyId == policyId &&
                        earlier.ResultPublicationId == input.ResultPublicationId &&
                        earlier.SubmittedByUserId == access.UserId
                        ? await ReportAsync(prior, token) : Conflict<ObeReportDto>();
                }
                var preview = await ComputeAsync(policyResult.Data, input.ResultPublicationId, token);
                if (!preview.Success || preview.Data == null)
                    return ApiResponse<ObeReportDto>.ErrorResponse(preview.Message, preview.StatusCode);
                var json = JsonSerializer.Serialize(preview.Data);
                var reference = Guid.NewGuid();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO dbo.ObeAttainmentReports
                      (TenantId, Reference, PolicyId, ResultPublicationId, PublicationVersionNo,
                       StateCode, SubmitRequestId, SubmittedByUserId, SubmittedAtUtc,
                       SnapshotJson, SnapshotSha256)
                    VALUES ({access.TenantId}, {reference}, {policyId}, {input.ResultPublicationId},
                        {preview.Data.PublicationVersionNo}, {"Submitted"}, {input.ClientRequestId},
                        {access.UserId}, {Now}, {json}, {Hash(json)})
                    """, token);
                return await ReportAsync(reference, token);
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { logger.LogWarning(ex, "OBE report submission conflict tenant {TenantId}", access.TenantId); return Conflict<ObeReportDto>(); }
    }

    public async Task<ApiResponse<ObeReportDto>> ReviewAsync(Guid reference, ObeReviewDto input,
        CancellationToken ct = default)
    {
        if (reference == Guid.Empty || input == null || input.ClientRequestId == Guid.Empty ||
            input.Decision is not ("Approve" or "Reject") ||
            (input.Reason?.Trim().Length ?? 0) is < 10 or > 500)
            return Invalid<ObeReportDto>("Decision and independent review reason are required.");
        if (!Coordinator(await access.RolesAsync(ct))) return Denied<ObeReportDto>();
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var row = await ReadReportAsync(reference, token);
                if (row == null || !Coordinator(await access.RolesAsync(token)) ||
                    !await ProgramScopeAsync(row.CampusId, row.AcademicProgramId, token) ||
                    row.SubmittedByUserId == access.UserId) return Denied<ObeReportDto>();
                if (row.StateCode != "Submitted")
                    return row.ReviewRequestId == input.ClientRequestId &&
                        row.ReviewedByUserId == access.UserId &&
                        row.StateCode == (input.Decision == "Approve" ? "Approved" : "Rejected") &&
                        row.ReviewReason == input.Reason.Trim()
                        ? await ReportAsync(reference, token) : Conflict<ObeReportDto>();
                if (MapReport(row) == null)
                    return ApiResponse<ObeReportDto>.ErrorResponse("Report integrity check failed.", 409);
                if (input.Decision == "Approve")
                {
                    var policy = await PolicyAsync(row.PolicyId, token);
                    if (!policy.Success || policy.Data == null) return Denied<ObeReportDto>();
                    var currentEvidence = await ComputeAsync(policy.Data, row.ResultPublicationId, token);
                    var frozen = JsonSerializer.Deserialize<ObePreviewDto>(row.SnapshotJson);
                    if (!currentEvidence.Success || currentEvidence.Data == null || frozen == null ||
                        currentEvidence.Data.PublicationVersionNo != row.PublicationVersionNo ||
                        currentEvidence.Data.EvidenceSha256 != frozen.EvidenceSha256)
                        return Conflict<ObeReportDto>();
                }
                var next = input.Decision == "Approve" ? "Approved" : "Rejected";
                var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE dbo.ObeAttainmentReports SET StateCode = {next}, ReviewedByUserId = {access.UserId},
                        ReviewedAtUtc = {Now}, ReviewRequestId = {input.ClientRequestId},
                        ReviewReason = {input.Reason.Trim()}
                    WHERE TenantId = {access.TenantId} AND Reference = {reference}
                        AND StateCode = {"Submitted"} AND SubmittedByUserId <> {access.UserId}
                    """, token);
                return changed == 1 ? await ReportAsync(reference, token) : Conflict<ObeReportDto>();
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { logger.LogWarning(ex, "OBE review conflict tenant {TenantId}", access.TenantId); return Conflict<ObeReportDto>(); }
    }
}
