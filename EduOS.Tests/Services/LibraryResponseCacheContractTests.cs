using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LibraryResponseCacheContractTests
{
    [Fact]
    public void Library_api_disables_response_caching_for_tenant_data()
    {
        var cache = Assert.Single(typeof(LibraryController)
            .GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Cast<ResponseCacheAttribute>());
        Assert.True(cache.NoStore);
        Assert.Equal(ResponseCacheLocation.None, cache.Location);
    }
}
