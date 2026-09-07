using EduOS.Core.Entities.Admission;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace EduOS.Tests.Persistence;

public class AdmissionApplicantModelTests
{
    [Fact]
    public void Admission_applicant_model_has_tenant_keys_restrictive_relations_and_concurrency()
    {
        using var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"admission-model-{Guid.NewGuid():N}")
            .Options);
        var entity = context.Model.FindEntityType(typeof(AdmissionApplicant));

        entity.Should().NotBeNull();
        entity!.GetQueryFilter().Should().NotBeNull();
        entity.FindProperty(nameof(AdmissionApplicant.DateOfBirth))!.GetColumnType().Should().Be("date");
        entity.FindProperty(nameof(AdmissionApplicant.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        entity.GetForeignKeys().Should().HaveCount(5);
        entity.GetForeignKeys().Should().OnlyContain(x => x.DeleteBehavior == DeleteBehavior.Restrict);
        var clientRequestIndex = new[] { nameof(AdmissionApplicant.TenantId), nameof(AdmissionApplicant.ClientRequestId) };
        var applicationNumberIndex = new[] { nameof(AdmissionApplicant.TenantId), nameof(AdmissionApplicant.ApplicationNumber) };
        entity.GetIndexes().Any(x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(clientRequestIndex)).Should().BeTrue();
        entity.GetIndexes().Any(x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(applicationNumberIndex)).Should().BeTrue();
    }
}
