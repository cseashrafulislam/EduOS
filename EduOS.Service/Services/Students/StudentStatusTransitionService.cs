using System.Data;
using System.Security.Cryptography;
using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Students;

/// <summary>
/// Suspends/reactivates only the tenant-local operational student status.
/// Never changes enrollment state, rolls, admission decisions or historic snapshots.
/// </summary>
public sealed class StudentStatusTransitionService(
    EduOSDbContext db, ICurrentUserService user, ILogger<StudentStatusTransitionService> logger)
    : IStudentStatusTransitionService
{
    private const string Scope = "StudentOperationalStatus";
    private const string AuditAction = "Student.StatusChanged";
    private bool Allowed => user.IsAuthenticated && user.IsTenantAdmin && user.TenantId > 0 && user.UserId > 0;
    private static ApiResponse<T> Fail<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);

    public async Task<ApiResponse<StudentStatusTransitionDto>> GetAsync(Guid studentReference, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<StudentStatusTransitionDto>("Tenant administrator required.", 403);
        if (studentReference == Guid.Empty) return Fail<StudentStatusTransitionDto>("Student reference required.");
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == studentReference && !x.IsDeleted, ct);
        if (student == null) return Fail<StudentStatusTransitionDto>("Student not found.", 404);
        if (student.StatusCode != "Active" && student.StatusCode != "Suspended")
            return Fail<StudentStatusTransitionDto>("Exit and readmission states cannot be changed here.", 409);
        var enrollment = await CurrentEnrollmentAsync(student.Id, ct);
        return enrollment == null ? Fail<StudentStatusTransitionDto>("Current campus enrollment not found.", 409) :
            ApiResponse<StudentStatusTransitionDto>.SuccessResponse(Map(student, enrollment));
    }

    public async Task<ApiResponse<StudentStatusTransitionDto>> ChangeAsync(Guid studentReference,
        ChangeStudentOperationalStatusRequestDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<StudentStatusTransitionDto>("Tenant administrator required.", 403);
        if (studentReference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            (request.TargetStatus != "Active" && request.TargetStatus != "Suspended") ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 500 ||
            request.Reason.Any(char.IsControl))
            return Fail<StudentStatusTransitionDto>("Invalid student status transition request.");

        var reason = request.Reason.Trim();
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            studentReference.ToString("N") + "\u001f" + request.TargetStatus + "\u001f" + reason)));
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var student = await db.Students.FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.PublicId == studentReference && !x.IsDeleted, token);
                if (student == null) return Fail<StudentStatusTransitionDto>("Student not found.", 404);
                if (student.StatusCode != "Active" && student.StatusCode != "Suspended")
                    return Fail<StudentStatusTransitionDto>("Exit and readmission states cannot be changed here.", 409);

                var enrollment = await CurrentEnrollmentAsync(student.Id, token);
                if (enrollment == null)
                    return Fail<StudentStatusTransitionDto>("Current campus enrollment not found.", 409);

                var previous = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Scope == Scope &&
                    x.RequestId == request.ClientRequestId && !x.IsDeleted, token);
                if (previous != null)
                {
                    // An old request must not appear successful after a later status transition.
                    var latest = await db.StudentStatusHistories.AsNoTracking()
                        .Where(x => x.TenantId == user.TenantId && x.StudentId == student.Id && !x.IsDeleted)
                        .OrderByDescending(x => x.EffectiveAt).ThenByDescending(x => x.Id)
                        .FirstOrDefaultAsync(token);
                    return previous.TargetId == student.Id && previous.RequestHash == digest &&
                        student.StatusCode == request.TargetStatus && latest != null &&
                        latest.EffectiveAt == previous.CompletedAt && latest.ToStatusCode == request.TargetStatus
                        ? ApiResponse<StudentStatusTransitionDto>.SuccessResponse(Map(student, enrollment),
                            "Student status transition already applied.")
                        : Fail<StudentStatusTransitionDto>("Request ID reused or student status subsequently changed.", 409);
                }

                if (student.StatusCode == request.TargetStatus)
                    return Fail<StudentStatusTransitionDto>("Student already has this status.", 409);
                if (student.RowVersion.Length > 0)
                {
                    byte[] supplied;
                    try { supplied = Convert.FromBase64String(request.RowVersion ?? ""); }
                    catch (FormatException) { return Fail<StudentStatusTransitionDto>("Invalid student version.", 409); }
                    if (supplied.Length == 0 ||
                        !CryptographicOperations.FixedTimeEquals(student.RowVersion, supplied))
                        return Fail<StudentStatusTransitionDto>("Student changed. Reload and retry.", 409);
                }

                var from = student.StatusCode;
                var now = DateTime.UtcNow;
                student.StatusCode = request.TargetStatus;
                student.UpdatedAt = now;
                student.UpdatedBy = user.UserId;
                db.StudentStatusHistories.Add(new StudentStatusHistory
                {
                    TenantId = user.TenantId, StudentId = student.Id, FromStatusCode = from,
                    ToStatusCode = request.TargetStatus, EffectiveAt = now,
                    ChangedByUserId = user.UserId, Reason = reason,
                    CreatedAt = now, CreatedBy = user.UserId
                });
                db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    TenantId = user.TenantId, Scope = Scope, RequestId = request.ClientRequestId,
                    RequestHash = digest, ResultHash = digest, TargetId = student.Id,
                    CompletedByUserId = user.UserId, CompletedAt = now, ExpiresAt = now.AddYears(5),
                    CreatedAt = now, CreatedBy = user.UserId
                });
                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = user.TenantId, UserId = user.UserId, Action = AuditAction,
                    EntityName = "Student", EntityId = student.Id,
                    RequestId = "SS:" + request.ClientRequestId.ToString("N"),
                    OldValue = from, NewValue = request.TargetStatus,
                    OccurredAt = now, IsSuccess = true
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<StudentStatusTransitionDto>.SuccessResponse(Map(student, enrollment),
                    "Student status updated.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<StudentStatusTransitionDto>("Student changed. Reload and retry.", 409);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205)
        {
            logger.LogWarning(ex, "Concurrent student status transition deadlock in tenant {TenantId}", user.TenantId);
            return Fail<StudentStatusTransitionDto>("Concurrent status transition. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Student status conflict in tenant {TenantId}", user.TenantId);
            return Fail<StudentStatusTransitionDto>("Student status conflict.", 409);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Student status update failed in tenant {TenantId}", user.TenantId);
            return Fail<StudentStatusTransitionDto>("Student status update failed.", 500);
        }
    }

    private Task<StudentEnrollment?> CurrentEnrollmentAsync(long studentId, CancellationToken ct) =>
        db.StudentEnrollments.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.StudentId == studentId && !x.IsDeleted &&
            x.IsCurrent && x.State == EnrollmentState.Active &&
            db.Campuses.Any(c => c.Id == x.CampusId && c.TenantId == user.TenantId && !c.IsDeleted))
            .OrderByDescending(x => x.EnrollmentDate).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

    private static StudentStatusTransitionDto Map(Student student, StudentEnrollment enrollment) => new()
    {
        StudentReference = student.PublicId, CurrentEnrollmentReference = enrollment.PublicId,
        CampusId = enrollment.CampusId, Status = student.StatusCode,
        RowVersion = Convert.ToBase64String(student.RowVersion)
    };
}
