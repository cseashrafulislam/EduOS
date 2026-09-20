using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace EduOS.Tests;

public sealed class OperationalModuleReadAuthorizationContractTests
{
    [Theory]
    [InlineData(typeof(LibraryController), nameof(LibraryController.Index))]
    [InlineData(typeof(TransportController), nameof(TransportController.Index))]
    public void Operational_module_reads_inherit_authenticated_controller_boundary(Type controllerType, string actionName)
    {
        Assert.NotEmpty(controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true));
        var method = controllerType.GetMethods().Single(x => x.Name == actionName);
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }
}
