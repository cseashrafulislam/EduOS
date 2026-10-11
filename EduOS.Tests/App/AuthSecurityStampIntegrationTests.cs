using EduOS.App.Extensions;
using EduOS.Core.Entities.Auth;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

public sealed class AuthSecurityStampIntegrationTests
{
    [Fact]
    public async Task Security_stamp_rotation_allows_only_one_of_two_concurrent_challenge_claims()
    {
        var services = CreateServices();
        using var provider = services.BuildServiceProvider();
        using (var setup = provider.CreateScope())
        {
            var manager = setup.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var created = await manager.CreateAsync(new ApplicationUser
            {
                UserName = "admin@example.test", Email = "admin@example.test",
                FullName = "Admin", EmailConfirmed = true
            }, "Secure123!");
            Assert.True(created.Succeeded, string.Join(";", created.Errors.Select(x => x.Description)));
        }

        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var second = secondScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var firstUser = await first.FindByEmailAsync("admin@example.test");
        var secondUser = await second.FindByEmailAsync("admin@example.test");
        Assert.NotNull(firstUser);
        Assert.NotNull(secondUser);
        var challengeStamp = await first.GetSecurityStampAsync(firstUser);
        Assert.Equal(challengeStamp, await second.GetSecurityStampAsync(secondUser));

        var firstCompletion = await first.UpdateSecurityStampAsync(firstUser);
        Assert.True(firstCompletion.Succeeded);
        var secondCompletion = await second.UpdateSecurityStampAsync(secondUser);
        Assert.False(secondCompletion.Succeeded);

        using var freshScope = provider.CreateScope();
        var verifier = freshScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var current = await verifier.FindByEmailAsync("admin@example.test");
        Assert.NotNull(current);
        Assert.NotEqual(challengeStamp, await verifier.GetSecurityStampAsync(current));
    }

    [Fact]
    public async Task Password_change_rotates_stamp_and_cookie_validation_has_no_stale_window()
    {
        var services = CreateServices();
        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value;
        Assert.Equal(TimeSpan.Zero, validator.ValidationInterval);
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "teacher@example.test", Email = "teacher@example.test", FullName = "Teacher", EmailConfirmed = true };
        Assert.True((await manager.CreateAsync(user, "Secure123!")).Succeeded);
        var before = await manager.GetSecurityStampAsync(user);
        var changed = await manager.ChangePasswordAsync(user, "Secure123!", "Different123!");
        Assert.True(changed.Succeeded, string.Join(";", changed.Errors.Select(x => x.Description)));
        Assert.NotEqual(before, await manager.GetSecurityStampAsync(user));
        Assert.True(await manager.CheckPasswordAsync(user, "Different123!"));
        Assert.False(await manager.CheckPasswordAsync(user, "Secure123!"));
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(Environments.Development);
        services.AddLogging();
        services.AddDataProtection();
        var databaseName = "auth-stamp-" + Guid.NewGuid().ToString("N");
        services.AddDbContext<EduOSDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddIdentityConfiguration(environment.Object);
        return services;
    }
}
