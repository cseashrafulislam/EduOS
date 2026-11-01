using System.Data;
using System.Security.Cryptography;
using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Alumni;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Alumni;

public sealed partial class AlumniService
{
    private const string CareerStudentEligibility = @"
        EXISTS (SELECT 1 FROM dbo.Students s
          JOIN dbo.StudentEnrollments en ON en.TenantId = s.TenantId
            AND en.StudentId = s.Id AND en.IsDeleted = 0
          WHERE s.TenantId = o.TenantId AND s.UserId = {1} AND s.IsDeleted = 0
            AND en.CampusId = e.CampusId
            AND (en.State = 1 OR (en.State = 2 AND EXISTS (
                SELECT 1 FROM dbo.StudentExitRecords x
                WHERE x.TenantId = s.TenantId AND x.StudentId = s.Id
                  AND x.StudentEnrollmentId = en.Id AND x.ExitType = 2
                  AND x.FeesCleared = 1 AND x.IsDeleted = 0))))";
    private const string CareerAdminGrant = @"
        EXISTS (SELECT 1 FROM dbo.UserCampusAccesses ca
          WHERE ca.TenantId = o.TenantId AND ca.CampusId = e.CampusId
            AND ca.UserId = {1} AND ca.IsActive = 1 AND ca.IsDeleted = 0)";
    private const string CareerOpportunityFrom = @"
        FROM dbo.CareerOpportunities o
        JOIN dbo.CareerEmployers e ON e.TenantId = o.TenantId AND e.Id = o.EmployerId
        WHERE o.TenantId = {0}";
    private const string CareerOpportunitySelect = @"
        SELECT o.Id, o.PublicId AS Reference, e.PublicId AS EmployerReference,
          e.CampusId, e.Name AS EmployerName, o.Title, o.Description,
          CAST(o.Kind AS int) AS Kind, CAST(o.Status AS int) AS Status,
          o.ApplyUntilUtc, o.CreatedByUserId, o.RowVersion";
    private static string CareerFingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public async Task<ApiResponse<CareerOpportunityItemDto>> CreateOpportunityAsync(
        CareerOpportunityDraftDto request, CancellationToken ct = default)
    {
        if (!CanManageEmployers()) return ApiResponse<CareerOpportunityItemDto>.ErrorResponse("Career administration required.", 403);
        var title = request?.Title?.Trim() ?? "";
        var details = string.IsNullOrWhiteSpace(request?.Description) ? null : request.Description.Trim();
        var deadline = request?.ApplyUntilUtc ?? default;
        if (request == null || request.ClientRequestId == Guid.Empty || request.EmployerReference == Guid.Empty ||
            title.Length is < 3 or > 160 || details?.Length > 2000 || request.Kind is < 1 or > 2 ||
            deadline.Kind != DateTimeKind.Utc)
            return ApiResponse<CareerOpportunityItemDto>.ErrorResponse("Valid employer, title, kind and future UTC deadline required.");
        var tenant = _user.TenantId;
        var actor = _user.UserId;
        var hash = CareerFingerprint($"{request.EmployerReference:D}|{title}|{details}|{request.Kind}|{deadline:O}");
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var existing = await _db.Database.SqlQueryRaw<CareerOpportunityReplay>(@"
                    SELECT o.PublicId AS Reference, o.CreatedByUserId, o.RequestHash
                    FROM dbo.CareerOpportunities o WHERE o.TenantId = {0} AND o.CreateRequestId = {1}",
                    tenant, request.ClientRequestId).ToListAsync(token);
                if (existing.Count > 0)
                {
                    if (existing[0].CreatedByUserId != actor || existing[0].RequestHash != hash)
                        return ApiResponse<CareerOpportunityItemDto>.ErrorResponse("Request ID already used.", 409);
                    var replay = await CareerOpportunityByRef(tenant, actor, existing[0].Reference, token);
                    return replay == null ? ApiResponse<CareerOpportunityItemDto>.ErrorResponse("Opportunity access denied.", 403)
                        : ApiResponse<CareerOpportunityItemDto>.SuccessResponse(ToCareerOpportunity(replay, true), "Existing draft returned.");
                }
                if (deadline <= DateTime.UtcNow.AddMinutes(5) || deadline > DateTime.UtcNow.AddYears(2))
                    return ApiResponse<CareerOpportunityItemDto>.ErrorResponse("Deadline must be 5 minutes to 2 years in the future.");
                var employers = await _db.Database.SqlQueryRaw<CareerEmployerKey>(@"
                    SELECT e.Id, e.CampusId FROM dbo.CareerEmployers e
                    WHERE e.TenantId = {0} AND e.PublicId = {2} AND e.Status = 2 AND " + EmployerCampusScope,
                    tenant, actor, request.EmployerReference).ToListAsync(token);
                if (employers.Count != 1)
                    return ApiResponse<CareerOpportunityItemDto>.ErrorResponse("Verified employer or campus permission not found.", 403);
                var reference = Guid.NewGuid();
                var now = DateTime.UtcNow;
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT dbo.CareerOpportunities
                    (TenantId, PublicId, EmployerId, CreateRequestId, RequestHash, Title,
                     Description, Kind, ApplyUntilUtc, Status, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc)
                    VALUES ({tenant}, {reference}, {employers[0].Id}, {request.ClientRequestId}, {hash},
                     {title}, {details}, {(byte)request.Kind}, {deadline}, {(byte)1}, {actor}, {now}, {now})", token);
                var saved = await CareerOpportunityByRef(tenant, actor, reference, token);
                if (saved == null) throw new InvalidOperationException("Career opportunity insert was not persisted.");
                return ApiResponse<CareerOpportunityItemDto>.SuccessResponse(ToCareerOpportunity(saved, true),
                    "Draft saved. Another administrator must publish it.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205)
        {
            _logger.LogWarning(ex, "Career opportunity create conflict for tenant {TenantId}", tenant);
            return ApiResponse<CareerOpportunityItemDto>.ErrorResponse("Duplicate or concurrent request.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Career opportunity create failed for tenant {TenantId}", tenant);
            return ApiResponse<CareerOpportunityItemDto>.ErrorResponse("Opportunity could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<CareerOpportunityItemDto>> ChangeOpportunityAsync(
        Guid reference, CareerActionDto request, int action, CancellationToken ct = default)
    {
        if (!CanManageEmployers()) return ApiResponse<CareerOpportunityItemDto>.ErrorResponse("Career administration required.", 403);
        if (request == null || reference == Guid.Empty || action is < 2 or > 3 || request.ClientRequestId == Guid.Empty ||
            !CareerVersion(request.RowVersion, out var version))
            return ApiResponse<CareerOpportunityItemDto>.ErrorResponse("Opportunity, request and valid version required.");
        var tenant = _user.TenantId;
        var actor = _user.UserId;
        var hash = CareerFingerprint($"{reference:D}|{action}|{request.RowVersion}");
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var current = await CareerOpportunityByRef(tenant, actor, reference, token);
                if (current == null) return ApiResponse<CareerOpportunityItemDto>.ErrorResponse("Opportunity not found.", 404);
                if (action == 2 && current.CreatedByUserId == actor)
                    return ApiResponse<CareerOpportunityItemDto>.ErrorResponse("A different administrator must publish.", 403);
                var prior = await _db.Database.SqlQueryRaw<CareerCommandReplay>(@"
                    SELECT OpportunityId AS ResourceId, ActorUserId, RequestHash, CAST(ActionCode AS int) AS ActionCode
                    FROM dbo.CareerOpportunityDecisions WHERE TenantId = {0} AND ClientRequestId = {1}",
                    tenant, request.ClientRequestId).ToListAsync(token);
                if (prior.Count > 0)
                {
                    if (prior[0].ResourceId != current.Id || prior[0].ActorUserId != actor ||
                        prior[0].RequestHash != hash || prior[0].ActionCode != action || current.Status != action)
                        return ApiResponse<CareerOpportunityItemDto>.ErrorResponse("Request ID reused or superseded.", 409);
                    return ApiResponse<CareerOpportunityItemDto>.SuccessResponse(ToCareerOpportunity(current, true), "Already processed.");
                }
                if (current.Status != action - 1 || !CryptographicOperations.FixedTimeEquals(current.RowVersion, version) ||
                    (action == 2 && current.ApplyUntilUtc <= DateTime.UtcNow))
                    return ApiResponse<CareerOpportunityItemDto>.ErrorResponse("State, deadline or version changed; reload.", 409);
                var now = DateTime.UtcNow;
                var count = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE dbo.CareerOpportunities SET Status = {(byte)action},
                      ApprovedByUserId = CASE WHEN {action} = 2 THEN {actor} ELSE ApprovedByUserId END,
                      UpdatedAtUtc = {now}
                    WHERE TenantId = {tenant} AND Id = {current.Id} AND Status = {(byte)(action - 1)}
                      AND RowVersion = {version}", token);
                if (count != 1) return ApiResponse<CareerOpportunityItemDto>.ErrorResponse("Opportunity changed; reload.", 409);
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT dbo.CareerOpportunityDecisions
                    (TenantId, OpportunityId, ClientRequestId, RequestHash, ActionCode, ActorUserId, OccurredAtUtc)
                    VALUES ({tenant}, {current.Id}, {request.ClientRequestId}, {hash}, {(byte)action}, {actor}, {now})", token);
                var saved = await CareerOpportunityByRef(tenant, actor, reference, token);
                return ApiResponse<CareerOpportunityItemDto>.SuccessResponse(ToCareerOpportunity(saved!, true), "Opportunity updated.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205)
        {
            _logger.LogWarning(ex, "Career opportunity transition conflict for tenant {TenantId}", tenant);
            return ApiResponse<CareerOpportunityItemDto>.ErrorResponse("Concurrent operation; reload.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Career opportunity transition failed for tenant {TenantId}", tenant);
            return ApiResponse<CareerOpportunityItemDto>.ErrorResponse("Opportunity could not be updated.", 500);
        }
    }

    public async Task<ApiResponse<PagedResult<CareerOpportunityItemDto>>> OpportunitiesAsync(
        CareerOpportunityQueryDto query, CancellationToken ct = default)
    {
        if (!CanRead()) return ApiResponse<PagedResult<CareerOpportunityItemDto>>.ErrorResponse("Institution membership required.", 403);
        if (query == null || query.Page < 1 || query.PageSize is < 1 or > 50 || query.Status is < 0 or > 3)
            return ApiResponse<PagedResult<CareerOpportunityItemDto>>.ErrorResponse("Invalid page or status.");
        var admin = _user.IsTenantAdmin;
        var scope = admin ? " AND " + CareerAdminGrant + " AND ({2} = 0 OR o.Status = {2})"
            : " AND o.Status = 2 AND o.ApplyUntilUtc > SYSUTCDATETIME() AND " + CareerStudentEligibility;
        try
        {
            var from = CareerOpportunityFrom + scope;
            var count = await _db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value " + from,
                _user.TenantId, _user.UserId, query.Status).ToListAsync(ct);
            var offset = (int)Math.Min((long)(query.Page - 1) * query.PageSize, int.MaxValue);
            var rows = await _db.Database.SqlQueryRaw<CareerOpportunityRow>(CareerOpportunitySelect + " " + from +
                " ORDER BY o.CreatedAtUtc DESC, o.Id DESC OFFSET {3} ROWS FETCH NEXT {4} ROWS ONLY",
                _user.TenantId, _user.UserId, query.Status, offset, query.PageSize).ToListAsync(ct);
            return ApiResponse<PagedResult<CareerOpportunityItemDto>>.SuccessResponse(new PagedResult<CareerOpportunityItemDto>
            {
                Page = query.Page, PageSize = query.PageSize, TotalCount = count.Single(),
                Items = rows.Select(r => ToCareerOpportunity(r, admin)).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Career opportunities query failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<PagedResult<CareerOpportunityItemDto>>.ErrorResponse("Opportunities could not be loaded.", 500);
        }
    }

    private async Task<CareerOpportunityRow?> CareerOpportunityByRef(long tenant, long actor, Guid reference, CancellationToken ct)
    {
        var rows = await _db.Database.SqlQueryRaw<CareerOpportunityRow>(CareerOpportunitySelect + " " +
            CareerOpportunityFrom + " AND o.PublicId = {2} AND " + CareerAdminGrant,
            tenant, actor, reference).ToListAsync(ct);
        return rows.FirstOrDefault();
    }
    private CareerOpportunityItemDto ToCareerOpportunity(CareerOpportunityRow row, bool admin) => new()
    {
        Reference = row.Reference, EmployerReference = row.EmployerReference, CampusId = row.CampusId,
        EmployerName = row.EmployerName, Title = row.Title, Description = row.Description,
        Kind = row.Kind, Status = row.Status, ApplyUntilUtc = DateTime.SpecifyKind(row.ApplyUntilUtc, DateTimeKind.Utc),
        CreatedByUserId = row.CreatedByUserId, RowVersion = Convert.ToBase64String(row.RowVersion),
        CanPublish = admin && row.Status == 1 && row.CreatedByUserId != _user.UserId,
        CanApply = !admin && row.Status == 2 && row.ApplyUntilUtc > DateTime.UtcNow
    };
    private static bool CareerVersion(string? raw, out byte[] version)
    {
        try { version = Convert.FromBase64String(raw ?? ""); }
        catch (FormatException) { version = []; }
        return version.Length == 8;
    }
    private sealed class CareerEmployerKey
    {
        public long Id { get; set; }
        public long CampusId { get; set; }
    }
    private sealed class CareerOpportunityReplay
    {
        public Guid Reference { get; set; }
        public long CreatedByUserId { get; set; }
        public string RequestHash { get; set; } = "";
    }
    private sealed class CareerCommandReplay
    {
        public long ResourceId { get; set; }
        public long ActorUserId { get; set; }
        public string RequestHash { get; set; } = "";
        public int ActionCode { get; set; }
    }
    private sealed class CareerOpportunityRow
    {
        public long Id { get; set; }
        public Guid Reference { get; set; }
        public Guid EmployerReference { get; set; }
        public long CampusId { get; set; }
        public string EmployerName { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public int Kind { get; set; }
        public int Status { get; set; }
        public DateTime ApplyUntilUtc { get; set; }
        public long CreatedByUserId { get; set; }
        public byte[] RowVersion { get; set; } = [];
    }
}
