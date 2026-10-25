using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Students;

/// <summary>Guardian-owned SMS/email preferences only. Primary, pickup, financial and relationship rights stay administrator-controlled.</summary>
public sealed class GuardianCommunicationPreferenceService(
    EduOSDbContext db, ICurrentUserService user,
    ILogger<GuardianCommunicationPreferenceService> logger) : IGuardianCommunicationPreferenceService
{
    private bool Allowed => user.IsAuthenticated && user.UserId > 0 && user.TenantId > 0 &&
        (user.IsInRole("Guardian") || user.IsInRole("Parent"));

    private IQueryable<StudentGuardian> Mine() =>
        from link in db.StudentGuardians
        join guardian in db.Guardians on link.GuardianId equals guardian.Id
        join student in db.Students on link.StudentId equals student.Id
        where link.TenantId == user.TenantId && guardian.TenantId == user.TenantId &&
            student.TenantId == user.TenantId && guardian.UserId == user.UserId &&
            guardian.IsActive && !guardian.IsDeleted && !link.IsDeleted && !student.IsDeleted &&
            (student.StatusCode == "Active" || student.StatusCode == "Suspended")
        select link;

    private static GuardianCommunicationPreferenceDto Map(StudentGuardian link, Student student) => new()
    {
        LinkId = link.Id,
        StudentReference = student.PublicId,
        StudentName = student.FullName,
        StudentCode = student.StudentCode,
        RelationCode = link.RelationCode,
        CanReceiveSms = link.CanReceiveSms,
        CanReceiveEmail = link.CanReceiveEmail,
        RowVersion = Convert.ToBase64String(link.RowVersion)
    };

    private static bool VersionMatches(byte[] current, string supplied)
    {
        try
        {
            var expected = Convert.FromBase64String(supplied);
            return expected.Length > 0 && current.Length == expected.Length &&
                CryptographicOperations.FixedTimeEquals(current, expected);
        }
        catch (FormatException) { return false; }
    }

    private static ApiResponse<T> Fail<T>(string message, int code) =>
        ApiResponse<T>.ErrorResponse(message, code);

    public async Task<ApiResponse<IReadOnlyList<GuardianCommunicationPreferenceDto>>> GetMineAsync(
        CancellationToken ct = default)
    {
        if (!Allowed) return Fail<IReadOnlyList<GuardianCommunicationPreferenceDto>>("Guardian account required.", 403);
        var rows = await Mine().AsNoTracking().OrderBy(x => x.Id).Take(101).ToListAsync(ct);
        if (rows.Count > 100)
            return Fail<IReadOnlyList<GuardianCommunicationPreferenceDto>>("Too many linked relationships.", 409);
        var studentIds = rows.Select(x => x.StudentId).Distinct().ToArray();
        var students = await db.Students.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && studentIds.Contains(x.Id) && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Id, ct);
        IReadOnlyList<GuardianCommunicationPreferenceDto> result = rows
            .Where(x => students.ContainsKey(x.StudentId))
            .Select(x => Map(x, students[x.StudentId])).ToList();
        return ApiResponse<IReadOnlyList<GuardianCommunicationPreferenceDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<GuardianCommunicationPreferenceDto>> UpdateMineAsync(
        long linkId, UpdateGuardianCommunicationPreferenceDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<GuardianCommunicationPreferenceDto>("Guardian account required.", 403);
        if (linkId <= 0 || request == null || string.IsNullOrWhiteSpace(request.RowVersion))
            return Fail<GuardianCommunicationPreferenceDto>("Relationship and row version are required.", 400);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var link = await Mine().FirstOrDefaultAsync(x => x.Id == linkId, ct);
            if (link == null) return Fail<GuardianCommunicationPreferenceDto>("Relationship not found.", 404);
            var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == link.StudentId && !x.IsDeleted, ct);
            if (student == null) return Fail<GuardianCommunicationPreferenceDto>("Relationship not found.", 404);
            // An exact retry is safe even if its original version was consumed.
            if (link.CanReceiveSms == request.CanReceiveSms &&
                link.CanReceiveEmail == request.CanReceiveEmail)
                return ApiResponse<GuardianCommunicationPreferenceDto>.SuccessResponse(Map(link, student),
                    "Preferences already saved.");
            if (!VersionMatches(link.RowVersion, request.RowVersion))
                return Fail<GuardianCommunicationPreferenceDto>("Preferences changed. Reload and retry.", 409);
            var previous = $"SMS={link.CanReceiveSms};Email={link.CanReceiveEmail}";
            link.CanReceiveSms = request.CanReceiveSms;
            link.CanReceiveEmail = request.CanReceiveEmail;
            link.UpdatedAt = DateTime.UtcNow;
            link.UpdatedBy = user.UserId;
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = user.TenantId, UserId = user.UserId,
                Action = "Guardian.CommunicationPreferencesChanged",
                EntityName = "StudentGuardian", EntityId = link.Id,
                OldValue = previous,
                NewValue = $"SMS={request.CanReceiveSms};Email={request.CanReceiveEmail}",
                OccurredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ApiResponse<GuardianCommunicationPreferenceDto>.SuccessResponse(Map(link, student));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<GuardianCommunicationPreferenceDto>("Preferences changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Guardian preference update conflict in tenant {TenantId}", user.TenantId);
            return Fail<GuardianCommunicationPreferenceDto>("Preference update conflicted with another change.", 409);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Guardian preference update failed in tenant {TenantId}", user.TenantId);
            return Fail<GuardianCommunicationPreferenceDto>("Unable to save preferences.", 500);
        }
    }
}
