using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Students;

/// <summary>Read-only institutional audit timeline from canonical, tenant-owned lifecycle records.</summary>
public sealed class StudentLifecycleTimelineService : IStudentLifecycleTimelineService
{
    private const int MaxEvents = 100;
    private readonly EduOSDbContext _db;
    private readonly ICurrentUserService _user;

    public StudentLifecycleTimelineService(EduOSDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    public async Task<ApiResponse<StudentLifecycleTimelineDto>> GetAsync(Guid studentReference,
        CancellationToken cancellationToken = default)
    {
        if (!_user.IsAuthenticated || !_user.IsTenantAdmin || _user.TenantId <= 0)
            return ApiResponse<StudentLifecycleTimelineDto>.ErrorResponse("Institution administrator access required.", 403);
        if (studentReference == Guid.Empty)
            return ApiResponse<StudentLifecycleTimelineDto>.ErrorResponse("A student reference is required.", 400);

        var tenant = _user.TenantId;
        var student = await _db.Students.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.PublicId == studentReference && !x.IsDeleted)
            .Select(x => new { x.Id, x.PublicId, x.StudentCode, x.StatusCode })
            .FirstOrDefaultAsync(cancellationToken);
        if (student == null)
            return ApiResponse<StudentLifecycleTimelineDto>.ErrorResponse("Student not found.", 404);

        // Read only canonical historical facts; never reconstruct old statuses from current enrollment.
        // Each source is bounded before the in-memory merge, preventing unbounded tenant queries.
        var changes = await _db.StudentStatusHistories.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted)
            .OrderByDescending(x => x.EffectiveAt).ThenByDescending(x => x.Id).Take(MaxEvents + 1)
            .Select(x => new StudentLifecycleEventDto
            {
                EventType = "Status change", OccurredAt = x.EffectiveAt,
                FromStatus = x.FromStatusCode, ToStatus = x.ToStatusCode,
                Reason = x.Reason, ActorUserId = x.ChangedByUserId
            }).ToListAsync(cancellationToken);

        var promotions = await _db.StudentPromotionRecords.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted)
            .OrderByDescending(x => x.ProcessedAt).ThenByDescending(x => x.Id).Take(MaxEvents + 1)
            .Select(x => new { x.ProcessedAt, x.Decision, x.Note, x.ProcessedByUserId })
            .ToListAsync(cancellationToken);

        var exits = await _db.StudentExitRecords.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted)
            .OrderByDescending(x => x.ProcessedAt).ThenByDescending(x => x.Id).Take(MaxEvents + 1)
            .Select(x => new { x.ProcessedAt, x.ExitType, x.Reason, x.ProcessedByUserId })
            .ToListAsync(cancellationToken);

        var events = changes.Concat(promotions.Select(x => new StudentLifecycleEventDto
            {
                EventType = "Progression", OccurredAt = x.ProcessedAt,
                Detail = x.Decision.ToString(), Reason = x.Note, ActorUserId = x.ProcessedByUserId
            }))
            .Concat(exits.Select(x => new StudentLifecycleEventDto
            {
                EventType = "Exit", OccurredAt = x.ProcessedAt,
                Detail = x.ExitType.ToString(), Reason = x.Reason, ActorUserId = x.ProcessedByUserId
            }))
            .OrderByDescending(x => x.OccurredAt).ThenBy(x => x.EventType, StringComparer.Ordinal)
            .ToList();

        return ApiResponse<StudentLifecycleTimelineDto>.SuccessResponse(new StudentLifecycleTimelineDto
        {
            StudentReference = student.PublicId,
            StudentCode = student.StudentCode,
            CurrentStatus = student.StatusCode,
            HasMore = events.Count > MaxEvents,
            Events = events.Take(MaxEvents).ToList()
        });
    }
}
