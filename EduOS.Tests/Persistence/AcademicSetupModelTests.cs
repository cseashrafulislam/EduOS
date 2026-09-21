using EduOS.Core.Entities.Academic;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Persistence;

public class AcademicSetupModelTests
{
    [Fact]
    public void Canonical_subjects_can_exist_without_a_legacy_class()
    {
        using var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"academic-setup-model-{Guid.NewGuid():N}").Options);
        var subject = context.Model.FindEntityType(typeof(Subject))!;

        subject.FindProperty(nameof(Subject.ClassId))!.ClrType.Should().Be(typeof(long?));
        subject.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(Class)).IsRequired.Should().BeFalse();
        var canonicalCode = subject.GetIndexes().Single(x => x.GetDatabaseName() == "UX_Subjects_Tenant_CanonicalCode");
        canonicalCode.IsUnique.Should().BeTrue();
        canonicalCode.GetFilter().Should().Contain("ClassId");
    }

    [Fact]
    public void Academic_setup_natural_keys_and_current_curriculum_are_database_enforced()
    {
        using var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"academic-setup-indexes-{Guid.NewGuid():N}").Options);

        AssertUniqueIndex<AcademicProgram>(context, "UX_AcademicPrograms_Tenant_Code");
        AssertUniqueIndex<AcademicLevel>(context, "UX_AcademicLevels_Tenant_Program_Code");
        AssertUniqueIndex<AcademicCurriculum>(context, "UX_AcademicCurriculums_Tenant_Code");
        AssertUniqueIndex<AcademicCurriculum>(context, "UX_AcademicCurriculums_Tenant_CurrentProgram");
        AssertUniqueIndex<CurriculumSubject>(context, "UX_CurriculumSubjects_Tenant_Scope");
        AssertUniqueIndex<AcademicBatch>(context, "UX_AcademicBatches_Tenant_Year_Code");
        AssertUniqueIndex<Room>(context, "UX_Rooms_Tenant_Code");
        AssertUniqueIndex<ProgramCampus>(context, "UX_ProgramCampuses_Tenant_Program_Campus");
    }

    private static void AssertUniqueIndex<TEntity>(EduOSDbContext context, string name) where TEntity : class =>
        context.Model.FindEntityType(typeof(TEntity))!.GetIndexes().Single(x => x.GetDatabaseName() == name).IsUnique.Should().BeTrue();
}
