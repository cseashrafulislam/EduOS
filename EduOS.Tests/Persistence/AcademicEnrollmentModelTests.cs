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

        enrollment.GetDeclaredQueryFilters().Should().NotBeEmpty();
        registration.GetDeclaredQueryFilters().Should().NotBeEmpty();
        enrollment.FindProperty(nameof(StudentEnrollment.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        registration.FindProperty(nameof(StudentSubjectRegistration.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        AssertUniqueIndex(enrollment, "TenantId", "ClientRequestId");
        AssertUniqueIndex(enrollment, "TenantId", "StudentId");
        AssertUniqueIndex(enrollment, "TenantId", "AcademicBatchId", "RollNo");
        AssertUniqueIndex(registration, "TenantId", "ClientRequestId");
        AssertUniqueIndex(registration, "TenantId", "StudentEnrollmentId", "SubjectOfferingId");
        enrollment.GetForeignKeys().Should().OnlyContain(x => x.DeleteBehavior == DeleteBehavior.Restrict);
        registration.GetForeignKeys().Should().OnlyContain(x => x.DeleteBehavior == DeleteBehavior.Restrict);
    }

    private static void AssertUniqueIndex(Microsoft.EntityFrameworkCore.Metadata.IEntityType entity,
        params string[] properties) =>
        entity.GetIndexes().Should().ContainSingle(x =>
            x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(properties),
            "the actual tenant-scoped database key must remain unique regardless of generated index naming");
}
