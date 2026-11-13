using EduOS.Core.Entities.Academic;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Academic;

/// <summary>
/// One authoritative section-placement validation shared by admission,
/// promotion, readmission and import. Auto-select only a single active section;
/// never guess when multiple choices exist.
/// </summary>
public static class AcademicSectionPlacementPolicy
{
    public static async Task<(long? SectionId, int Capacity, string? Error)> ResolveAsync(
        IQueryable<AcademicSection> sections, long tenantId, long campusId,
        long academicLevelId, long? requestedSectionId, CancellationToken ct = default)
    {
        if (tenantId <= 0 || campusId <= 0 || academicLevelId <= 0 || requestedSectionId is <= 0)
            return (null, 0, "Invalid tenant, campus, class or section.");
        var scoped = sections.AsNoTracking().Where(x =>
            x.TenantId == tenantId && x.CampusId == campusId && x.AcademicLevelId == academicLevelId &&
            x.IsActive && !x.IsDeleted);
        if (requestedSectionId.HasValue)
        {
            var selected = await scoped.Where(x => x.Id == requestedSectionId.Value)
                .Select(x => new { x.Id, x.Capacity }).FirstOrDefaultAsync(ct);
            if (selected == null)
                return (null, 0, "Selected section does not belong to this campus and class.");
            if (selected.Capacity <= 0)
                return (null, 0, "Selected section has no capacity.");
            return (selected.Id, selected.Capacity, null);
        }
        var choices = await scoped.OrderBy(x => x.Id).Take(2)
            .Select(x => new { x.Id, x.Capacity }).ToListAsync(ct);
        if (choices.Count == 0)
            return (null, 0, "Create an academic section for this class before assigning students.");
        if (choices.Count > 1)
            return (null, 0, "Select a specific academic section for this class.");
        if (choices[0].Capacity <= 0)
            return (null, 0, "The only active section has no capacity.");
        return (choices[0].Id, choices[0].Capacity, null);
    }

    public static async Task<bool> CapacityAvailableAsync(
        IQueryable<StudentEnrollment> enrollments, long tenantId,
        long sectionId, int capacity, CancellationToken ct = default) =>
        capacity > 0 && await enrollments.AsNoTracking().CountAsync(x =>
            x.TenantId == tenantId && x.AcademicSectionId == sectionId &&
            x.IsCurrent && x.State == EduOS.Core.Enums.Domain.EnrollmentState.Active &&
            !x.IsDeleted, ct) < capacity;
}
