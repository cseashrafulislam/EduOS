using EduOS.Core.Entities.SaaS;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Persistence;

public sealed class TenantDomainPrimaryModelTests
{
    [Fact]
    public void Tenant_domain_mapping_limits_active_primary_to_one_per_tenant()
    {
        using var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"domain-primary-{Guid.NewGuid():N}").Options);
        var mapping = context.Model.FindEntityType(typeof(TenantDomain));
        Assert.NotNull(mapping);
        var primaryIndex = Assert.Single(mapping!.GetIndexes().Where(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(TenantDomain.TenantId) })
            && index.GetDatabaseName() == "UX_TenantDomains_OneActivePrimary"));
        Assert.Equal("[IsDeleted] = 0 AND [IsActive] = 1 AND [IsPrimary] = 1", primaryIndex.GetFilter());
    }
}
