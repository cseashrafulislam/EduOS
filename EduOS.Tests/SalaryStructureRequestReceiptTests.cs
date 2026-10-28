using EduOS.Core.Entities.Payroll;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests;

public sealed class SalaryStructureRequestReceiptTests
{
    [Fact]
    public void Salary_structure_request_key_is_nullable_for_history_and_unique_for_new_commands()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer("Server=(local);Database=EduOSTestModel;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new EduOSDbContext(options);
        var entity = db.Model.FindEntityType(typeof(SalaryStructure));
        Assert.NotNull(entity);
        Assert.True(entity!.FindProperty(nameof(SalaryStructure.ClientRequestId))!.IsNullable);
        Assert.Equal(64, entity.FindProperty(nameof(SalaryStructure.RequestFingerprint))!.GetMaxLength());
        var index = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
            { nameof(SalaryStructure.TenantId), nameof(SalaryStructure.ClientRequestId) }));
        Assert.True(index.IsUnique);
        Assert.Equal("[ClientRequestId] IS NOT NULL", index.GetFilter());
    }
}
