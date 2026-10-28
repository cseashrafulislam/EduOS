using EduOS.Core.Entities.Payroll;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests;

public sealed class PayrollPeriodDatabaseInvariantTests
{
    [Fact]
    public void Non_cancelled_institution_payroll_periods_are_database_unique()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer("Server=(local);Database=EduOSTestModel;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new EduOSDbContext(options);
        var entity = db.Model.FindEntityType(typeof(PayrollRun));
        Assert.NotNull(entity);
        var index = entity!.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] {
                nameof(PayrollRun.TenantId), nameof(PayrollRun.Year), nameof(PayrollRun.Month)
            }));
        Assert.True(index.IsUnique);
        Assert.Equal("[CampusId] IS NULL AND [State] <> 5 AND [IsDeleted] = 0", index.GetFilter());
    }
}
