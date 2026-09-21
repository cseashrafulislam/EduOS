using EduOS.Core.Entities.Admission;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace EduOS.Tests.Persistence;

public sealed class AdmissionIntakeModelTests
{
    [Fact]
    public void Configurable_intake_and_document_models_have_database_integrity_guards()
    {
        using var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase($"admission-intake-model-{Guid.NewGuid():N}").Options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var form = model.FindEntityType(typeof(AdmissionIntakeForm))!;
        var document = model.FindEntityType(typeof(AdmissionApplicantDocument))!;
        var applicant = model.FindEntityType(typeof(AdmissionApplicant))!;

        applicant.FindProperty(nameof(AdmissionApplicant.AdmissionIntakeFormId))!.ClrType.Should().Be(typeof(long?));
        form.FindProperty(nameof(AdmissionIntakeForm.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        document.FindProperty(nameof(AdmissionApplicantDocument.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        form.GetIndexes().Should().Contain(x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual([nameof(AdmissionIntakeForm.TenantId), nameof(AdmissionIntakeForm.Code)]));
        document.GetIndexes().Single(x => x.GetDatabaseName() == "UX_AdmissionApplicantDocuments_CurrentType").IsUnique.Should().BeTrue();
        form.GetCheckConstraints().Should().Contain(x => x.Name == "CK_AdmissionIntakeForms_DateRange");
        document.GetCheckConstraints().Should().Contain(x => x.Name == "CK_AdmissionApplicantDocuments_FileSize");
        form.GetForeignKeys().Should().OnlyContain(x => x.DeleteBehavior == DeleteBehavior.Restrict);
        document.GetForeignKeys().Should().OnlyContain(x => x.DeleteBehavior == DeleteBehavior.Restrict);
    }
}
