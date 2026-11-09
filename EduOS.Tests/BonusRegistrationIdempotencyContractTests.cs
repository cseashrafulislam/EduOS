using EduOS.Core.Entities.Payroll;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests;

public sealed class BonusRegistrationIdempotencyContractTests
{
    [Fact]
    public void Bonus_request_key_preserves_history_and_is_unique_per_tenant()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer("Server=(local);Database=EduOSTestModel;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new EduOSDbContext(options);
        var entity = db.Model.FindEntityType(typeof(Bonus));
        Assert.NotNull(entity);
        var requestProperty = entity!.FindProperty(nameof(Bonus.ClientRequestId));
        Assert.NotNull(requestProperty);
        Assert.True(requestProperty!.IsNullable);
        var index = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(
                new[] { nameof(Bonus.TenantId), nameof(Bonus.ClientRequestId) }));
        Assert.True(index.IsUnique);
        Assert.Equal("[ClientRequestId] IS NOT NULL", index.GetFilter());
    }
}
