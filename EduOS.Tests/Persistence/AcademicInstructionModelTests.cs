using EduOS.Core.Entities.Academic;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Persistence;

public class AcademicInstructionModelTests
{
    [Fact]
    public void Instruction_models_reference_canonical_routines_and_subject_offerings()
    {
        using var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"instruction-model-{Guid.NewGuid():N}").Options);
        var substitution = context.Model.FindEntityType(typeof(Substitution))!;
        var lesson = context.Model.FindEntityType(typeof(LessonPlan))!;

        substitution.GetQueryFilter().Should().NotBeNull();
        lesson.GetQueryFilter().Should().NotBeNull();
        substitution.FindProperty("ClassId").Should().BeNull();
        lesson.FindProperty("ClassId").Should().BeNull();
        substitution.FindProperty(nameof(Substitution.RoutineEntryId))!.ClrType.Should().Be(typeof(long));
        substitution.FindProperty(nameof(Substitution.SubstituteEmployeeId))!.ClrType.Should().Be(typeof(long));
        lesson.FindProperty(nameof(LessonPlan.SubjectOfferingId))!.ClrType.Should().Be(typeof(long));
        lesson.FindProperty(nameof(LessonPlan.EmployeeId))!.ClrType.Should().Be(typeof(long));
        substitution.FindProperty(nameof(Substitution.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        lesson.FindProperty(nameof(LessonPlan.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        substitution.GetIndexes().Should().Contain(x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Substitution.TenantId), nameof(Substitution.ClientRequestId) }));
    }
}
