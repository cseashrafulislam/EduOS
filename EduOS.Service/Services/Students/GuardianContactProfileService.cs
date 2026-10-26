using System.Data;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Students;

/// <summary>
/// Updates tenant-local guardian contact data shared by siblings in that institution.
/// Does not change global Person, authentication credentials, student/guardian links,
/// enrollment identities, consent or historical admission snapshots.
/// </summary>
public sealed class GuardianContactProfileService(
    EduOSDbContext db, ICurrentUserService user, ILogger<GuardianContactProfileService> logger)
    : IGuardianContactProfileService
{
    private const string Scope = "GuardianContactProfile";
    private const string AuditAction = "Guardian.ContactUpdated";
    private bool Allowed => user.IsAuthenticated && user.IsTenantAdmin && user.TenantId > 0 && user.UserId > 0;
    private static ApiResponse<T> Fail<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool HasControl(string? value) => value?.Any(char.IsControl) == true;

    // The student must be active and the relationship must still be active at the time of use.
    // This is checked again INSIDE the write transaction to close the unlink/update race.
    private async Task<Guardian?> FindLinkedAsync(Guid studentReference, Guid guardianReference,
        CancellationToken ct)
    {
        // Separate tenant-scoped predicates translate consistently on SQL Server and
        // the in-memory test provider; the write path repeats all reads under SERIALIZABLE.
        var studentId = await db.Students.AsNoTracking()
            .Where(s => s.TenantId == user.TenantId && s.PublicId == studentReference &&
                !s.IsDeleted && (s.StatusCode == "Active" || s.StatusCode == "Suspended"))
            .Select(s => s.Id).FirstOrDefaultAsync(ct);
        if (studentId <= 0) return null;
        var guardian = await db.Guardians.FirstOrDefaultAsync(g =>
            g.TenantId == user.TenantId && g.PublicId == guardianReference &&
            g.IsActive && !g.IsDeleted, ct);
        if (guardian == null) return null;
        var linked = await db.StudentGuardians.AsNoTracking().AnyAsync(link =>
            link.TenantId == user.TenantId && link.StudentId == studentId &&
            link.GuardianId == guardian.Id && !link.IsDeleted, ct);
        return linked ? guardian : null;
    }

    public async Task<ApiResponse<GuardianContactProfileDto>> GetAsync(Guid studentReference,
        Guid guardianReference, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<GuardianContactProfileDto>("Tenant administrator required.", 403);
        if (studentReference == Guid.Empty || guardianReference == Guid.Empty)
            return Fail<GuardianContactProfileDto>("Student and guardian references required.");
        try
        {
            var guardian = await FindLinkedAsync(studentReference, guardianReference, ct);
            return guardian == null ? Fail<GuardianContactProfileDto>("Linked guardian not found.", 404) :
                ApiResponse<GuardianContactProfileDto>.SuccessResponse(Map(studentReference, guardian));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Guardian contact read failed in tenant {TenantId}", user.TenantId);
            return Fail<GuardianContactProfileDto>("Could not load guardian contact profile.", 500);
        }
    }

    public async Task<ApiResponse<GuardianContactProfileDto>> UpdateAsync(Guid studentReference,
        Guid guardianReference, UpdateGuardianContactProfileRequestDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<GuardianContactProfileDto>("Tenant administrator required.", 403);
        if (studentReference == Guid.Empty || guardianReference == Guid.Empty ||
            request == null || request.ClientRequestId == Guid.Empty ||
            request.Phone?.Length > 30 || request.Email?.Length > 200 ||
            request.Occupation?.Length > 150 || request.Address?.Length > 1000 ||
            request.Reason == null || request.Reason.Trim().Length is < 10 or > 500 ||
            HasControl(request.Phone) || HasControl(request.Email) ||
            HasControl(request.Occupation) || HasControl(request.Address) || HasControl(request.Reason))
            return Fail<GuardianContactProfileDto>("Invalid guardian contact request.");

        var phone = Clean(request.Phone);
        var email = Clean(request.Email);
        var occupation = Clean(request.Occupation);
        var address = Clean(request.Address);
        var reason = request.Reason.Trim();
        if (email != null && (!MailAddress.TryCreate(email, out var parsed) ||
            !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase)))
            return Fail<GuardianContactProfileDto>("Invalid email address.");

        // Include both opaque references and reason in the replay fingerprint. A request ID
        // cannot be reused for a different sibling, guardian, reason or contact payload.
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\u001f", studentReference.ToString("N"), guardianReference.ToString("N"),
                phone ?? "", email ?? "", occupation ?? "", address ?? "", reason))));
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var guardian = await FindLinkedAsync(studentReference, guardianReference, token);
                if (guardian == null)
                    return Fail<GuardianContactProfileDto>("Linked guardian not found.", 404);

                var previous = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Scope == Scope &&
                    x.RequestId == request.ClientRequestId && !x.IsDeleted, token);
                var same = guardian.Phone == phone && guardian.Email == email &&
                    guardian.Occupation == occupation && guardian.Address == address;
                if (previous != null)
                    return previous.TargetId == guardian.Id && previous.RequestHash == digest && same
                        ? ApiResponse<GuardianContactProfileDto>.SuccessResponse(
                            Map(studentReference, guardian), "Guardian contact already saved.")
                        : Fail<GuardianContactProfileDto>("Request ID already used or contact profile changed.", 409);

                if (guardian.RowVersion.Length > 0)
                {
                    byte[] supplied;
                    try { supplied = Convert.FromBase64String(request.RowVersion ?? ""); }
                    catch (FormatException) { return Fail<GuardianContactProfileDto>("Invalid guardian version.", 409); }
                    if (supplied.Length == 0 || supplied.Length != guardian.RowVersion.Length ||
                        !CryptographicOperations.FixedTimeEquals(guardian.RowVersion, supplied))
                        return Fail<GuardianContactProfileDto>("Guardian changed. Reload and retry.", 409);
                }

                var now = DateTime.UtcNow;
                if (!same)
                {
                    guardian.Phone = phone;
                    guardian.Email = email;
                    guardian.Occupation = occupation;
                    guardian.Address = address;
                    guardian.UpdatedAt = now;
                    guardian.UpdatedBy = user.UserId;
                }
                db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    TenantId = user.TenantId, Scope = Scope, RequestId = request.ClientRequestId,
                    RequestHash = digest, ResultHash = digest, TargetId = guardian.Id,
                    CompletedByUserId = user.UserId, CompletedAt = now,
                    ExpiresAt = now.AddYears(5), CreatedAt = now, CreatedBy = user.UserId
                });
                // Do not store raw phone, email, address or other private values in audit.
                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = user.TenantId, UserId = user.UserId, Action = AuditAction,
                    EntityName = "Guardian", EntityId = guardian.Id,
                    RequestId = "GC:" + request.ClientRequestId.ToString("N"),
                    OldValue = reason, NewValue = digest, OccurredAt = now, IsSuccess = true
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<GuardianContactProfileDto>.SuccessResponse(
                    Map(studentReference, guardian), "Guardian contact saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<GuardianContactProfileDto>("Guardian changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Guardian contact conflict in tenant {TenantId}", user.TenantId);
            return Fail<GuardianContactProfileDto>("Guardian contact conflict.", 409);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Guardian contact update failed in tenant {TenantId}", user.TenantId);
            return Fail<GuardianContactProfileDto>("Could not save guardian contact profile.", 500);
        }
    }

    private static GuardianContactProfileDto Map(Guid studentReference, Guardian guardian) => new()
    {
        StudentReference = studentReference, GuardianReference = guardian.PublicId,
        FullName = guardian.FullName, Phone = guardian.Phone, Email = guardian.Email,
        Occupation = guardian.Occupation, Address = guardian.Address,
        RowVersion = Convert.ToBase64String(guardian.RowVersion)
    };
}
