using EduOS.Core.Entities.Payroll;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests;

public sealed class CampusPayrollIndexTests
{
    [Fact]
    public void Campus_and_global_payroll_periods_keep_distinct_unique_indexes()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer("Server=(local);Database=EduOSTestModel;Trusted_Connection=True;TrustServerCertificate=True").Options;
        using var db = new EduOSDbContext(options);
        var entity = db.Model.FindEntityType(typeof(PayrollRun));
        Assert.NotNull(entity);
        var index = entity!.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(
                new[] { "TenantId", "Year", "Month", "CampusId" }));
        Assert.True(index.IsUnique);
        Assert.Equal("[CampusId] IS NOT NULL AND [State] <> 5 AND [IsDeleted] = 0", index.GetFilter());
        var global = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "Year", "Month" }));
        Assert.True(global.IsUnique);
        Assert.Equal("[CampusId] IS NULL AND [State] <> 5 AND [IsDeleted] = 0", global.GetFilter());
    }
}
