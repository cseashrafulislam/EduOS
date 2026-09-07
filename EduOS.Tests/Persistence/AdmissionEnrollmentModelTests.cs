using EduOS.Core.Entities.Students;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace EduOS.Tests.Persistence;

public class AdmissionEnrollmentModelTests
{
    [Fact]
    public void Admission_conversion_models_have_tenant_filters_concurrency_and_unique_business_keys()
    {
        using var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"admission-enrollment-model-{Guid.NewGuid():N}").Options);
        var student = context.Model.FindEntityType(typeof(Student))!;
        var guardian = context.Model.FindEntityType(typeof(Guardian))!;
        var enrollment = context.Model.FindEntityType(typeof(Enrollment))!;

        student.GetQueryFilter().Should().NotBeNull();
        guardian.GetQueryFilter().Should().NotBeNull();
        enrollment.GetQueryFilter().Should().NotBeNull();
        student.FindProperty(nameof(Student.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        guardian.FindProperty(nameof(Guardian.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        enrollment.FindProperty(nameof(Enrollment.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        student.GetIndexes().Should().Contain(x => x.IsUnique && x.Properties.Any(p => p.Name == nameof(Student.AdmissionApplicationId)));
        enrollment.GetIndexes().Should().Contain(x => x.IsUnique && x.Properties.Any(p => p.Name == nameof(Enrollment.StudentId)));
    }
}
