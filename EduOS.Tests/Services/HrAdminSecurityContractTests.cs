using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.HR;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HrAdminSecurityContractTests
{
    [Fact]
    public void Hr_service_implements_all_twelve_canonical_contract_members()
    {
        var contract = typeof(IHrAdminService);
        contract.GetMethods().Should().HaveCount(12);
        typeof(HrAdminService).GetInterfaces().Should().Contain(contract);
        contract.GetMethod(nameof(IHrAdminService.SaveBankAccountAsync))!
            .GetParameters()[1].ParameterType.Should().Be(typeof(SaveEmployeeBankAccountRequestDto));
        contract.GetMethod(nameof(IHrAdminService.ReviewEmployeeLeaveAsync))!
            .GetParameters()[0].ParameterType.Should().Be(typeof(ReviewEmployeeLeaveDto));
    }

    [Fact]
    public void Hr_api_enforces_roles_module_antiforgery_and_rate_limit()
    {
        var type = typeof(HrAdminController);
        var authorize = type.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single();
        authorize.Roles.Should().Be("TenantAdmin,Principal,HR");
        type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true).Should().NotBeEmpty();
        type.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Should().NotBeEmpty();
        type.GetCustomAttributes(typeof(ResponseCacheAttribute), true).Should().NotBeEmpty();
        foreach (var method in type.GetMethods().Where(x =>
            x.GetCustomAttributes(typeof(HttpPostAttribute), true).Length > 0 ||
            x.GetCustomAttributes(typeof(HttpPutAttribute), true).Length > 0))
            method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Should().BeEmpty();
    }

    [Fact]
    public void Hr_service_uses_tenant_scope_transactions_and_protected_account_numbers()
    {
        var source = File.ReadAllText(FindFile(
            "EduOS.Service", "Services", "HR", "HrAdminService.cs"));
        source.Should().Contain("ExecuteInTransactionAsync");
        source.Should().Contain("x.TenantId == _user.TenantId");
        source.Should().Contain("ProtectedAccountNumber = protector.Protect(number)");
        source.Should().Contain("Matches(row.RowVersion, version)");
        source.Should().Contain("Take(request.PageSize)");
        source.Should().NotContain("ProtectedAccountNumber = request.AccountNumber");
    }

    private static string FindFile(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var file = Path.Combine(new[] { directory.FullName }.Concat(path).ToArray());
            if (File.Exists(file)) return file;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(path));
    }
}
