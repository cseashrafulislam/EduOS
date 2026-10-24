using EduOS.Core.Entities.Academic;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Academic;

/// <summary>Tenant/campus-scoped prerequisite evaluation shared by write and read workflows.</summary>
internal sealed class SubjectPrerequisiteEligibility(
    IGenericRepository<SubjectPrerequisite> prerequisites,
    IGenericRepository<StudentEnrollment> enrollments,
    IGenericRepository<StudentSubjectRegistration> registrations,
    IGenericRepository<SubjectOffering> offerings,
    IGenericRepository<CurriculumSubject> mappings,
    IGenericRepository<Subject> subjects)
{
    public async Task<IReadOnlyDictionary<long, IReadOnlyList<string>>> MissingAsync(
        long tenant, long student, long campus, long curriculum,
        IReadOnlyCollection<long> subjectsToCheck, DateOnly asOf, CancellationToken ct)
    {
        var ids = subjectsToCheck.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<long, IReadOnlyList<string>>();
        var rules = await prerequisites.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.IsMandatory &&
            x.AcademicCurriculumId == curriculum && ids.Contains(x.SubjectId))
            .Select(x => new { x.SubjectId, x.PrerequisiteSubjectId }).ToListAsync(ct);
        if (rules.Count == 0) return new Dictionary<long, IReadOnlyList<string>>();
        var required = rules.Select(x => x.PrerequisiteSubjectId).Distinct().ToArray();
        var cutoff = asOf.ToDateTime(new TimeOnly(23, 59, 59));
        var completed = await (from registration in registrations.GetQueryable().AsNoTracking()
            join enrollment in enrollments.GetQueryable().AsNoTracking()
                on registration.StudentEnrollmentId equals enrollment.Id
            join offering in offerings.GetQueryable().AsNoTracking()
                on registration.SubjectOfferingId equals offering.Id
            join mapping in mappings.GetQueryable().AsNoTracking()
                on offering.CurriculumSubjectId equals mapping.Id
            where registration.TenantId == tenant && enrollment.TenantId == tenant &&
                offering.TenantId == tenant && mapping.TenantId == tenant &&
                !registration.IsDeleted && !enrollment.IsDeleted && !offering.IsDeleted && !mapping.IsDeleted &&
                enrollment.StudentId == student && enrollment.CampusId == campus &&
                enrollment.EnrollmentDate <= asOf && registration.RegisteredAt <= cutoff &&
                registration.State == SubjectRegistrationState.Completed && required.Contains(mapping.SubjectId)
            select mapping.SubjectId).Distinct().ToListAsync(ct);
        var done = completed.ToHashSet();
        var outstanding = required.Where(x => !done.Contains(x)).ToArray();
        if (outstanding.Length == 0) return new Dictionary<long, IReadOnlyList<string>>();
        var names = await subjects.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && outstanding.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Code + " - " + x.Name, ct);
        return rules.Where(x => !done.Contains(x.PrerequisiteSubjectId))
            .GroupBy(x => x.SubjectId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x
                .Select(y => names.GetValueOrDefault(y.PrerequisiteSubjectId, "Subject #" + y.PrerequisiteSubjectId))
                .Distinct().OrderBy(y => y).ToArray());
    }
}
