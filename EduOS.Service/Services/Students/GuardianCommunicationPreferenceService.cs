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

    private IQueryable<GuardianLink> Mine() =>
        from link in db.StudentGuardians
        join guardian in db.Guardians on link.GuardianId equals guardian.Id
        join student in db.Students on link.StudentId equals student.Id
        where link.TenantId == user.TenantId && guardian.TenantId == user.TenantId &&
            student.TenantId == user.TenantId && guardian.UserId == user.UserId &&
            guardian.IsActive && !guardian.IsDeleted && !link.IsDeleted && !student.IsDeleted &&
            (student.StatusCode == "Active" || student.StatusCode == "Suspended")
        select new GuardianLink(link, student);

    private sealed record GuardianLink(StudentGuardian Link, Student Student);

    private static GuardianCommunicationPreferenceDto Map(GuardianLink row) => new()
    {
        LinkId = row.Link.Id,
        StudentReference = row.Student.PublicId,
        StudentName = row.Student.FullName,
        StudentCode = row.Student.StudentCode,
        RelationCode = row.Link.RelationCode,
        CanReceiveSms = row.Link.CanReceiveSms,
        CanReceiveEmail = row.Link.CanReceiveEmail,
        RowVersion = Convert.ToBase64String(row.Link.RowVersion)
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
        var rows = await Mine().AsNoTracking().OrderBy(x => x.Student.FullName)
            .ThenBy(x => x.Link.Id).Take(101).ToListAsync(ct);
        if (rows.Count > 100)
            return Fail<IReadOnlyList<GuardianCommunicationPreferenceDto>>("Too many linked relationships.", 409);
        IReadOnlyList<GuardianCommunicationPreferenceDto> result = rows.Select(Map).ToList();
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
            var row = await Mine().FirstOrDefaultAsync(x => x.Link.Id == linkId, ct);
            if (row == null) return Fail<GuardianCommunicationPreferenceDto>("Relationship not found.", 404);
            // An exact retry is safe even if its original version was consumed.
            if (row.Link.CanReceiveSms == request.CanReceiveSms &&
                row.Link.CanReceiveEmail == request.CanReceiveEmail)
                return ApiResponse<GuardianCommunicationPreferenceDto>.SuccessResponse(Map(row),
                    "Preferences already saved.");
            if (!VersionMatches(row.Link.RowVersion, request.RowVersion))
                return Fail<GuardianCommunicationPreferenceDto>("Preferences changed. Reload and retry.", 409);
            var previous = $"SMS={row.Link.CanReceiveSms};Email={row.Link.CanReceiveEmail}";
            row.Link.CanReceiveSms = request.CanReceiveSms;
            row.Link.CanReceiveEmail = request.CanReceiveEmail;
            row.Link.UpdatedAt = DateTime.UtcNow;
            row.Link.UpdatedBy = user.UserId;
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = user.TenantId, UserId = user.UserId,
                Action = "Guardian.CommunicationPreferencesChanged",
                EntityName = "StudentGuardian", EntityId = row.Link.Id,
                OldValue = previous,
                NewValue = $"SMS={request.CanReceiveSms};Email={request.CanReceiveEmail}",
                OccurredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ApiResponse<GuardianCommunicationPreferenceDto>.SuccessResponse(Map(row));
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
