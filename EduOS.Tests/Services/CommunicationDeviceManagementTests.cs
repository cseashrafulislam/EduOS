using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Communication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class CommunicationDeviceManagementTests
{
    [Fact]
    public async Task Anonymous_user_cannot_list_registered_devices()
    {
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("devices-anon-" + Guid.NewGuid().ToString("N")).Options);
        var user = new Mock<ICurrentUserService>();
        var reader = new MessageWorkspaceService(db, user.Object, db);
        Assert.Equal(403, (await reader.GetMyDevicesAsync(1, 20)).StatusCode);
    }

    [Fact]
    public async Task Invalid_paging_is_rejected_before_database_access()
    {
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("devices-page-" + Guid.NewGuid().ToString("N")).Options);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.TenantId).Returns(11);
        user.SetupGet(x => x.UserId).Returns(99);
        var reader = new MessageWorkspaceService(db, user.Object, db);
        Assert.Equal(400, (await reader.GetMyDevicesAsync(0, 20)).StatusCode);
        Assert.Equal(400, (await reader.GetMyDevicesAsync(1, 101)).StatusCode);
    }

    [Fact]
    public void Device_api_requires_authentication_and_antiforgery_for_writes()
    {
        var type = typeof(CommunicationWorkspaceApiController);
        Assert.NotEmpty(type.GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.NotEmpty(type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        Assert.NotEmpty(type.GetMethod("Devices")!.GetCustomAttributes(typeof(HttpGetAttribute), true));
        Assert.NotEmpty(type.GetMethod("DeactivateDevice")!.GetCustomAttributes(typeof(HttpPostAttribute), true));
    }
}
