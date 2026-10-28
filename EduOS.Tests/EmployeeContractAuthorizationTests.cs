using EduOS.App.Controllers.Api;
using EduOS.App.Authorization;
using EduOS.Core.Entities.HR;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests;

public sealed class EmployeeContractAuthorizationTests
{
    [Fact]
    public void Contracts_have_tenant_admin_HR_and_csrf_boundaries()
    {
        var type = typeof(EmployeeContractsApiController);
        Assert.Contains(type.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x => x.Roles == "TenantAdmin");
        Assert.Contains(type.GetCustomAttributes(typeof(RequireModuleAttribute), true)
            .Cast<RequireModuleAttribute>(), x => x.Policy == RequireModuleAttribute.PolicyPrefix + "HR");
        Assert.NotEmpty(type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
    }

    [Fact]
    public void Replay_and_contract_codes_are_tenant_unique()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(
            "Server=(local);Database=EduOSModel;Trusted_Connection=True;TrustServerCertificate=True").Options;
        using var db = new EduOSDbContext(options);
        var entity = db.Model.FindEntityType(typeof(EmployeeContract));
        Assert.NotNull(entity);
        foreach(var name in new[] { nameof(EmployeeContract.ClientRequestId), nameof(EmployeeContract.ContractCode) })
            Assert.Contains(entity!.GetIndexes(), x => x.IsUnique &&
                x.Properties.Select(p => p.Name).SequenceEqual(new [] { "TenantId", name }));
    }
}
