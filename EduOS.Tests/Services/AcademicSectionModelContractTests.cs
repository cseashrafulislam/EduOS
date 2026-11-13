using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Finance;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AcademicSectionModelContractTests
{
    [Fact]
    public void Academic_batch_and_academic_section_are_distinct_tenant_scoped_relational_entities()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer("Server=(local);Database=EduOS_Model_Contract;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new EduOSDbContext(options);
        var batches = db.Model.FindEntityType(typeof(AcademicBatch));
        var sections = db.Model.FindEntityType(typeof(AcademicSection));
        Assert.NotNull(batches);
        Assert.NotNull(sections);
        Assert.Equal("AcademicBatches", batches!.GetTableName());
        Assert.Equal("AcademicSections", sections!.GetTableName());
        Assert.Null(sections.FindProperty("AcademicBatchId"));
        foreach (var parent in new[] {
            (Type: typeof(EduOS.Core.Entities.SaaS.Campus), Key: "CampusId"),
            (Type: typeof(AcademicLevel), Key: "AcademicLevelId")
        })
            Assert.Contains(sections.GetForeignKeys(), fk =>
                fk.PrincipalEntityType.ClrType == parent.Type &&
                fk.Properties.Select(x => x.Name).SequenceEqual(new[] { "TenantId", parent.Key }));
        Assert.Contains(sections.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(x => x.Name).SequenceEqual(
                new[] { "TenantId", "CampusId", "AcademicLevelId", "Code" }));
        Assert.True(sections.FindProperty("RowVersion")!.IsConcurrencyToken);
        foreach (var entity in new[]
        {
            typeof(StudentEnrollment), typeof(SubjectOffering),
            typeof(AttendanceSession), typeof(ResultPublication), typeof(FeeStructure)
        })
        {
            var model = db.Model.FindEntityType(entity);
            Assert.NotNull(model);
            Assert.NotNull(model!.FindProperty("AcademicBatchId"));
            Assert.NotNull(model.FindProperty("AcademicSectionId"));
            Assert.Contains(model.GetForeignKeys(), fk =>
                fk.PrincipalEntityType.ClrType == typeof(AcademicSection) &&
                fk.Properties.Any(p => p.Name == "TenantId") &&
                fk.Properties.Any(p => p.Name == "AcademicSectionId"));
        }
    }
}
