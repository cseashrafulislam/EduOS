using EduOS.Core.Entities.Academic;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Persistence;

public class AcademicSetupModelTests
{
    [Fact]
    public void Canonical_subjects_use_tenant_scoped_codes_not_legacy_class_links()
    {
        using var context = CreateContext();
        var subject = context.Model.FindEntityType(typeof(Subject))!;
        subject.FindProperty("ClassId").Should().BeNull();
        subject.GetForeignKeys().Should().NotContain(x => x.PrincipalEntityType.ClrType.Name == "Class");
        AssertUniqueIndex<Subject>(context, nameof(Subject.TenantId), nameof(Subject.Code));
    }

    [Fact]
    public void Academic_setup_natural_keys_and_current_curriculum_are_database_enforced()
    {
        using var context = CreateContext();
        AssertUniqueIndex<AcademicProgram>(context, nameof(AcademicProgram.TenantId), nameof(AcademicProgram.Code));
        AssertUniqueIndex<AcademicLevel>(context, nameof(AcademicLevel.TenantId), nameof(AcademicLevel.AcademicProgramId), nameof(AcademicLevel.Code));
        AssertUniqueIndex<AcademicCurriculum>(context, nameof(AcademicCurriculum.TenantId), nameof(AcademicCurriculum.Code));
        AssertUniqueIndex<CurriculumSubject>(context, nameof(CurriculumSubject.TenantId), nameof(CurriculumSubject.AcademicCurriculumId), nameof(CurriculumSubject.AcademicLevelId), nameof(CurriculumSubject.SubjectId));
        AssertUniqueIndex<AcademicBatch>(context, nameof(AcademicBatch.TenantId), nameof(AcademicBatch.CampusId), nameof(AcademicBatch.AcademicYearId), nameof(AcademicBatch.Code));
        AssertUniqueIndex<Room>(context, nameof(Room.TenantId), nameof(Room.CampusId), nameof(Room.Code));
        AssertUniqueIndex<ProgramCampus>(context, nameof(ProgramCampus.TenantId), nameof(ProgramCampus.AcademicProgramId), nameof(ProgramCampus.CampusId));
        var curriculum = context.Model.FindEntityType(typeof(AcademicCurriculum))!;
        curriculum.GetIndexes().Should().Contain(x => x.IsUnique && x.GetFilter() != null && x.GetFilter()!.Contains("IsCurrent") &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(AcademicCurriculum.TenantId), nameof(AcademicCurriculum.AcademicProgramId), nameof(AcademicCurriculum.AcademicTrackId), nameof(AcademicCurriculum.MediumId) }));
    }

    private static EduOSDbContext CreateContext() => new(new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase($"academic-setup-model-{Guid.NewGuid():N}").Options);

    private static void AssertUniqueIndex<TEntity>(EduOSDbContext context, params string[] names) where TEntity : class =>
        context.Model.FindEntityType(typeof(TEntity))!.GetIndexes().Should().Contain(x =>
            x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(names));
}
