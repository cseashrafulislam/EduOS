using EduOS.App.Controllers.Api;
using EduOS.App.Extensions;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.SaaS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class UsageHistoryDependencyInjectionTests
{
    [Fact]
    public void Usage_history_uses_canonical_scoped_service_registration()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices(new ConfigurationBuilder().Build());
        var registration = Assert.Single(services.Where(x => x.ServiceType == typeof(IUsageHistoryService)));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.Equal(typeof(UsageHistoryService), registration.ImplementationType);
    }

    [Fact]
    public void Usage_history_api_requires_registered_service()
    {
        var constructor = Assert.Single(typeof(UsageHistoryApiController).GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters());
        Assert.Equal(typeof(IUsageHistoryService), parameter.ParameterType);
    }
}
