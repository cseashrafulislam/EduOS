using EduOS.Core.Entities.Academic;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace EduOS.Tests.Persistence;

public class AcademicInstructionModelTests
{
    [Fact]
    public void Instruction_models_preserve_legacy_links_and_add_canonical_integrity_guards()
    {
        using var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase($"instruction-model-{Guid.NewGuid():N}").Options);
        var substitution = context.Model.FindEntityType(typeof(Substitution))!;
        var lesson = context.Model.FindEntityType(typeof(LessonPlan))!;

        substitution.FindProperty(nameof(Substitution.ClassId))!.IsNullable.Should().BeTrue();
        lesson.FindProperty(nameof(LessonPlan.ClassId))!.IsNullable.Should().BeTrue();
        substitution.FindProperty(nameof(Substitution.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        lesson.FindProperty(nameof(LessonPlan.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        substitution.GetIndexes().Single(x => x.GetDatabaseName() == "UX_Substitutions_Tenant_Routine_Date").IsUnique.Should().BeTrue();
        substitution.GetIndexes().Single(x => x.GetDatabaseName() == "UX_Substitutions_Tenant_Substitute_Date_Slot").IsUnique.Should().BeTrue();
        lesson.GetIndexes().Single(x => x.GetDatabaseName() == "UX_LessonPlans_Tenant_Request").IsUnique.Should().BeTrue();
        lesson.GetIndexes().Single(x => x.GetDatabaseName() == "UX_LessonPlans_Tenant_NaturalKey").IsUnique.Should().BeTrue();
        substitution.GetCheckConstraints().Should().ContainSingle(x => x.Name == "CK_Substitutions_CancellationState");
        lesson.GetCheckConstraints().Should().Contain(x => x.Name == "CK_LessonPlans_ProgressPercent");
        lesson.GetCheckConstraints().Should().Contain(x => x.Name == "CK_LessonPlans_CanonicalState");
        substitution.GetForeignKeys().Should().OnlyContain(x => x.DeleteBehavior == DeleteBehavior.Restrict);
        lesson.GetForeignKeys().Should().OnlyContain(x => x.DeleteBehavior == DeleteBehavior.Restrict);
    }
}
