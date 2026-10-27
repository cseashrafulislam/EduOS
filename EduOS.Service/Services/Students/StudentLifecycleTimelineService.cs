using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Students;

/// <summary>Read-only, tenant-owned lifecycle audit facts with bounded, stable pagination.</summary>
public sealed class StudentLifecycleTimelineService : IStudentLifecycleTimelineService
{
    private const int MaxPage = 50;
    private const int MaxPageSize = 100;
    private readonly EduOSDbContext _db;
    private readonly ICurrentUserService _user;

    public StudentLifecycleTimelineService(EduOSDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    // Preserve the existing service contract for callers that expect the latest 100 events.
    public Task<ApiResponse<StudentLifecycleTimelineDto>> GetAsync(Guid studentReference,
        CancellationToken cancellationToken = default) =>
        GetPageAsync(studentReference, 1, 100, cancellationToken);

    public async Task<ApiResponse<StudentLifecycleTimelineDto>> GetPageAsync(Guid studentReference,
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!_user.IsAuthenticated || !_user.IsTenantAdmin || _user.TenantId <= 0)
            return ApiResponse<StudentLifecycleTimelineDto>.ErrorResponse("Institution administrator access required.", 403);
        if (studentReference == Guid.Empty || page < 1 || page > MaxPage ||
            pageSize < 1 || pageSize > MaxPageSize)
            return ApiResponse<StudentLifecycleTimelineDto>.ErrorResponse("Valid student, page and page size are required.", 400);

        var tenant = _user.TenantId;
        var student = await _db.Students.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.PublicId == studentReference && !x.IsDeleted)
            .Select(x => new { x.Id, x.PublicId, x.StudentCode, x.StatusCode })
            .FirstOrDefaultAsync(cancellationToken);
        if (student == null)
            return ApiResponse<StudentLifecycleTimelineDto>.ErrorResponse("Student not found.", 404);

        // Each ordered source contributes at most the requested window plus one.
        // This is sufficient for globally ordered paging without loading a tenant's full audit history.
        var offset = (page - 1) * pageSize;
        var window = offset + pageSize + 1;
        var changes = await _db.StudentStatusHistories.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted)
            .OrderByDescending(x => x.EffectiveAt).ThenByDescending(x => x.Id).Take(window)
            .Select(x => new { x.Id, x.EffectiveAt, x.FromStatusCode, x.ToStatusCode,
                x.Reason, x.ChangedByUserId }).ToListAsync(cancellationToken);
        var promotions = await _db.StudentPromotionRecords.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted)
            .OrderByDescending(x => x.ProcessedAt).ThenByDescending(x => x.Id).Take(window)
            .Select(x => new { x.Id, x.ProcessedAt, x.Decision, x.Note, x.ProcessedByUserId })
            .ToListAsync(cancellationToken);
        var exits = await _db.StudentExitRecords.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted)
            .OrderByDescending(x => x.ProcessedAt).ThenByDescending(x => x.Id).Take(window)
            .Select(x => new { x.Id, x.ProcessedAt, x.ExitType, x.Reason, x.ProcessedByUserId })
            .ToListAsync(cancellationToken);

        // Source rank and record ID resolve timestamp ties deterministically across requests.
        var facts = new List<TimelineFact>(changes.Count + promotions.Count + exits.Count);
        facts.AddRange(changes.Select(x => new TimelineFact(x.Id, 0, new StudentLifecycleEventDto
        {
            EventType = "Status change", OccurredAt = x.EffectiveAt,
            FromStatus = x.FromStatusCode, ToStatus = x.ToStatusCode,
            Reason = x.Reason, ActorUserId = x.ChangedByUserId
        })));
        facts.AddRange(promotions.Select(x => new TimelineFact(x.Id, 1, new StudentLifecycleEventDto
        {
            EventType = "Progression", OccurredAt = x.ProcessedAt,
            Detail = x.Decision.ToString(), Reason = x.Note, ActorUserId = x.ProcessedByUserId
        })));
        facts.AddRange(exits.Select(x => new TimelineFact(x.Id, 2, new StudentLifecycleEventDto
        {
            EventType = "Exit", OccurredAt = x.ProcessedAt,
            Detail = x.ExitType.ToString(), Reason = x.Reason, ActorUserId = x.ProcessedByUserId
        })));
        var ordered = facts.OrderByDescending(x => x.Event.OccurredAt)
            .ThenBy(x => x.SourceRank).ThenByDescending(x => x.Id).ToList();
        return ApiResponse<StudentLifecycleTimelineDto>.SuccessResponse(new StudentLifecycleTimelineDto
        {
            StudentReference = student.PublicId,
            StudentCode = student.StudentCode,
            CurrentStatus = student.StatusCode,
            Page = page,
            PageSize = pageSize,
            HasMore = ordered.Count > offset + pageSize,
            Events = ordered.Skip(offset).Take(pageSize).Select(x => x.Event).ToList()
        });
    }

    private sealed record TimelineFact(long Id, int SourceRank, StudentLifecycleEventDto Event);
}
