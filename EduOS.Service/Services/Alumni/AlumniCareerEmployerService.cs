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
    // Match existing Alumni campus authorization until the separate explicit-grant
    // acceptance decision is made. No client-supplied tenant or campus grant is trusted.
    private const string EmployerCampusScope = @"
        (NOT EXISTS (SELECT 1 FROM dbo.UserCampusAccesses ca
             WHERE ca.TenantId = {0} AND ca.UserId = {1})
         OR EXISTS (SELECT 1 FROM dbo.UserCampusAccesses ca
             WHERE ca.TenantId = {0} AND ca.UserId = {1}
               AND ca.CampusId = e.CampusId AND ca.IsActive = 1 AND ca.IsDeleted = 0))";
    private const string EmployerColumns = @"
        SELECT e.Id, e.PublicId AS Reference, e.CampusId, e.Name, e.Website,
               CAST(e.Status AS int) AS Status, e.RowVersion,
               e.CreatedByUserId, e.CreatedAtUtc
        FROM dbo.CareerEmployers e WHERE e.TenantId = {0}";

    private bool CanManageEmployers() => CanRead() && _user.IsTenantAdmin;
    private static string EmployerHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public async Task<ApiResponse<CareerEmployerItemDto>> CreateEmployerAsync(
        CareerEmployerDraftDto request, CancellationToken ct = default)
    {
        if (!CanManageEmployers())
            return ApiResponse<CareerEmployerItemDto>.ErrorResponse("Career administration required.", 403);
        var name = request?.Name?.Trim() ?? "";
        var website = string.IsNullOrWhiteSpace(request?.Website) ? null : request.Website.Trim();
        if (request == null || request.ClientRequestId == Guid.Empty || request.CampusId <= 0 ||
            name.Length is < 3 or > 160 || website?.Length > 300 ||
            (website != null && (!Uri.TryCreate(website, UriKind.Absolute, out var uri) ||
                                  uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0)))
            return ApiResponse<CareerEmployerItemDto>.ErrorResponse("Valid employer, campus and HTTPS website required.");
        var tenant = _user.TenantId;
        var actor = _user.UserId;
        var hash = EmployerHash($"{request.CampusId}|{name.ToUpperInvariant()}|{website}");
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var existing = await _db.Database.SqlQueryRaw<CareerEmployerRow>(
                    EmployerColumns + " AND e.CreateRequestId = {2} AND " + EmployerCampusScope,
                    tenant, actor, request.ClientRequestId).ToListAsync(token);
                if (existing.Count > 0)
                {
                    var savedHash = await _db.Database.SqlQueryRaw<string>(
                        "SELECT e.RequestHash AS Value FROM dbo.CareerEmployers e WHERE e.TenantId = {0} AND e.Id = {1}",
                        tenant, existing[0].Id).ToListAsync(token);
                    if (existing[0].CreatedByUserId != actor || savedHash.Single() != hash)
                        return ApiResponse<CareerEmployerItemDto>.ErrorResponse("Request ID already used.", 409);
                    return ApiResponse<CareerEmployerItemDto>.SuccessResponse(EmployerDto(existing[0]), "Existing draft returned.");
                }
                var permitted = await _db.Database.SqlQueryRaw<int>(@"
                    SELECT COUNT(*) AS Value FROM dbo.Campuses c
                    WHERE c.TenantId = {0} AND c.Id = {2} AND c.IsActive = 1 AND c.IsDeleted = 0
                      AND (NOT EXISTS (SELECT 1 FROM dbo.UserCampusAccesses ca
                            WHERE ca.TenantId = {0} AND ca.UserId = {1})
                           OR EXISTS (SELECT 1 FROM dbo.UserCampusAccesses ca
                            WHERE ca.TenantId = {0} AND ca.UserId = {1} AND ca.CampusId = c.Id
                              AND ca.IsActive = 1 AND ca.IsDeleted = 0))",
                    tenant, actor, request.CampusId).ToListAsync(token);
                if (permitted.Single() != 1)
                    return ApiResponse<CareerEmployerItemDto>.ErrorResponse("Campus access denied.", 403);
                var reference = Guid.NewGuid();
                var now = DateTime.UtcNow;
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO dbo.CareerEmployers
                    (TenantId, PublicId, CampusId, CreateRequestId, RequestHash, Name,
                     NormalizedName, Website, Status, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc)
                    VALUES ({tenant}, {reference}, {request.CampusId}, {request.ClientRequestId},
                            {hash}, {name}, {name.ToUpperInvariant()}, {website}, {(byte)1},
                            {actor}, {now}, {now})", token);
                var created = await EmployerByReference(tenant, actor, reference, token);
                if (created == null) throw new InvalidOperationException("Employer draft was not persisted.");
                return ApiResponse<CareerEmployerItemDto>.SuccessResponse(
                    EmployerDto(created), "Employer draft saved; a different administrator must verify.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205)
        {
            _logger.LogWarning(ex, "Career employer create conflict for tenant {TenantId}", tenant);
            return ApiResponse<CareerEmployerItemDto>.ErrorResponse("Duplicate employer or concurrent request.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Career employer creation failed for tenant {TenantId}", tenant);
            return ApiResponse<CareerEmployerItemDto>.ErrorResponse("Employer could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<CareerEmployerItemDto>> VerifyEmployerAsync(
        Guid reference, CareerEmployerVerifyDto request, CancellationToken ct = default)
    {
        if (!CanManageEmployers())
            return ApiResponse<CareerEmployerItemDto>.ErrorResponse("Career administration required.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RowVersion))
            return ApiResponse<CareerEmployerItemDto>.ErrorResponse("Employer, request and version required.");
        byte[] version;
        try { version = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { return ApiResponse<CareerEmployerItemDto>.ErrorResponse("Invalid row version."); }
        if (version.Length != 8)
            return ApiResponse<CareerEmployerItemDto>.ErrorResponse("Invalid row version.");
        var tenant = _user.TenantId;
        var actor = _user.UserId;
        var hash = EmployerHash($"{reference:D}|{request.RowVersion}");
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var current = await EmployerByReference(tenant, actor, reference, token);
                if (current == null)
                    return ApiResponse<CareerEmployerItemDto>.ErrorResponse("Employer not found.", 404);
                if (current.CreatedByUserId == actor)
                    return ApiResponse<CareerEmployerItemDto>.ErrorResponse("A different administrator must verify.", 403);
                var prior = await _db.Database.SqlQueryRaw<CareerEmployerDecisionRow>(@"
                    SELECT EmployerId, ActorUserId, RequestHash
                    FROM dbo.CareerEmployerDecisions WHERE TenantId = {0} AND ClientRequestId = {1}",
                    tenant, request.ClientRequestId).ToListAsync(token);
                if (prior.Count > 0)
                {
                    if (prior[0].EmployerId != current.Id || prior[0].ActorUserId != actor ||
                        prior[0].RequestHash != hash || current.Status != 2)
                        return ApiResponse<CareerEmployerItemDto>.ErrorResponse("Request ID already used or superseded.", 409);
                    return ApiResponse<CareerEmployerItemDto>.SuccessResponse(EmployerDto(current), "Employer already verified.");
                }
                if (current.Status != 1 ||
                    !CryptographicOperations.FixedTimeEquals(current.RowVersion, version))
                    return ApiResponse<CareerEmployerItemDto>.ErrorResponse("Employer changed; reload.", 409);
                var now = DateTime.UtcNow;
                var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE dbo.CareerEmployers SET Status = {(byte)2},
                        VerifiedByUserId = {actor}, UpdatedAtUtc = {now}
                    WHERE TenantId = {tenant} AND Id = {current.Id}
                      AND Status = {(byte)1} AND RowVersion = {version}", token);
                if (affected != 1)
                    return ApiResponse<CareerEmployerItemDto>.ErrorResponse("Employer changed; reload.", 409);
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO dbo.CareerEmployerDecisions
                    (TenantId, EmployerId, ClientRequestId, RequestHash, ActorUserId, OccurredAtUtc)
                    VALUES ({tenant}, {current.Id}, {request.ClientRequestId}, {hash}, {actor}, {now})", token);
                var verified = await EmployerByReference(tenant, actor, reference, token);
                return ApiResponse<CareerEmployerItemDto>.SuccessResponse(EmployerDto(verified!), "Employer verified.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205)
        {
            _logger.LogWarning(ex, "Career employer verify conflict for tenant {TenantId}", tenant);
            return ApiResponse<CareerEmployerItemDto>.ErrorResponse("Concurrent verification; reload.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Career employer verification failed for tenant {TenantId}", tenant);
            return ApiResponse<CareerEmployerItemDto>.ErrorResponse("Employer could not be verified.", 500);
        }
    }

    public async Task<ApiResponse<PagedResult<CareerEmployerItemDto>>> EmployersAsync(
        CareerEmployerQueryDto query, CancellationToken ct = default)
    {
        if (!CanManageEmployers())
            return ApiResponse<PagedResult<CareerEmployerItemDto>>.ErrorResponse("Career administration required.", 403);
        if (query == null || query.Page < 1 || query.PageSize is < 1 or > 50 || query.Status is < 0 or > 2)
            return ApiResponse<PagedResult<CareerEmployerItemDto>>.ErrorResponse("Invalid page or status.");
        var tenant = _user.TenantId;
        var actor = _user.UserId;
        try
        {
            var from = " FROM dbo.CareerEmployers e WHERE e.TenantId = {0} AND "
                + EmployerCampusScope + " AND ({2} = 0 OR e.Status = {2})";
            var count = await _db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value" + from, tenant, actor, query.Status).ToListAsync(ct);
            var offset = (int)Math.Min((long)(query.Page - 1) * query.PageSize, int.MaxValue);
            var rows = await _db.Database.SqlQueryRaw<CareerEmployerRow>(
                @"SELECT e.Id, e.PublicId AS Reference, e.CampusId, e.Name, e.Website,
                         CAST(e.Status AS int) AS Status, e.RowVersion, e.CreatedByUserId, e.CreatedAtUtc"
                + from + " ORDER BY e.CreatedAtUtc DESC, e.Id DESC OFFSET {3} ROWS FETCH NEXT {4} ROWS ONLY",
                tenant, actor, query.Status, offset, query.PageSize).ToListAsync(ct);
            return ApiResponse<PagedResult<CareerEmployerItemDto>>.SuccessResponse(
                new PagedResult<CareerEmployerItemDto>
                {
                    Page = query.Page, PageSize = query.PageSize, TotalCount = count.Single(),
                    Items = rows.Select(EmployerDto).ToList()
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Career employer list failed for tenant {TenantId}", tenant);
            return ApiResponse<PagedResult<CareerEmployerItemDto>>.ErrorResponse("Employers could not be loaded.", 500);
        }
    }

    private async Task<CareerEmployerRow?> EmployerByReference(
        long tenant, long actor, Guid reference, CancellationToken ct)
    {
        var rows = await _db.Database.SqlQueryRaw<CareerEmployerRow>(
            EmployerColumns + " AND e.PublicId = {2} AND " + EmployerCampusScope,
            tenant, actor, reference).ToListAsync(ct);
        return rows.FirstOrDefault();
    }
    private CareerEmployerItemDto EmployerDto(CareerEmployerRow row) => new()
    {
        Reference = row.Reference, CampusId = row.CampusId, Name = row.Name,
        Website = row.Website, Status = row.Status, CreatedByUserId = row.CreatedByUserId,
        CanVerify = row.Status == 1 && row.CreatedByUserId != _user.UserId,
        CreatedAtUtc = DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc),
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };
    private sealed class CareerEmployerRow
    {
        public long Id { get; set; }
        public Guid Reference { get; set; }
        public long CampusId { get; set; }
        public string Name { get; set; } = "";
        public string? Website { get; set; }
        public int Status { get; set; }
        public byte[] RowVersion { get; set; } = [];
        public long CreatedByUserId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
    private sealed class CareerEmployerDecisionRow
    {
        public long EmployerId { get; set; }
        public long ActorUserId { get; set; }
        public string RequestHash { get; set; } = "";
    }
}
