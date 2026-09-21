using EduOS.Core.Entities.Academic;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace EduOS.Tests.Persistence;

public class AcademicEnrollmentModelTests
{
    [Fact]
    public void Canonical_enrollment_models_have_tenant_filters_concurrency_and_unique_invariants()
    {
        using var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"academic-enrollment-model-{Guid.NewGuid():N}").Options);
        var enrollment = context.Model.FindEntityType(typeof(StudentEnrollment))!;
        var registration = context.Model.FindEntityType(typeof(StudentSubjectRegistration))!;

        enrollment.GetQueryFilter().Should().NotBeNull();
        registration.GetQueryFilter().Should().NotBeNull();
        enrollment.FindProperty(nameof(StudentEnrollment.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        registration.FindProperty(nameof(StudentSubjectRegistration.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        AssertUniqueIndex(enrollment, "UX_StudentEnrollments_Tenant_Request");
        AssertUniqueIndex(enrollment, "UX_StudentEnrollments_Tenant_CurrentStudent");
        AssertUniqueIndex(enrollment, "UX_StudentEnrollments_Tenant_Batch_Roll");
        AssertUniqueIndex(registration, "UX_StudentSubjectRegistrations_Tenant_Request");
        AssertUniqueIndex(registration, "UX_StudentSubjectRegistrations_Tenant_Enrollment_Subject");
        enrollment.GetForeignKeys().Should().OnlyContain(x => x.DeleteBehavior == DeleteBehavior.Restrict);
        registration.GetForeignKeys().Should().OnlyContain(x => x.DeleteBehavior == DeleteBehavior.Restrict);
    }

    private static void AssertUniqueIndex(Microsoft.EntityFrameworkCore.Metadata.IEntityType entity, string name) =>
        entity.GetIndexes().Single(x => x.GetDatabaseName() == name).IsUnique.Should().BeTrue();
}
