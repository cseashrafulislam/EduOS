using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace EduOS.Service.Services.LMS;

/// <summary>
/// Learner-controlled, per-certificate guardian consent. No implied disclosure from
/// pickup/financial/emergency contact flags. Revoking a relationship immediately
/// invalidates all shares, including guessed IDs and print URLs.
/// </summary>
public sealed class LmsCertificateGuardianService(
    EduOSDbContext db, ICurrentUserService user, TimeProvider clock,
    ILogger<LmsCertificateGuardianService> logger) : ILmsCertificateGuardianService
{
    private bool Student => user.IsAuthenticated && user.TenantId > 0 && user.UserId > 0 && user.IsInRole("Student");
    private bool Guardian => user.IsAuthenticated && user.TenantId > 0 && user.UserId > 0 &&
        (user.IsInRole("Guardian") || user.IsInRole("Parent"));
    private static ApiResponse<T> Deny<T>() => ApiResponse<T>.ErrorResponse("Certificate not found.", 404);
    private static ApiResponse<T> Invalid<T>() => ApiResponse<T>.ErrorResponse("Invalid certificate sharing request.", 400);

    // EF Core's SQL execution strategy may wrap deadlock victims in
    // InvalidOperationException rather than surfacing SqlException directly.
    // Only known SQL concurrency/uniqueness errors are translated to 409.
    private static bool IsSqlConflict(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current is Microsoft.Data.SqlClient.SqlException sql &&
                sql.Number is 1205 or 1222 or 3960 or 2601 or 2627)
                return true;
        return false;
    }

    private IQueryable<LmsCompletionCertificate> OwnedCertificates() =>
        from certificate in db.LmsCompletionCertificates
        join learner in db.Students on certificate.StudentId equals learner.Id
        where certificate.TenantId == user.TenantId && learner.TenantId == user.TenantId &&
            learner.UserId == user.UserId && !learner.IsDeleted && !certificate.IsDeleted &&
            !certificate.IsRevoked
        select certificate;

    private IQueryable<LmsGuardianChoiceDto> Eligible(long studentId) =>
        from link in db.StudentGuardians.AsNoTracking()
        join guardian in db.Guardians.AsNoTracking() on link.GuardianId equals guardian.Id
        where link.TenantId == user.TenantId && guardian.TenantId == user.TenantId &&
            link.StudentId == studentId && !link.IsDeleted && !guardian.IsDeleted &&
            guardian.IsActive && guardian.UserId.HasValue && guardian.UserId.Value > 0
        select new LmsGuardianChoiceDto
        {
            GuardianLinkId = link.Id, GuardianName = guardian.FullName, RelationCode = link.RelationCode
        };

    public async Task<ApiResponse<IReadOnlyList<LmsGuardianChoiceDto>>> GetEligibleGuardiansAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (!Student) return Deny<IReadOnlyList<LmsGuardianChoiceDto>>();
        var certificate = await OwnedCertificates().AsNoTracking()
            .Where(x => x.PublicId == reference).Select(x => new { x.StudentId }).FirstOrDefaultAsync(ct);
        if (certificate == null) return Deny<IReadOnlyList<LmsGuardianChoiceDto>>();
        var rows = await Eligible(certificate.StudentId).OrderBy(x => x.GuardianName)
            .ThenBy(x => x.GuardianLinkId).Take(25).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<LmsGuardianChoiceDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<LmsCertificateGuardianGrantDto>>> GetActiveGrantsAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (!Student) return Deny<IReadOnlyList<LmsCertificateGuardianGrantDto>>();
        var certificate = await OwnedCertificates().AsNoTracking()
            .Where(x => x.PublicId == reference).Select(x => new { x.Id, x.StudentId }).FirstOrDefaultAsync(ct);
        if (certificate == null) return Deny<IReadOnlyList<LmsCertificateGuardianGrantDto>>();
        var rows = await (from grant in db.LmsCertificateGuardianGrants.AsNoTracking()
            join link in db.StudentGuardians.AsNoTracking() on grant.StudentGuardianId equals link.Id
            join guardian in db.Guardians.AsNoTracking() on link.GuardianId equals guardian.Id
            where grant.TenantId == user.TenantId && link.TenantId == user.TenantId &&
                guardian.TenantId == user.TenantId && grant.CertificateId == certificate.Id &&
                link.StudentId == certificate.StudentId && !grant.IsDeleted && !link.IsDeleted &&
                !guardian.IsDeleted && guardian.IsActive && grant.RevokedAt == null
            orderby grant.GrantedAt descending, grant.Id descending
            select new LmsCertificateGuardianGrantDto
            {
                Reference = grant.PublicId, GuardianLinkId = link.Id,
                GuardianName = guardian.FullName, GrantedAt = grant.GrantedAt,
                RowVersion = Convert.ToBase64String(grant.RowVersion)
            }).Take(25).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<LmsCertificateGuardianGrantDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<LmsCertificateGuardianGrantDto>> GrantAsync(
        GrantLmsCertificateGuardianRequestDto request, CancellationToken ct = default)
    {
        if (!Student) return Deny<LmsCertificateGuardianGrantDto>();
        if (request == null || request.CertificateReference == Guid.Empty ||
            request.GuardianLinkId <= 0 || request.ClientRequestId == Guid.Empty)
            return Invalid<LmsCertificateGuardianGrantDto>();
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var certificate = await OwnedCertificates().FirstOrDefaultAsync(x =>
                    x.PublicId == request.CertificateReference, token);
                if (certificate == null) return Deny<LmsCertificateGuardianGrantDto>();
                var guardian = await Eligible(certificate.StudentId)
                    .FirstOrDefaultAsync(x => x.GuardianLinkId == request.GuardianLinkId, token);
                if (guardian == null) return Deny<LmsCertificateGuardianGrantDto>();
                var previous = await db.LmsCertificateGuardianGrants.FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, token);
                if (previous != null)
                {
                    if (previous.CertificateId != certificate.Id || previous.StudentGuardianId != guardian.GuardianLinkId)
                        return ApiResponse<LmsCertificateGuardianGrantDto>.ErrorResponse("Request reference conflict.", 409);
                    return ApiResponse<LmsCertificateGuardianGrantDto>.SuccessResponse(
                        Map(previous, guardian.GuardianName));
                }
                var active = await db.LmsCertificateGuardianGrants.FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.CertificateId == certificate.Id &&
                    x.StudentGuardianId == guardian.GuardianLinkId && !x.IsDeleted && x.RevokedAt == null, token);
                if (active != null)
                    return ApiResponse<LmsCertificateGuardianGrantDto>.SuccessResponse(Map(active, guardian.GuardianName));
                var now = clock.GetUtcNow().UtcDateTime;
                var grant = new LmsCertificateGuardianGrant
                {
                    TenantId = user.TenantId, CertificateId = certificate.Id,
                    StudentGuardianId = guardian.GuardianLinkId, ClientRequestId = request.ClientRequestId,
                    PublicId = Guid.NewGuid(), GrantedAt = now, GrantedByUserId = user.UserId,
                    CreatedAt = now, CreatedBy = user.UserId
                };
                db.LmsCertificateGuardianGrants.Add(grant);
                await db.SaveChangesAsync(token);
                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = user.TenantId, UserId = user.UserId, EntityName = "LmsCertificateGuardianGrant",
                    EntityId = grant.Id, Action = "LMS.CertificateGuardianGranted", OccurredAt = now, IsSuccess = true
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<LmsCertificateGuardianGrantDto>.SuccessResponse(Map(grant, guardian.GuardianName));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<LmsCertificateGuardianGrantDto>.ErrorResponse("Consent changed concurrently.", 409); }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222 or 3960 or 2601 or 2627)
        {
            logger.LogWarning(ex, "LMS guardian consent SQL race tenant {TenantId}", user.TenantId);
            return ApiResponse<LmsCertificateGuardianGrantDto>.ErrorResponse("Concurrent consent change. Retry.", 409);
        }
        catch (InvalidOperationException ex) when (IsSqlConflict(ex))
        {
            logger.LogWarning(ex, "LMS guardian consent transient SQL conflict tenant {TenantId}", user.TenantId);
            return ApiResponse<LmsCertificateGuardianGrantDto>.ErrorResponse("Concurrent consent change. Retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "LMS guardian certificate grant conflict tenant {TenantId}", user.TenantId);
            return ApiResponse<LmsCertificateGuardianGrantDto>.ErrorResponse("Consent conflicts with another request.", 409);
        }
    }

    public async Task<ApiResponse<LmsCertificateGuardianGrantDto>> RevokeAsync(
        Guid reference, RevokeLmsCertificateGuardianRequestDto request, CancellationToken ct = default)
    {
        if (!Student) return Deny<LmsCertificateGuardianGrantDto>();
        if (reference == Guid.Empty || request == null || string.IsNullOrWhiteSpace(request.RowVersion))
            return Invalid<LmsCertificateGuardianGrantDto>();
        byte[] expected;
        try { expected = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { return Invalid<LmsCertificateGuardianGrantDto>(); }
        if (expected.Length == 0 || expected.Length > 32) return Invalid<LmsCertificateGuardianGrantDto>();
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var grant = await db.LmsCertificateGuardianGrants.FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, token);
                if (grant == null || !await OwnedCertificates().AnyAsync(x => x.Id == grant.CertificateId, token))
                    return Deny<LmsCertificateGuardianGrantDto>();
                var guardian = await Eligible((await db.LmsCompletionCertificates.AsNoTracking()
                    .Where(x => x.TenantId == user.TenantId && x.Id == grant.CertificateId)
                    .Select(x => x.StudentId).SingleAsync(token)))
                    .FirstOrDefaultAsync(x => x.GuardianLinkId == grant.StudentGuardianId, token);
                if (guardian == null) return Deny<LmsCertificateGuardianGrantDto>();
                if (grant.RevokedAt.HasValue)
                    return ApiResponse<LmsCertificateGuardianGrantDto>.SuccessResponse(Map(grant, guardian.GuardianName));
                if (!grant.RowVersion.SequenceEqual(expected))
                    return ApiResponse<LmsCertificateGuardianGrantDto>.ErrorResponse("Consent changed. Reload.", 409);
                var now = clock.GetUtcNow().UtcDateTime;
                grant.RevokedAt = now; grant.RevokedByUserId = user.UserId;
                grant.UpdatedAt = now; grant.UpdatedBy = user.UserId;
                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = user.TenantId, UserId = user.UserId, EntityName = "LmsCertificateGuardianGrant",
                    EntityId = grant.Id, Action = "LMS.CertificateGuardianRevoked", OccurredAt = now, IsSuccess = true
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<LmsCertificateGuardianGrantDto>.SuccessResponse(Map(grant, guardian.GuardianName));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<LmsCertificateGuardianGrantDto>.ErrorResponse("Consent changed concurrently.", 409); }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222 or 3960 or 2601 or 2627)
        {
            logger.LogWarning(ex, "LMS guardian consent SQL race tenant {TenantId}", user.TenantId);
            return ApiResponse<LmsCertificateGuardianGrantDto>.ErrorResponse("Concurrent consent change. Retry.", 409);
        }
        catch (InvalidOperationException ex) when (IsSqlConflict(ex))
        {
            logger.LogWarning(ex, "LMS guardian consent transient SQL conflict tenant {TenantId}", user.TenantId);
            return ApiResponse<LmsCertificateGuardianGrantDto>.ErrorResponse("Concurrent consent change. Retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "LMS guardian certificate revoke conflict tenant {TenantId}", user.TenantId);
            return ApiResponse<LmsCertificateGuardianGrantDto>.ErrorResponse("Consent conflicts with another request.", 409);
        }
    }

    private IQueryable<LmsCompletionCertificate> SharedQuery() =>
        from grant in db.LmsCertificateGuardianGrants.AsNoTracking()
        join certificate in db.LmsCompletionCertificates.AsNoTracking() on grant.CertificateId equals certificate.Id
        join link in db.StudentGuardians.AsNoTracking() on grant.StudentGuardianId equals link.Id
        join guardian in db.Guardians.AsNoTracking() on link.GuardianId equals guardian.Id
        join learner in db.Students.AsNoTracking() on certificate.StudentId equals learner.Id
        join course in db.Courses.AsNoTracking() on certificate.CourseId equals course.Id
        where grant.TenantId == user.TenantId && certificate.TenantId == user.TenantId &&
            link.TenantId == user.TenantId && guardian.TenantId == user.TenantId &&
            learner.TenantId == user.TenantId && course.TenantId == user.TenantId &&
            link.StudentId == learner.Id && guardian.UserId == user.UserId &&
            guardian.IsActive && !guardian.IsDeleted && !link.IsDeleted && !learner.IsDeleted &&
            !course.IsDeleted && !certificate.IsDeleted && !certificate.IsRevoked &&
            !grant.IsDeleted && grant.RevokedAt == null
        select certificate;

    public async Task<ApiResponse<PagedResult<LmsCertificateDto>>> GetSharedCertificatesAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        if (!Guardian) return Deny<PagedResult<LmsCertificateDto>>();
        if (page < 1 || pageSize is < 1 or > 20 || (long)(page - 1) * pageSize > int.MaxValue)
            return Invalid<PagedResult<LmsCertificateDto>>();
        var query = SharedQuery();
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.IssuedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var courseIds = rows.Select(x => x.CourseId).Distinct().ToArray();
        var courses = await db.Courses.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            courseIds.Contains(x.Id)).Select(x => new { x.Id, x.PublicId })
            .ToDictionaryAsync(x => x.Id, x => x.PublicId, ct);
        return ApiResponse<PagedResult<LmsCertificateDto>>.SuccessResponse(new PagedResult<LmsCertificateDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = rows.Where(x => courses.ContainsKey(x.CourseId))
                .Select(x => LmsWorkflowService.MapCertificate(x, courses[x.CourseId])).ToList()
        });
    }

    public async Task<ApiResponse<LmsCertificateDto>> GetSharedCertificateAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (!Guardian || reference == Guid.Empty) return Deny<LmsCertificateDto>();
        var certificate = await SharedQuery().FirstOrDefaultAsync(x => x.PublicId == reference, ct);
        if (certificate == null) return Deny<LmsCertificateDto>();
        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == certificate.CourseId && !x.IsDeleted, ct);
        return course == null ? Deny<LmsCertificateDto>() :
            ApiResponse<LmsCertificateDto>.SuccessResponse(LmsWorkflowService.MapCertificate(certificate, course.PublicId));
    }

    private static LmsCertificateGuardianGrantDto Map(LmsCertificateGuardianGrant row, string name) => new()
    {
        Reference = row.PublicId, GuardianLinkId = row.StudentGuardianId, GuardianName = name,
        GrantedAt = row.GrantedAt, RowVersion = Convert.ToBase64String(row.RowVersion)
    };
}
