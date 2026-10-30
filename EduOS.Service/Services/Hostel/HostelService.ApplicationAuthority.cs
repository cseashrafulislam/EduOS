using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    // Explicit application authority is independent of pickup and financial-contact permissions.
    // Each grant has evidence, an expiry, a grant actor and optional immutable revocation markers.
    private IQueryable<long> AuthorizedApplicantStudentIds(long tenant)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var own = _students.GetQueryable().AsNoTracking().Where(s => s.TenantId == tenant &&
            s.UserId == _currentUser.UserId).Select(s => s.Id);
        var delegated = from authority in _applicationAuthorities.GetQueryable().AsNoTracking()
            join link in _studentGuardians.GetQueryable().AsNoTracking()
                on authority.StudentGuardianId equals link.Id
            join guardian in _guardians.GetQueryable().AsNoTracking()
                on link.GuardianId equals guardian.Id
            where authority.TenantId == tenant && link.TenantId == tenant && guardian.TenantId == tenant &&
                authority.StudentId == link.StudentId && authority.GuardianId == link.GuardianId &&
                authority.RevokedAtUtc == null && authority.ExpiresAtUtc > now &&
                guardian.IsActive && guardian.UserId == _currentUser.UserId
            select authority.StudentId;
        return own.Union(delegated);
    }

    private Task<bool> CanApplyForStudentAsync(long studentId, long tenant, CancellationToken ct) =>
        AuthorizedApplicantStudentIds(tenant).AnyAsync(id => id == studentId, ct);

    private bool CanManageHostelAuthority() => CanRead() &&
        (_currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal"));

    private static ApiResponse<HostelApplicationAuthorityDto> AuthorityError(string message, int status = 400) =>
        ApiResponse<HostelApplicationAuthorityDto>.ErrorResponse(message, status);

    public async Task<ApiResponse<PagedResult<HostelAuthorityGuardianOptionDto>>> GetAuthorityGuardianOptionsAsync(
        Guid enrollmentReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManageHostelAuthority()) return Denied<PagedResult<HostelAuthorityGuardianOptionDto>>();
        if (enrollmentReference == Guid.Empty || page < 1 || pageSize is < 1 or > 50 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<HostelAuthorityGuardianOptionDto>>.ErrorResponse("Invalid guardian lookup.");
        var tenant = _currentUser.TenantId;
        var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(e =>
            e.TenantId == tenant && e.PublicId == enrollmentReference && e.IsCurrent &&
            e.State == EnrollmentState.Active, ct);
        if (enrollment == null) return ApiResponse<PagedResult<HostelAuthorityGuardianOptionDto>>.ErrorResponse("Enrollment not found.", 404);
        if (!await CanManageCampusAsync(enrollment.CampusId, tenant, ct))
            return Denied<PagedResult<HostelAuthorityGuardianOptionDto>>();
        var query = from link in _studentGuardians.GetQueryable().AsNoTracking()
            join guardian in _guardians.GetQueryable().AsNoTracking() on link.GuardianId equals guardian.Id
            where link.TenantId == tenant && guardian.TenantId == tenant && link.StudentId == enrollment.StudentId &&
                guardian.IsActive && guardian.UserId.HasValue
            select new HostelAuthorityGuardianOptionDto
            {
                GuardianReference = guardian.PublicId, GuardianName = guardian.FullName,
                RelationCode = link.RelationCode
            };
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.GuardianName).ThenBy(x => x.GuardianReference)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<HostelAuthorityGuardianOptionDto>>.SuccessResponse(new PagedResult<HostelAuthorityGuardianOptionDto>
        {
            Items = rows, Page = page, PageSize = pageSize, TotalCount = count
        });
    }

    private async Task<HostelApplicationAuthorityDto> MapAuthorityAsync(
        HostelApplicationAuthority authority, CancellationToken ct)
    {
        var student = await _students.GetQueryable().AsNoTracking().Where(s =>
            s.TenantId == authority.TenantId && s.Id == authority.StudentId)
            .Select(s => s.FullName).FirstOrDefaultAsync(ct);
        var guardian = await _guardians.GetQueryable().AsNoTracking().Where(g =>
            g.TenantId == authority.TenantId && g.Id == authority.GuardianId)
            .Select(g => g.FullName).FirstOrDefaultAsync(ct);
        return new HostelApplicationAuthorityDto
        {
            Id = authority.Id, StudentName = student ?? string.Empty, GuardianName = guardian ?? string.Empty,
            ExpiresAtUtc = authority.ExpiresAtUtc, RevokedAtUtc = authority.RevokedAtUtc,
            RowVersion = Convert.ToBase64String(authority.RowVersion)
        };
    }

    public async Task<ApiResponse<HostelApplicationAuthorityDto>> GrantApplicationAuthorityAsync(
        GrantHostelApplicationAuthorityRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageHostelAuthority()) return Denied<HostelApplicationAuthorityDto>();
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.StudentEnrollmentReference == Guid.Empty || request.GuardianReference == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.EvidenceReference) || request.EvidenceReference.Trim().Length > 150 ||
            request.ExpiresAtUtc.Kind != DateTimeKind.Utc)
            return AuthorityError("Enrollment, guardian, verified evidence and UTC expiry are required.");
        var tenant = _currentUser.TenantId;
        var now = _clock.GetUtcNow().UtcDateTime;
        if (request.ExpiresAtUtc <= now.AddHours(1) || request.ExpiresAtUtc > now.AddDays(365))
            return AuthorityError("Authority must expire between one hour and one year from now.");
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(e =>
                    e.TenantId == tenant && e.PublicId == request.StudentEnrollmentReference &&
                    e.IsCurrent && e.State == EnrollmentState.Active, token);
                if (enrollment == null) return AuthorityError("Active enrollment not found.", 404);
                if (!await CanManageCampusAsync(enrollment.CampusId, tenant, token))
                    return Denied<HostelApplicationAuthorityDto>();
                var guardian = await _guardians.GetQueryable().AsNoTracking().FirstOrDefaultAsync(g =>
                    g.TenantId == tenant && g.PublicId == request.GuardianReference &&
                    g.IsActive && g.UserId.HasValue, token);
                if (guardian == null) return AuthorityError("Verified guardian account not found.", 404);
                var link = await _studentGuardians.GetQueryable().AsNoTracking().FirstOrDefaultAsync(g =>
                    g.TenantId == tenant && g.StudentId == enrollment.StudentId &&
                    g.GuardianId == guardian.Id, token);
                if (link == null) return AuthorityError("Active student–guardian relationship not found.", 404);
                var duplicate = await _applicationAuthorities.GetQueryable().FirstOrDefaultAsync(a =>
                    a.TenantId == tenant && a.ClientRequestId == request.ClientRequestId, token);
                if (duplicate != null)
                {
                    if (duplicate.StudentGuardianId != link.Id || duplicate.ExpiresAtUtc != request.ExpiresAtUtc ||
                        duplicate.EvidenceReference != request.EvidenceReference.Trim())
                        return AuthorityError("Request ID reused for different authority.", 409);
                    return ApiResponse<HostelApplicationAuthorityDto>.SuccessResponse(
                        await MapAuthorityAsync(duplicate, token), "Authority already recorded.");
                }
                if (await _applicationAuthorities.GetQueryable().AsNoTracking().AnyAsync(a =>
                    a.TenantId == tenant && a.StudentGuardianId == link.Id &&
                    a.RevokedAtUtc == null && a.ExpiresAtUtc > now, token))
                    return AuthorityError("An active authority already exists for this guardian and student.", 409);
                var authority = new HostelApplicationAuthority
                {
                    TenantId = tenant, ClientRequestId = request.ClientRequestId,
                    StudentGuardianId = link.Id, StudentId = enrollment.StudentId, GuardianId = guardian.Id,
                    CampusId = enrollment.CampusId, EvidenceReference = request.EvidenceReference.Trim(),
                    GrantedAtUtc = now, ExpiresAtUtc = request.ExpiresAtUtc,
                    GrantedByUserId = _currentUser.UserId, CreatedAt = now, CreatedBy = _currentUser.UserId
                };
                await _applicationAuthorities.AddAsync(authority);
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<HostelApplicationAuthorityDto>.SuccessResponse(
                    await MapAuthorityAsync(authority, token), "Guardian application authority granted.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel guardian authority conflict tenant {TenantId}", tenant);
            return AuthorityError("Concurrent or duplicate authority. Reload and retry.", 409);
        }
        catch (Exception ex) when (HasHostelSqlSerializationConflict(ex) || ex is System.Transactions.TransactionAbortedException)
        {
            _logger.LogWarning(ex, "Hostel guardian authority SQL conflict tenant {TenantId}", tenant);
            return AuthorityError("Concurrent authority change. Reload and retry.", 409);
        }
    }

    public async Task<ApiResponse<HostelApplicationAuthorityDto>> RevokeApplicationAuthorityAsync(
        long id, RevokeHostelApplicationAuthorityRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageHostelAuthority()) return Denied<HostelApplicationAuthorityDto>();
        if (id <= 0 || request == null || !TryDecodeVersion(request.RowVersion, out var version) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            return AuthorityError("Authority, current version and revocation reason are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var authority = await _applicationAuthorities.GetQueryable().FirstOrDefaultAsync(a =>
                    a.TenantId == tenant && a.Id == id, token);
                if (authority == null) return AuthorityError("Authority not found.", 404);
                if (!await CanManageCampusAsync(authority.CampusId, tenant, token))
                    return Denied<HostelApplicationAuthorityDto>();
                if (!authority.RowVersion.AsSpan().SequenceEqual(version))
                    return AuthorityError("Authority changed. Reload and retry.", 409);
                if (authority.RevokedAtUtc.HasValue)
                    return AuthorityError("Authority already revoked.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                authority.RevokedAtUtc = now; authority.RevokedByUserId = _currentUser.UserId;
                authority.RevocationReason = request.Reason.Trim();
                authority.UpdatedAt = now; authority.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<HostelApplicationAuthorityDto>.SuccessResponse(
                    await MapAuthorityAsync(authority, token), "Authority revoked.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return AuthorityError("Authority changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel guardian authority revocation conflict tenant {TenantId}", tenant);
            return AuthorityError("Authority revocation conflict.", 409);
        }
    }

    public async Task<ApiResponse<PagedResult<HostelApplicationAuthorityDto>>> GetApplicationAuthoritiesAsync(
        long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManageHostelAuthority()) return Denied<PagedResult<HostelApplicationAuthorityDto>>();
        if (campusId is <= 0 || page < 1 || pageSize is < 1 or > 50 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<HostelApplicationAuthorityDto>>.ErrorResponse("Invalid authority page.");
        var tenant = _currentUser.TenantId;
        var query = from authority in _applicationAuthorities.GetQueryable().AsNoTracking()
            join student in _students.GetQueryable().AsNoTracking() on authority.StudentId equals student.Id
            join guardian in _guardians.GetQueryable().AsNoTracking() on authority.GuardianId equals guardian.Id
            where authority.TenantId == tenant && student.TenantId == tenant && guardian.TenantId == tenant &&
                (!campusId.HasValue || authority.CampusId == campusId.Value)
            select new { authority, StudentName = student.FullName, GuardianName = guardian.FullName };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.authority.CampusId));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.authority.GrantedAtUtc).ThenByDescending(x => x.authority.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.authority.Id, x.StudentName, x.GuardianName,
                x.authority.ExpiresAtUtc, x.authority.RevokedAtUtc, x.authority.RowVersion })
            .ToListAsync(ct);
        return ApiResponse<PagedResult<HostelApplicationAuthorityDto>>.SuccessResponse(new PagedResult<HostelApplicationAuthorityDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = items.Select(x => new HostelApplicationAuthorityDto
            {
                Id = x.Id, StudentName = x.StudentName, GuardianName = x.GuardianName,
                ExpiresAtUtc = x.ExpiresAtUtc, RevokedAtUtc = x.RevokedAtUtc,
                RowVersion = Convert.ToBase64String(x.RowVersion)
            }).ToList()
        });
    }
}
