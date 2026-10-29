using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.HR;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class EmployeeBankAccountWorkflowTests
{
    [Fact]
    public void Bank_account_endpoints_and_workspace_require_tenant_admin()
    {
        foreach (var name in new[] { nameof(HrAdminController.BankAccounts), nameof(HrAdminController.SaveBankAccount) })
        {
            var action = typeof(HrAdminController).GetMethod(name);
            action.Should().NotBeNull();
            action!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>().Should().Contain(x => x.Roles == "TenantAdmin");
        }
        typeof(HRController).GetMethod(nameof(HRController.BankAccounts))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Should().Contain(x => x.Roles == "TenantAdmin");
    }

    [Theory]
    [InlineData("HR")]
    [InlineData("Principal")]
    public async Task Non_admin_cannot_read_protected_accounts(string role)
    {
        var response = await Service(role).GetBankAccountsAsync(Guid.NewGuid());
        response.StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData("HR")]
    [InlineData("Principal")]
    public async Task Non_admin_cannot_change_protected_accounts(string role)
    {
        var response = await Service(role).SaveBankAccountAsync(null, new SaveEmployeeBankAccountRequestDto
        {
            EmployeeReference = Guid.NewGuid(), BankName = "Bank", AccountName = "Employee",
            AccountNumber = "12345678", CurrencyCode = "BDT"
        });
        response.StatusCode.Should().Be(403);
    }

    [Fact]
    public void Read_contract_never_exposes_full_or_protected_account_number()
    {
        var names = typeof(EmployeeBankAccountDto).GetProperties().Select(x => x.Name).ToArray();
        names.Should().Contain(nameof(EmployeeBankAccountDto.AccountNumberLast4));
        names.Should().NotContain("AccountNumber");
        names.Should().NotContain("ProtectedAccountNumber");
    }

    private static HrAdminService Service(string role) => new(
        null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        null!, null!, null!, null!, null!, null!, null!, null!, new TestUser(role),
        TimeProvider.System, null!, NullLogger<HrAdminService>.Instance, null!);

    private sealed class TestUser(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
