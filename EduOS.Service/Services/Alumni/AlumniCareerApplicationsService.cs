using System.Data;
using System.Security.Cryptography;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Alumni;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Alumni;

public sealed partial class AlumniService
{
    private const string CareerApplicationFrom = @"
        FROM dbo.CareerApplications a
        JOIN dbo.CareerOpportunities o ON o.TenantId = a.TenantId AND o.Id = a.OpportunityId
        JOIN dbo.CareerEmployers e ON e.TenantId = o.TenantId AND e.Id = o.EmployerId
        JOIN dbo.Students s ON s.TenantId = a.TenantId AND s.Id = a.StudentId
        WHERE a.TenantId = {0}";
    private const string CareerApplicationSelect = @"
        SELECT a.Id, a.PublicId AS Reference, o.PublicId AS OpportunityReference,
          o.Title AS OpportunityTitle, e.CampusId, s.PublicId AS StudentReference,
          s.StudentCode, s.FullName, CAST(a.Status AS int) AS Status,
          CAST(a.ProposedOutcome AS int) AS ProposedOutcome, a.ProposedByUserId,
          a.RowVersion, a.AppliedAtUtc";

    public async Task<ApiResponse<IReadOnlyList<CareerEligibleStudentDto>>> CareerStudentsAsync(CancellationToken ct = default)
    {
        if (!CanRead()) return ApiResponse<IReadOnlyList<CareerEligibleStudentDto>>.ErrorResponse("Institution membership required.", 403);
        try
        {
            var rows = await _db.Database.SqlQueryRaw<CareerEligibleStudentDto>(@"
                SELECT DISTINCT TOP (100) s.PublicId AS StudentReference, en.CampusId,
                    s.StudentCode, s.FullName
                FROM dbo.Students s JOIN dbo.StudentEnrollments en
                  ON en.TenantId = s.TenantId AND en.StudentId = s.Id AND en.IsDeleted = 0
                WHERE s.TenantId = {0} AND s.UserId = {1} AND s.IsDeleted = 0
                  AND (en.State = 1 OR (en.State = 2 AND EXISTS (
                    SELECT 1 FROM dbo.StudentExitRecords x
                    WHERE x.TenantId = s.TenantId AND x.StudentId = s.Id
                      AND x.StudentEnrollmentId = en.Id AND x.ExitType = 2
                      AND x.FeesCleared = 1 AND x.IsDeleted = 0)))
                ORDER BY s.StudentCode, s.FullName, en.CampusId",
                _user.TenantId, _user.UserId).ToListAsync(ct);
            return ApiResponse<IReadOnlyList<CareerEligibleStudentDto>>.SuccessResponse(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Career student lookup failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<IReadOnlyList<CareerEligibleStudentDto>>.ErrorResponse("Student identities could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<CareerApplicationItemDto>> ApplyCareerAsync(
        Guid opportunity, CareerApplyDto request, CancellationToken ct = default)
    {
        if (!CanRead()) return ApiResponse<CareerApplicationItemDto>.ErrorResponse("Institution membership required.", 403);
        if (request == null || opportunity == Guid.Empty || request.StudentReference == Guid.Empty || request.ClientRequestId == Guid.Empty)
            return ApiResponse<CareerApplicationItemDto>.ErrorResponse("Opportunity, student and unique request required.");
        var tenant = _user.TenantId;
        var actor = _user.UserId;
        var hash = CareerFingerprint($"{opportunity:D}|{request.StudentReference:D}");
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var prior = await _db.Database.SqlQueryRaw<CareerApplyReplay>(@"
                    SELECT a.PublicId AS Reference, a.RequestHash, s.UserId AS ActorUserId,
                        o.PublicId AS OpportunityReference, s.PublicId AS StudentReference
                    FROM dbo.CareerApplications a
                    JOIN dbo.Students s ON s.TenantId = a.TenantId AND s.Id = a.StudentId
                    JOIN dbo.CareerOpportunities o ON o.TenantId = a.TenantId AND o.Id = a.OpportunityId
                    WHERE a.TenantId = {0} AND a.ClientRequestId = {1}",
                    tenant, request.ClientRequestId).ToListAsync(token);
                if (prior.Count > 0)
                {
                    if (prior[0].ActorUserId != actor || prior[0].RequestHash != hash ||
                        prior[0].OpportunityReference != opportunity || prior[0].StudentReference != request.StudentReference)
                        return ApiResponse<CareerApplicationItemDto>.ErrorResponse("Request ID already used.", 409);
                    var replay = await CareerApplicationByRef(tenant, actor, prior[0].Reference, false, token);
                    return replay == null ? ApiResponse<CareerApplicationItemDto>.ErrorResponse("Application not found.", 404)
                        : ApiResponse<CareerApplicationItemDto>.SuccessResponse(ToCareerApplication(replay, false), "Already applied.");
                }
                var eligible = await _db.Database.SqlQueryRaw<CareerApplyKey>(@"
                    SELECT o.Id AS OpportunityId, s.Id AS StudentId
                    FROM dbo.CareerOpportunities o
                    JOIN dbo.CareerEmployers e ON e.TenantId = o.TenantId AND e.Id = o.EmployerId
                    JOIN dbo.Students s ON s.TenantId = o.TenantId AND s.UserId = {1}
                      AND s.PublicId = {3} AND s.IsDeleted = 0
                    WHERE o.TenantId = {0} AND o.PublicId = {2} AND o.Status = 2
                      AND o.ApplyUntilUtc > SYSUTCDATETIME() AND e.Status = 2
                      AND EXISTS (SELECT 1 FROM dbo.StudentEnrollments en
                        WHERE en.TenantId = s.TenantId AND en.StudentId = s.Id
                          AND en.CampusId = e.CampusId AND en.IsDeleted = 0
                          AND (en.State = 1 OR (en.State = 2 AND EXISTS (
                            SELECT 1 FROM dbo.StudentExitRecords x
                            WHERE x.TenantId = s.TenantId AND x.StudentId = s.Id
                              AND x.StudentEnrollmentId = en.Id AND x.ExitType = 2
                              AND x.FeesCleared = 1 AND x.IsDeleted = 0))))",
                    tenant, actor, opportunity, request.StudentReference).ToListAsync(token);
                if (eligible.Count != 1)
                    return ApiResponse<CareerApplicationItemDto>.ErrorResponse("Opportunity closed or verified student access denied.", 403);
                var reference = Guid.NewGuid();
                var now = DateTime.UtcNow;
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT dbo.CareerApplications
                    (TenantId, PublicId, OpportunityId, StudentId, ClientRequestId, RequestHash,
                     Status, AppliedAtUtc, UpdatedAtUtc)
                    VALUES ({tenant}, {reference}, {eligible[0].OpportunityId}, {eligible[0].StudentId},
                     {request.ClientRequestId}, {hash}, {(byte)1}, {now}, {now})", token);
                var saved = await CareerApplicationByRef(tenant, actor, reference, false, token);
                if (saved == null) throw new InvalidOperationException("Career application insert was not persisted.");
                return ApiResponse<CareerApplicationItemDto>.SuccessResponse(ToCareerApplication(saved, false), "Application submitted.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205)
        {
            _logger.LogWarning(ex, "Career duplicate application in tenant {TenantId}", tenant);
            return ApiResponse<CareerApplicationItemDto>.ErrorResponse("Already applied or concurrent request.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Career application failed for tenant {TenantId}", tenant);
            return ApiResponse<CareerApplicationItemDto>.ErrorResponse("Application could not be submitted.", 500);
        }
    }

    // Action 1 = propose an outcome, 2 = independent final approval, 3 = applicant withdrawal.
    // Proposed outcomes never change reported placement counts until separately approved.
    public async Task<ApiResponse<CareerApplicationItemDto>> ChangeApplicationAsync(
        Guid application, CareerActionDtoBase request, int action, int outcome, CancellationToken ct = default)
    {
        if (!CanRead()) return ApiResponse<CareerApplicationItemDto>.ErrorResponse("Institution membership required.", 403);
        if (action is < 1 or > 3 || (action != 3 && !CanManageEmployers()))
            return ApiResponse<CareerApplicationItemDto>.ErrorResponse("Permission denied.", 403);
        if (request == null || application == Guid.Empty || request.ClientRequestId == Guid.Empty ||
            !CareerVersion(request.RowVersion, out var version) || (action == 1 && outcome is < 2 or > 3))
            return ApiResponse<CareerApplicationItemDto>.ErrorResponse("Application, request and valid version/outcome required.");
        var tenant = _user.TenantId;
        var actor = _user.UserId;
        var admin = action != 3;
        var hash = CareerFingerprint($"{application:D}|{action}|{outcome}|{request.RowVersion}");
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var current = await CareerApplicationByRef(tenant, actor, application, admin, token);
                if (current == null) return ApiResponse<CareerApplicationItemDto>.ErrorResponse("Application not found.", 404);
                if (action == 2 && (current.ProposedByUserId == null || current.ProposedByUserId == actor))
                    return ApiResponse<CareerApplicationItemDto>.ErrorResponse("Independent second administrator required.", 403);
                var prior = await _db.Database.SqlQueryRaw<CareerApplicationCommandReplay>(@"
                    SELECT ApplicationId AS ResourceId, ActorUserId, RequestHash, CAST(ActionCode AS int) AS ActionCode
                    FROM dbo.CareerApplicationDecisions WHERE TenantId = {0} AND ClientRequestId = {1}",
                    tenant, request.ClientRequestId).ToListAsync(token);
                if (prior.Count > 0)
                {
                    var expected = action == 1 ? current.ProposedOutcome == outcome && current.Status == 1
                        : action == 2 ? current.Status == current.ProposedOutcome && current.Status is 2 or 3
                        : current.Status == 4;
                    if (prior[0].ResourceId != current.Id || prior[0].ActorUserId != actor ||
                        prior[0].ActionCode != action || prior[0].RequestHash != hash || !expected)
                        return ApiResponse<CareerApplicationItemDto>.ErrorResponse("Request reused or superseded.", 409);
                    return ApiResponse<CareerApplicationItemDto>.SuccessResponse(ToCareerApplication(current, admin), "Already processed.");
                }
                if (current.Status != 1 || !CryptographicOperations.FixedTimeEquals(current.RowVersion, version) ||
                    (action == 1 && current.ProposedOutcome != null) ||
                    (action == 2 && current.ProposedOutcome == null))
                    return ApiResponse<CareerApplicationItemDto>.ErrorResponse("Application changed; reload.", 409);
                var now = DateTime.UtcNow;
                var nextStatus = action == 2 ? (byte)current.ProposedOutcome!.Value : action == 3 ? (byte)4 : (byte)1;
                var recordedOutcome = action == 1 ? outcome : action == 2 ? current.ProposedOutcome!.Value : 4;
                var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE dbo.CareerApplications
                    SET ProposedOutcome = CASE WHEN {action} = 1 THEN {(byte)outcome} ELSE ProposedOutcome END,
                        ProposedByUserId = CASE WHEN {action} = 1 THEN {actor} ELSE ProposedByUserId END,
                        ApprovedByUserId = CASE WHEN {action} = 2 THEN {actor} ELSE ApprovedByUserId END,
                        Status = {nextStatus}, UpdatedAtUtc = {now}
                    WHERE TenantId = {tenant} AND Id = {current.Id} AND Status = {(byte)1}
                      AND RowVersion = {version}", token);
                if (affected != 1) return ApiResponse<CareerApplicationItemDto>.ErrorResponse("Application changed; reload.", 409);
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT dbo.CareerApplicationDecisions
                    (TenantId, ApplicationId, ClientRequestId, RequestHash, ActionCode, Outcome, ActorUserId, OccurredAtUtc)
                    VALUES ({tenant}, {current.Id}, {request.ClientRequestId}, {hash},
                     {(byte)action}, {(byte)recordedOutcome}, {actor}, {now})", token);
                var saved = await CareerApplicationByRef(tenant, actor, application, admin, token);
                return ApiResponse<CareerApplicationItemDto>.SuccessResponse(ToCareerApplication(saved!, admin), "Application updated.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205)
        {
            _logger.LogWarning(ex, "Career application transition conflict for tenant {TenantId}", tenant);
            return ApiResponse<CareerApplicationItemDto>.ErrorResponse("Concurrent decision; reload.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Career application transition failed for tenant {TenantId}", tenant);
            return ApiResponse<CareerApplicationItemDto>.ErrorResponse("Application could not be updated.", 500);
        }
    }

    public async Task<ApiResponse<PagedResult<CareerApplicationItemDto>>> CareerApplicationsAsync(
        CareerApplicationQueryDto query, CancellationToken ct = default)
    {
        if (!CanRead()) return ApiResponse<PagedResult<CareerApplicationItemDto>>.ErrorResponse("Institution membership required.", 403);
        if (query == null || query.Page < 1 || query.PageSize is < 1 or > 50 || query.Status is < 0 or > 4)
            return ApiResponse<PagedResult<CareerApplicationItemDto>>.ErrorResponse("Invalid filter.");
        var admin = _user.IsTenantAdmin;
        var scope = admin ? " AND " + CareerAdminGrant : " AND s.UserId = {1} AND s.IsDeleted = 0";
        var from = CareerApplicationFrom + scope + " AND ({2} = '00000000-0000-0000-0000-000000000000' OR o.PublicId = {2})" +
            " AND ({3} = 0 OR a.Status = {3})";
        try
        {
            var reference = query.OpportunityReference ?? Guid.Empty;
            var count = await _db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value " + from,
                _user.TenantId, _user.UserId, reference, query.Status).ToListAsync(ct);
            var offset = (int)Math.Min((long)(query.Page - 1) * query.PageSize, int.MaxValue);
            var rows = await _db.Database.SqlQueryRaw<CareerApplicationRow>(CareerApplicationSelect + " " + from +
                " ORDER BY a.AppliedAtUtc DESC, a.Id DESC OFFSET {4} ROWS FETCH NEXT {5} ROWS ONLY",
                _user.TenantId, _user.UserId, reference, query.Status, offset, query.PageSize).ToListAsync(ct);
            return ApiResponse<PagedResult<CareerApplicationItemDto>>.SuccessResponse(new PagedResult<CareerApplicationItemDto>
            {
                Page = query.Page, PageSize = query.PageSize, TotalCount = count.Single(),
                Items = rows.Select(r => ToCareerApplication(r, admin)).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Career applications read failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<PagedResult<CareerApplicationItemDto>>.ErrorResponse("Applications could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<PagedResult<CareerPlacementReportItemDto>>> CareerReportAsync(
        CareerOpportunityQueryDto query, CancellationToken ct = default)
    {
        if (!CanManageEmployers())
            return ApiResponse<PagedResult<CareerPlacementReportItemDto>>.ErrorResponse("Career reporting permission required.", 403);
        if (query == null || query.Page < 1 || query.PageSize is < 1 or > 50 || query.Status is < 0 or > 3)
            return ApiResponse<PagedResult<CareerPlacementReportItemDto>>.ErrorResponse("Invalid report filter.");
        var from = CareerOpportunityFrom + " AND " + CareerAdminGrant + " AND ({2} = 0 OR o.Status = {2})";
        try
        {
            var count = await _db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value " + from,
                _user.TenantId, _user.UserId, query.Status).ToListAsync(ct);
            var offset = (int)Math.Min((long)(query.Page - 1) * query.PageSize, int.MaxValue);
            var rows = await _db.Database.SqlQueryRaw<CareerPlacementReportItemDto>(@"
                SELECT o.PublicId AS OpportunityReference, e.CampusId, e.Name AS EmployerName,
                    o.Title AS OpportunityTitle,
                    COUNT(a.Id) AS Applications,
                    COUNT(CASE WHEN a.Status = 1 THEN 1 END) AS Pending,
                    COUNT(CASE WHEN a.Status = 2 THEN 1 END) AS Rejected,
                    COUNT(CASE WHEN a.Status = 3 THEN 1 END) AS Placed,
                    COUNT(CASE WHEN a.Status = 4 THEN 1 END) AS Withdrawn
                FROM dbo.CareerOpportunities o
                JOIN dbo.CareerEmployers e ON e.TenantId = o.TenantId AND e.Id = o.EmployerId
                LEFT JOIN dbo.CareerApplications a ON a.TenantId = o.TenantId AND a.OpportunityId = o.Id
                WHERE o.TenantId = {0} AND " + CareerAdminGrant + @"
                  AND ({2} = 0 OR o.Status = {2})
                GROUP BY o.Id, o.PublicId, e.CampusId, e.Name, o.Title, o.CreatedAtUtc
                ORDER BY o.CreatedAtUtc DESC, o.Id DESC
                OFFSET {3} ROWS FETCH NEXT {4} ROWS ONLY",
                _user.TenantId, _user.UserId, query.Status, offset, query.PageSize).ToListAsync(ct);
            return ApiResponse<PagedResult<CareerPlacementReportItemDto>>.SuccessResponse(new PagedResult<CareerPlacementReportItemDto>
            {
                Page = query.Page, PageSize = query.PageSize, TotalCount = count.Single(), Items = rows
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Career placement report failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<PagedResult<CareerPlacementReportItemDto>>.ErrorResponse("Report could not be loaded.", 500);
        }
    }

    private async Task<CareerApplicationRow?> CareerApplicationByRef(
        long tenant, long actor, Guid reference, bool admin, CancellationToken ct)
    {
        var grant = admin ? CareerAdminGrant : "s.UserId = {1} AND s.IsDeleted = 0";
        var rows = await _db.Database.SqlQueryRaw<CareerApplicationRow>(CareerApplicationSelect + " " +
            CareerApplicationFrom + " AND a.PublicId = {2} AND " + grant,
            tenant, actor, reference).ToListAsync(ct);
        return rows.FirstOrDefault();
    }
    private CareerApplicationItemDto ToCareerApplication(CareerApplicationRow row, bool admin) => new()
    {
        Reference = row.Reference, OpportunityReference = row.OpportunityReference,
        OpportunityTitle = row.OpportunityTitle, CampusId = row.CampusId,
        StudentReference = row.StudentReference, StudentCode = row.StudentCode, FullName = row.FullName,
        Status = row.Status, ProposedOutcome = row.ProposedOutcome,
        CanApprove = admin && row.Status == 1 && row.ProposedOutcome != null && row.ProposedByUserId != _user.UserId,
        RowVersion = Convert.ToBase64String(row.RowVersion),
        AppliedAtUtc = DateTime.SpecifyKind(row.AppliedAtUtc, DateTimeKind.Utc)
    };
    private sealed class CareerApplyReplay
    {
        public Guid Reference { get; set; }
        public string RequestHash { get; set; } = "";
        public long ActorUserId { get; set; }
        public Guid OpportunityReference { get; set; }
        public Guid StudentReference { get; set; }
    }
    private sealed class CareerApplyKey
    {
        public long OpportunityId { get; set; }
        public long StudentId { get; set; }
    }
    private sealed class CareerApplicationCommandReplay
    {
        public long ResourceId { get; set; }
        public long ActorUserId { get; set; }
        public string RequestHash { get; set; } = "";
        public int ActionCode { get; set; }
    }
    private sealed class CareerApplicationRow
    {
        public long Id { get; set; }
        public Guid Reference { get; set; }
        public Guid OpportunityReference { get; set; }
        public string OpportunityTitle { get; set; } = "";
        public long CampusId { get; set; }
        public Guid StudentReference { get; set; }
        public string StudentCode { get; set; } = "";
        public string FullName { get; set; } = "";
        public int Status { get; set; }
        public int? ProposedOutcome { get; set; }
        public long? ProposedByUserId { get; set; }
        public byte[] RowVersion { get; set; } = [];
        public DateTime AppliedAtUtc { get; set; }
    }
}
