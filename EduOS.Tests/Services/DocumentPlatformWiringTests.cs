using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using EduOS.App.Extensions;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class DocumentPlatformWiringTests
{
    [Fact]
    public void Document_workflows_are_registered_once_with_scoped_lifetime()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices(new ConfigurationBuilder().Build());
        AssertRegistration<IDocumentService, DocumentService>(services);
        AssertRegistration<IDocumentFileService, DocumentFileService>(services);
        AssertRegistration<IDocumentVerificationService, DocumentVerificationService>(services);
    }

    [Theory]
    [InlineData(typeof(DocumentAdministrationController))]
    [InlineData(typeof(DocumentAdministrationApiController))]
    [InlineData(typeof(DocumentFilesApiController))]
    public void Document_controllers_require_tenant_admin(Type controller)
    {
        var attributes = controller.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().ToArray();
        Assert.Contains(attributes, x => x.Roles == "TenantAdmin");
    }

    [Theory]
    [InlineData(typeof(DocumentAdministrationApiController))]
    [InlineData(typeof(DocumentFilesApiController))]
    public void Document_write_apis_validate_antiforgery(Type controller) =>
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));

    private static void AssertRegistration<TContract, TImplementation>(IServiceCollection services)
    {
        var registrations = services.Where(x => x.ServiceType == typeof(TContract)).ToArray();
        var registration = Assert.Single(registrations);
        Assert.Equal(typeof(TImplementation), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
    }
}
