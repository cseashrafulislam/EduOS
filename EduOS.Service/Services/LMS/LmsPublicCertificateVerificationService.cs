using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;

namespace EduOS.Service.Services.LMS;

/// <summary>
/// An explicit learner opt-in for short-lived, privacy-minimal public verification.
/// The bearer secret is returned once, never stored in plaintext or written to logs.
/// V1 formal credentials and LMS completion evidence remain independent.
/// </summary>
public sealed class LmsPublicCertificateVerificationService(
    EduOSDbContext db, ICurrentUserService user, TimeProvider clock,
    ILogger<LmsPublicCertificateVerificationService> logger) : ILmsPublicCertificateVerificationService
{
    private static ApiResponse<T> NotFound<T>() => ApiResponse<T>.ErrorResponse("Verification unavailable.", 404);
    private static ApiResponse<T> Conflict<T>() => ApiResponse<T>.ErrorResponse("Certificate changed. Reload and retry.", 409);
    private bool IsStudent => user.IsAuthenticated && user.UserId > 0 && user.TenantId > 0 &&
        user.IsInRole("Student");

    private IQueryable<LmsCompletionCertificate> Owned() =>
        from certificate in db.LmsCompletionCertificates
        join student in db.Students on certificate.StudentId equals student.Id
        where certificate.TenantId == user.TenantId && student.TenantId == user.TenantId &&
            student.UserId == user.UserId && !certificate.IsDeleted && !student.IsDeleted &&
            !certificate.IsRevoked
        select certificate;

    private static bool Matches(byte[] actual, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try
        {
            var bytes = Convert.FromBase64String(supplied);
            return bytes.Length == 8 && actual.AsSpan().SequenceEqual(bytes);
        }
        catch (FormatException) { return false; }
    }

    private static bool IsSqlConflict(Exception error)
    {
        for (Exception? current = error; current != null; current = current.InnerException)
            if (current is Microsoft.Data.SqlClient.SqlException sql &&
                sql.Number is 1205 or 1222 or 3960 or 2601 or 2627)
                return true;
        return false;
    }

    private static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(secret)));

    private static bool ValidToken(string? token) =>
        token is { Length: 43 } && token.All(c =>
            c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

    private static LmsPublicVerificationStatusDto Status(LmsCompletionCertificate row, DateTime now) => new()
    {
        Enabled = row.VerificationTokenHash != null && row.VerificationExpiresAt > now,
        ExpiresAt = row.VerificationExpiresAt,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    public async Task<ApiResponse<LmsPublicVerificationStatusDto>> GetStatusAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (!IsStudent || reference == Guid.Empty) return NotFound<LmsPublicVerificationStatusDto>();
        var row = await Owned().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == reference, ct);
        return row == null ? NotFound<LmsPublicVerificationStatusDto>() :
            ApiResponse<LmsPublicVerificationStatusDto>.SuccessResponse(Status(row, clock.GetUtcNow().UtcDateTime));
    }

    public async Task<ApiResponse<LmsPublicVerificationLinkDto>> EnableAsync(
        Guid reference, LmsPublicVerificationChangeDto request, CancellationToken ct = default)
    {
        if (!IsStudent || reference == Guid.Empty) return NotFound<LmsPublicVerificationLinkDto>();
        if (request == null || string.IsNullOrWhiteSpace(request.RowVersion))
            return ApiResponse<LmsPublicVerificationLinkDto>.ErrorResponse("Row version required.", 400);
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var row = await Owned().FirstOrDefaultAsync(x => x.PublicId == reference, token);
                if (row == null) return NotFound<LmsPublicVerificationLinkDto>();
                if (!Matches(row.RowVersion, request.RowVersion)) return Conflict<LmsPublicVerificationLinkDto>();
                var now = clock.GetUtcNow().UtcDateTime;
                var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
                row.VerificationTokenHash = Hash(secret);
                row.VerificationExpiresAt = now.AddDays(7);
                row.UpdatedAt = now; row.UpdatedBy = user.UserId;
                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = user.TenantId, UserId = user.UserId,
                    EntityName = "LmsCompletionCertificate", EntityId = row.Id,
                    Action = "LMS.PublicVerificationEnabled", OccurredAt = now, IsSuccess = true
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<LmsPublicVerificationLinkDto>.SuccessResponse(new LmsPublicVerificationLinkDto
                {
                    Token = secret, ExpiresAt = row.VerificationExpiresAt!.Value,
                    RowVersion = Convert.ToBase64String(row.RowVersion)
                });
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Conflict<LmsPublicVerificationLinkDto>(); }
        catch (Exception ex) when (IsSqlConflict(ex))
        {
            logger.LogWarning("LMS verification enable conflict for tenant {TenantId}", user.TenantId);
            return Conflict<LmsPublicVerificationLinkDto>();
        }
    }

    public async Task<ApiResponse<LmsPublicVerificationStatusDto>> DisableAsync(
        Guid reference, LmsPublicVerificationChangeDto request, CancellationToken ct = default)
    {
        if (!IsStudent || reference == Guid.Empty) return NotFound<LmsPublicVerificationStatusDto>();
        if (request == null || string.IsNullOrWhiteSpace(request.RowVersion))
            return ApiResponse<LmsPublicVerificationStatusDto>.ErrorResponse("Row version required.", 400);
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var row = await Owned().FirstOrDefaultAsync(x => x.PublicId == reference, token);
                if (row == null) return NotFound<LmsPublicVerificationStatusDto>();
                if (!Matches(row.RowVersion, request.RowVersion)) return Conflict<LmsPublicVerificationStatusDto>();
                var now = clock.GetUtcNow().UtcDateTime;
                if (row.VerificationTokenHash != null)
                {
                    row.VerificationTokenHash = null; row.VerificationExpiresAt = null;
                    row.UpdatedAt = now; row.UpdatedBy = user.UserId;
                    db.AuditLogs.Add(new AuditLog
                    {
                        TenantId = user.TenantId, UserId = user.UserId,
                        EntityName = "LmsCompletionCertificate", EntityId = row.Id,
                        Action = "LMS.PublicVerificationDisabled", OccurredAt = now, IsSuccess = true
                    });
                    await db.SaveChangesAsync(token);
                }
                return ApiResponse<LmsPublicVerificationStatusDto>.SuccessResponse(Status(row, now));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Conflict<LmsPublicVerificationStatusDto>(); }
        catch (Exception ex) when (IsSqlConflict(ex))
        {
            logger.LogWarning("LMS verification disable conflict for tenant {TenantId}", user.TenantId);
            return Conflict<LmsPublicVerificationStatusDto>();
        }
    }

    /// <summary>
    /// Anonymous verification deliberately bypasses the request tenant filter only for
    /// a SHA-256 bearer-token equality lookup. Subsequent checks explicitly bind the
    /// discovered tenant and LMS entitlement. Never expose learner/course PII or token.
    /// </summary>
    public async Task<ApiResponse<LmsPublicVerificationResultDto>> VerifyAsync(
        string? token, CancellationToken ct = default)
    {
        if (!ValidToken(token)) return NotFound<LmsPublicVerificationResultDto>();
        var now = clock.GetUtcNow().UtcDateTime;
        var matches = await db.LmsCompletionCertificates.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.VerificationTokenHash == Hash(token!) && !x.IsDeleted &&
                !x.IsRevoked && x.VerificationExpiresAt > now)
            .Select(x => new { x.TenantId, x.IssuedAt, x.Revision })
            .Take(2).ToListAsync(ct);
        if (matches.Count != 1) return NotFound<LmsPublicVerificationResultDto>();
        var record = matches[0];
        var tenant = await db.Tenants.AsNoTracking()
            .Where(x => x.Id == record.TenantId && !x.IsDeleted &&
                x.State == TenantState.Active && x.OnboardingStage == OnboardingStage.Completed &&
                x.OnboardingCompletedAt != null)
            .Select(x => new { x.Id, x.Name }).SingleOrDefaultAsync(ct);
        if (tenant == null) return NotFound<LmsPublicVerificationResultDto>();
        var subscription = await db.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.TenantId == tenant.Id && !x.IsDeleted &&
                x.StartsAt <= now && x.EndsAt > now &&
                (x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial ||
                 x.State == SubscriptionState.Grace), ct);
        if (!subscription) return NotFound<LmsPublicVerificationResultDto>();
        var module = await (
            from selection in db.TenantModules.IgnoreQueryFilters().AsNoTracking()
            join product in db.ProductModules.AsNoTracking()
                on selection.ProductModuleId equals product.Id
            where selection.TenantId == tenant.Id && !selection.IsDeleted &&
                selection.IsEnabled && product.IsActive && !product.IsDeleted && product.Code == "LMS"
            select selection.Id).AnyAsync(ct);
        if (!module) return NotFound<LmsPublicVerificationResultDto>();
        logger.LogInformation("Public LMS certificate verification accepted for tenant {TenantId}", tenant.Id);
        return ApiResponse<LmsPublicVerificationResultDto>.SuccessResponse(new LmsPublicVerificationResultDto
        {
            Status = "Valid", IssuerName = tenant.Name, IssuedAt = record.IssuedAt,
            Revision = record.Revision
        });
    }
}
