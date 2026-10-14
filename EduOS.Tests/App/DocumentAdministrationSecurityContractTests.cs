using System.Reflection;
using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.App;

public sealed class DocumentAdministrationSecurityContractTests
{
    [Theory]
    [InlineData(typeof(DocumentAdministrationController))]
    [InlineData(typeof(DocumentAdministrationApiController))]
    [InlineData(typeof(DocumentFilesApiController))]
    public void DocumentEndpointsRequireTenantAdmin(Type controller)
    {
        var authorize = controller.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("TenantAdmin", authorize!.Roles);
    }

    [Theory]
    [InlineData(typeof(DocumentAdministrationApiController))]
    [InlineData(typeof(DocumentFilesApiController))]
    public void DocumentApiUsesAntiforgeryAndNoStore(Type controller)
    {
        Assert.NotNull(controller.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>());
        var cache = controller.GetCustomAttribute<ResponseCacheAttribute>();
        Assert.NotNull(cache);
        Assert.True(cache!.NoStore);
    }

    [Fact]
    public void DocumentFileUploadIsBoundedAndMultipartOnly()
    {
        var method = typeof(DocumentFilesApiController).GetMethod("Upload");
        Assert.NotNull(method);
        Assert.Equal(6291456L, method!.GetCustomAttribute<RequestSizeLimitAttribute>()!.Bytes);
        Assert.Contains("multipart/form-data", method.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes);
    }

    [Fact]
    public void DocumentServiceUsesCanonicalContract()
    {
        Assert.True(typeof(IDocumentService).IsAssignableFrom(typeof(EduOS.Service.Services.Documents.DocumentService)));
        Assert.True(typeof(IDocumentFileService).IsAssignableFrom(typeof(EduOS.Service.Services.Documents.DocumentFileService)));
    }
}