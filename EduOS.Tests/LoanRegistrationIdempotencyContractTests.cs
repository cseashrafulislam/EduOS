using EduOS.Core.Entities.Payroll;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests;

public sealed class LoanRegistrationIdempotencyContractTests
{
    [Fact]
    public void Loan_request_key_is_nullable_for_history_but_unique_per_tenant_for_new_commands()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer("Server=(local);Database=EduOSTestModel;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new EduOSDbContext(options);
        var entity = db.Model.FindEntityType(typeof(LoanAdvance));
        Assert.NotNull(entity);
        var requestProperty = entity!.FindProperty(nameof(LoanAdvance.ClientRequestId));
        Assert.NotNull(requestProperty);
        Assert.True(requestProperty!.IsNullable);
        var index = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(
                new[] { nameof(LoanAdvance.TenantId), nameof(LoanAdvance.ClientRequestId) }));
        Assert.True(index.IsUnique);
        Assert.Equal("[ClientRequestId] IS NOT NULL", index.GetFilter());
    }
}
