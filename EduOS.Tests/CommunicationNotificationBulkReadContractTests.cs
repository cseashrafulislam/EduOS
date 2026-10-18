using System.Reflection;
using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests;

public sealed class CommunicationNotificationBulkReadContractTests
{
    [Fact]
    public void MarkAllRead_UsesExistingAuthenticatedAntiforgeryProtectedApi()
    {
        var controller = typeof(CommunicationWorkspaceApiController);
        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(controller.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>());
        var action = controller.GetMethod(nameof(CommunicationWorkspaceApiController.MarkAllNotificationsRead));
        Assert.NotNull(action);
        Assert.Equal("notifications/read-all", action!.GetCustomAttribute<HttpPostAttribute>()?.Template);
    }

    [Fact]
    public void MarkAllRead_ReusesTenantScopedRepositoryContract()
    {
        Assert.NotNull(typeof(ICommunicationService).GetMethod(nameof(ICommunicationService.MarkAllMyNotificationsReadAsync)));
        var method = typeof(INotificationRepository).GetMethod(nameof(INotificationRepository.MarkAllAsReadAsync));
        Assert.NotNull(method);
        var args = method!.GetParameters();
        Assert.Equal("tenantId", args[0].Name);
        Assert.Equal("userId", args[1].Name);
    }
}
