using EduOS.App.Controllers;
using EduOS.App.Security;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

public sealed class WorkspaceSessionReadbackTests
{
    [Theory]
    [InlineData("get")]
    [InlineData("save")]
    [InlineData("reset")]
    public async Task Revoked_session_during_preferences_request_cannot_receive_private_readback(string operation)
    {
        await using var db = NewDb();
        var sid = await AuthSessionRegistry.CreateAsync(db, 50, 100, "", "", false);
        var service = new StubService(async () => { await AuthSessionRegistry.RevokeAsync(db, 50, sid); });
        var controller = Controller(db, service, 50, 100, sid);

        var result = await InvokeAsync(controller, operation);
        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, 50, sid, 100));
        Assert.Equal(AuthSessionRegistry.ToPublicReference(sid),
            controller.Response.Headers["X-EduOS-Session-Reference"].ToString());
    }

    [Theory]
    [InlineData(50L, 200L)]
    [InlineData(51L, 100L)]
    public async Task Another_institution_or_user_cannot_receive_preferences_for_the_session(
        long userId, long tenantId)
    {
        await using var db = NewDb();
        var sid = await AuthSessionRegistry.CreateAsync(db, 50, 100, "", "", false);
        var controller = Controller(db, new StubService(() => Task.CompletedTask), userId, tenantId, sid);
        Assert.IsType<UnauthorizedObjectResult>(await controller.Get(default));
    }

    [Fact]
    public async Task Current_authenticated_institution_receives_its_preferences()
    {
        await using var db = NewDb();
        var sid = await AuthSessionRegistry.CreateAsync(db, 50, 100, "", "", false);
        var controller = Controller(db, new StubService(() => Task.CompletedTask), 50, 100, sid);
        var response = Assert.IsType<ObjectResult>(await controller.Get(default));
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("100", controller.Response.Headers["X-EduOS-Tenant-Id"].ToString());
    }

    private static EduOSDbContext NewDb() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("workspace-session-" + Guid.NewGuid().ToString("N")).Options);

    private static WorkspacePreferencesController Controller(EduOSDbContext db,
        IWorkspacePreferencesService service, long userId, long tenantId, string sid)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("TenantId", tenantId.ToString()),
            new Claim(AuthSessionRegistry.ClaimType, sid)
        };
        return new WorkspacePreferencesController(service, db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookie"))
                }
            }
        };
    }

    private static Task<IActionResult> InvokeAsync(WorkspacePreferencesController controller, string operation) =>
        operation switch
        {
            "get" => controller.Get(default),
            "save" => controller.Save(new SaveWorkspacePreferencesRequestDto(), default),
            "reset" => controller.Reset(new ResetWorkspacePreferencesRequestDto(), default),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private sealed class StubService(Func<Task> afterQuery) : IWorkspacePreferencesService
    {
        private async Task<ApiResponse<WorkspacePreferencesDto>> Execute()
        {
            await afterQuery();
            return ApiResponse<WorkspacePreferencesDto>.SuccessResponse(new WorkspacePreferencesDto());
        }

        public Task<ApiResponse<WorkspacePreferencesDto>> GetAsync(CancellationToken cancellationToken = default) =>
            Execute();

        public Task<ApiResponse<WorkspacePreferencesDto>> SaveAsync(
            SaveWorkspacePreferencesRequestDto request, CancellationToken cancellationToken = default) =>
            Execute();

        public Task<ApiResponse<WorkspacePreferencesDto>> ResetAsync(
            ResetWorkspacePreferencesRequestDto request, CancellationToken cancellationToken = default) =>
            Execute();
    }
}
