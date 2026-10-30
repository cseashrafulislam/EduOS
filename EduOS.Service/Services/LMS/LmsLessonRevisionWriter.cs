using EduOS.Core.Entities.LMS;
using EduOS.Core.Interfaces.IRepositories;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EduOS.Service.Services.LMS;

/// <summary>Writes immutable snapshots inside the same authoring transaction. Historical private file IDs are never returned to clients.</summary>
internal static class LmsLessonRevisionWriter
{
    private sealed record ResourceEvidence(long Id, string? Title, string? ExternalUrl,
        long? FileAssetId, int DisplayOrder);

    public static async Task BaselineAsync(IGenericRepository<LmsLessonRevision>? revisions,
        IGenericRepository<Lesson> lessons, IGenericRepository<LessonResource>? resources,
        long tenant, long lessonId, long actor, DateTime now, CancellationToken ct)
    {
        if (revisions == null || resources == null) return; // Legacy service fixtures omit optional repositories.
        if (!await revisions.GetQueryable().AnyAsync(x => x.TenantId == tenant && x.LessonId == lessonId, ct))
            await AppendAsync(revisions, lessons, resources, tenant, lessonId, "Baseline", actor, now, ct);
    }

    public static async Task AppendAsync(IGenericRepository<LmsLessonRevision>? revisions,
        IGenericRepository<Lesson> lessons, IGenericRepository<LessonResource>? resources,
        long tenant, long lessonId, string kind, long actor, DateTime now, CancellationToken ct)
    {
        if (revisions == null || resources == null) return;
        var lesson = await lessons.GetQueryable().AsNoTracking().SingleAsync(x =>
            x.TenantId == tenant && x.Id == lessonId && !x.IsDeleted, ct);
        var resourceRows = await resources.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.LessonId == lessonId && !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Take(1001)
            .Select(x => new ResourceEvidence(x.Id, x.Title, x.ExternalUrl, x.FileAssetId, x.DisplayOrder))
            .ToListAsync(ct);
        if (resourceRows.Count > 1000)
            throw new InvalidOperationException("Lesson revision exceeds the 1000-resource safety limit.");
        var latest = await revisions.GetQueryable().Where(x =>
            x.TenantId == tenant && x.LessonId == lessonId)
            .Select(x => (int?)x.RevisionNo).MaxAsync(ct) ?? 0;
        await revisions.AddAsync(new LmsLessonRevision
        {
            TenantId = tenant, LessonId = lessonId, CourseId = lesson.CourseId,
            RevisionNo = checked(latest + 1), ChangeKind = kind,
            Title = lesson.Title, Content = lesson.Content, ContentUrl = lesson.ContentUrl,
            CourseSectionId = lesson.CourseSectionId, DisplayOrder = lesson.DisplayOrder,
            IsPublished = lesson.IsPublished,
            ResourceSnapshotJson = JsonSerializer.Serialize(resourceRows),
            CapturedAt = now, CapturedByUserId = actor, CreatedAt = now, CreatedBy = actor
        });
        await revisions.UnitOfWork.SaveChangesAsync(ct);
    }

    public static IReadOnlyList<EduOS.Core.DTOs.LMS.LmsRevisionResourceDto> PublicResources(string json)
    {
        var rows = JsonSerializer.Deserialize<List<ResourceEvidence>>(json) ?? [];
        return rows.Select(x => new EduOS.Core.DTOs.LMS.LmsRevisionResourceDto
        {
            Title = x.Title, ExternalUrl = x.ExternalUrl,
            HasPrivateFile = x.FileAssetId.HasValue, DisplayOrder = x.DisplayOrder
        }).ToList();
    }
}
