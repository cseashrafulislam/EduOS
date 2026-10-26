using System.Data;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
/// Edits tenant-local student contact fields. Government identifiers, global Person,
/// admission/enrollment snapshots and historical certificates are never changed.
/// </summary>
public sealed class StudentContactProfileService(
    EduOSDbContext db, ICurrentUserService user, ILogger<StudentContactProfileService> logger)
    : IStudentContactProfileService
{
    private const string AuditAction = "Student.ContactUpdated";
    private bool Allowed => user.IsAuthenticated && user.IsTenantAdmin && user.TenantId > 0 && user.UserId > 0;
    private static ApiResponse<T> Fail<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
    private Task<Student?> FindAsync(Guid reference, CancellationToken ct) =>
        db.Students.FirstOrDefaultAsync(x => x.TenantId == user.TenantId && x.PublicId == reference &&
            !x.IsDeleted && (x.StatusCode == "Active" || x.StatusCode == "Suspended"), ct);

    public async Task<ApiResponse<StudentContactProfileDto>> GetAsync(Guid studentReference, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<StudentContactProfileDto>("Tenant administrator required.", 403);
        if (studentReference == Guid.Empty) return Fail<StudentContactProfileDto>("Student reference required.");
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == studentReference && !x.IsDeleted &&
            (x.StatusCode == "Active" || x.StatusCode == "Suspended"), ct);
        return student == null ? Fail<StudentContactProfileDto>("Student not found.", 404) :
            ApiResponse<StudentContactProfileDto>.SuccessResponse(Map(student));
    }

    public async Task<ApiResponse<StudentContactProfileDto>> UpdateAsync(Guid studentReference,
        UpdateStudentContactProfileRequestDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<StudentContactProfileDto>("Tenant administrator required.", 403);
        if (studentReference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.Phone?.Length > 30 || request.Email?.Length > 200 || request.Address?.Length > 1000 ||
            request.Reason == null || request.Reason.Trim().Length is < 10 or > 500 ||
            string.IsNullOrWhiteSpace(request.PreferredLanguage) || request.PreferredLanguage.Length > 20 ||
            !Regex.IsMatch(request.PreferredLanguage, "^[a-z]{2,3}(-[A-Z]{2})?$") ||
            HasControl(request.Phone) || HasControl(request.Email) ||
            HasControl(request.Address) || HasControl(request.Reason))
            return Fail<StudentContactProfileDto>("Invalid contact profile request.");

        var phone = Clean(request.Phone);
        var email = Clean(request.Email);
        var address = Clean(request.Address);
        var language = request.PreferredLanguage.Trim();
        if (email != null && (!MailAddress.TryCreate(email, out var parsed) ||
            !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase)))
            return Fail<StudentContactProfileDto>("Invalid email address.");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\u001f", phone ?? "", email ?? "", address ?? "", language))));
        var key = "SC:" + request.ClientRequestId.ToString("N");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var student = await FindAsync(studentReference, token);
                if (student == null) return Fail<StudentContactProfileDto>("Student not found.", 404);

                // A retry is accepted only for the original student and exact persisted payload.
                var previous = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Scope == "StudentContactProfile" &&
                    x.RequestId == request.ClientRequestId && !x.IsDeleted, token);
                var same = student.Phone == phone && student.Email == email &&
                    student.Address == address && student.PreferredLanguage == language;
                if (previous != null)
                    return previous.TargetId == student.Id && previous.RequestHash == digest && same
                        ? ApiResponse<StudentContactProfileDto>.SuccessResponse(Map(student), "Contact profile already saved.")
                        : Fail<StudentContactProfileDto>("Request ID was already used or the profile changed.", 409);

                if (student.RowVersion.Length > 0)
                {
                    byte[] supplied;
                    try { supplied = Convert.FromBase64String(request.RowVersion ?? ""); }
                    catch (FormatException) { return Fail<StudentContactProfileDto>("Invalid student version.", 409); }
                    if (supplied.Length == 0 || !CryptographicOperations.FixedTimeEquals(student.RowVersion, supplied))
                        return Fail<StudentContactProfileDto>("Student changed. Reload and retry.", 409);
                }

                var now = DateTime.UtcNow;
                if (!same)
                {
                    student.Phone = phone;
                    student.Email = email;
                    student.Address = address;
                    student.PreferredLanguage = language;
                    student.UpdatedAt = now;
                    student.UpdatedBy = user.UserId;
                }
                db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    TenantId = user.TenantId, Scope = "StudentContactProfile",
                    RequestId = request.ClientRequestId, RequestHash = digest,
                    ResultHash = digest, TargetId = student.Id, CompletedByUserId = user.UserId,
                    CompletedAt = now, ExpiresAt = now.AddYears(5),
                    CreatedAt = now, CreatedBy = user.UserId
                });
                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = user.TenantId, UserId = user.UserId, Action = AuditAction,
                    EntityName = "Student", EntityId = student.Id, RequestId = key,
                    OldValue = request.Reason.Trim(), NewValue = digest,
                    OccurredAt = now, IsSuccess = true
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<StudentContactProfileDto>.SuccessResponse(Map(student), "Contact profile saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<StudentContactProfileDto>("Student changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Student contact update conflict in tenant {TenantId}", user.TenantId);
            return Fail<StudentContactProfileDto>("Student contact update conflict.", 409);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Student contact update failed in tenant {TenantId}", user.TenantId);
            return Fail<StudentContactProfileDto>("Could not save student contact profile.", 500);
        }
    }

    private static bool HasControl(string? value) => value?.Any(char.IsControl) == true;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static StudentContactProfileDto Map(Student s) => new()
    {
        StudentReference = s.PublicId, Phone = s.Phone, Email = s.Email, Address = s.Address,
        PreferredLanguage = s.PreferredLanguage, RowVersion = Convert.ToBase64String(s.RowVersion)
    };
}
