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
            .UseSqlServer("Server=localhost;Database=EduOS_ModelTest;User Id=unused;Password=unused;TrustServerCertificate=True")
            .Options);
        var entity = context.Model.FindEntityType(typeof(AdmissionApplicant));

        entity.Should().NotBeNull();
        entity!.GetQueryFilter().Should().NotBeNull();
        // DateOnly is mapped to SQL Server 'date' by provider convention, so a
        // hand-authored ColumnType annotation need not exist in the EF model.
        var birthDate = entity.FindProperty(nameof(AdmissionApplicant.DateOfBirth))!;
        birthDate.ClrType.Should().Be(typeof(DateOnly?));
        birthDate.GetColumnType().Should().Be("date");
        entity.FindProperty(nameof(AdmissionApplicant.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        entity.GetForeignKeys().Should().HaveCount(6);
        entity.GetForeignKeys().Should().OnlyContain(x => x.DeleteBehavior == DeleteBehavior.Restrict);
        var clientRequestIndex = new[] { nameof(AdmissionApplicant.TenantId), nameof(AdmissionApplicant.ClientRequestId) };
        var applicationNumberIndex = new[] { nameof(AdmissionApplicant.TenantId), nameof(AdmissionApplicant.ApplicationNumber) };
        entity.GetIndexes().Any(x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(clientRequestIndex)).Should().BeTrue();
        entity.GetIndexes().Any(x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(applicationNumberIndex)).Should().BeTrue();
    }
}
