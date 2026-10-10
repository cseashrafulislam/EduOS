using EduOS.Core.Entities.Accounting;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Persistence;

public sealed class CanonicalAccountHierarchyTests
{
    [Fact]
    public void Canonical_account_hierarchy_has_one_authoritative_parent_account_id()
    {
        var account = new Account { ParentAccountId = 12 };
        Assert.Equal(12L, account.ParentAccountId);
        account.ParentAccountId = 25;
        Assert.Equal(25L, account.ParentAccountId);
        account.ParentAccountId = null;
        Assert.Null(account.ParentAccountId);
        Assert.Null(typeof(Account).GetProperty("ParentId"));
    }

    [Fact]
    public void Account_mapping_exposes_a_single_parent_foreign_key()
    {
        using var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"account-hierarchy-{Guid.NewGuid():N}").Options);
        var mapping = context.Model.FindEntityType(typeof(Account));
        Assert.NotNull(mapping);
        Assert.Null(mapping!.FindProperty("ParentId"));
        Assert.NotNull(mapping.FindProperty(nameof(Account.ParentAccountId)));
        Assert.Single(mapping.GetForeignKeys().Where(fk => fk.PrincipalEntityType.ClrType == typeof(Account)));
    }
}
