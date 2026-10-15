using EduOS.Core.Enums.Domain;
using EduOS.Service.Services.Platform;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PlatformTenantLifecycleServiceTests
{
    [Theory]
    [InlineData(TenantState.Active, TenantState.Suspended, true)]
    [InlineData(TenantState.Suspended, TenantState.Active, true)]
    [InlineData(TenantState.PendingVerification, TenantState.Active, false)]
    [InlineData(TenantState.Closed, TenantState.Active, false)]
    [InlineData(TenantState.Active, TenantState.Active, false)]
    public void Tenant_transition_policy(TenantState current, TenantState target, bool expected)
        => Assert.Equal(expected, PlatformTenantLifecycleService.IsAllowedTransition(current, target));
}
