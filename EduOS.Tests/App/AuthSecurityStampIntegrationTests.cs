using EduOS.App.Extensions;
using EduOS.App.Security;
using System.Security.Claims;
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

    [Fact]
    public async Task Security_stamp_renewal_retains_signed_tenant_roles_and_session_identifier()
    {
        var services = CreateServices();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value;
        Assert.NotNull(options.OnRefreshingPrincipal);
        var oldPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", "101"),
            new Claim(AuthSessionRegistry.ClaimType, new string('A', 48)),
            new Claim("amr", "mfa")
        }, "Identity.Application"));
        var nextPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(ClaimTypes.Role, "Principal")
        }, "Identity.Application"));
        var refresh = new SecurityStampRefreshingPrincipalContext
        {
            CurrentPrincipal = oldPrincipal,
            NewPrincipal = nextPrincipal
        };
        await options.OnRefreshingPrincipal!(refresh);
        Assert.Equal("101", refresh.NewPrincipal!.FindFirstValue("TenantId"));
        Assert.Equal(new string('A', 48), refresh.NewPrincipal.FindFirstValue(AuthSessionRegistry.ClaimType));
        Assert.True(refresh.NewPrincipal.IsInRole("TenantAdmin"));
        Assert.False(refresh.NewPrincipal.IsInRole("Principal"));
        Assert.Equal("mfa", refresh.NewPrincipal.FindFirstValue("amr"));
    }

    [Fact]
    public async Task Password_change_revokes_all_institution_sessions_but_not_another_account()
    {
        using var provider = CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<EduOSDbContext>();
        var owner = new ApplicationUser { UserName = "change@example.test", Email = "change@example.test",
            FullName = "Owner", EmailConfirmed = true };
        var stranger = new ApplicationUser { UserName = "stranger@example.test", Email = "stranger@example.test",
            FullName = "Stranger", EmailConfirmed = true };
        Assert.True((await manager.CreateAsync(owner, "Secure123!")).Succeeded);
        Assert.True((await manager.CreateAsync(stranger, "Secure123!")).Succeeded);
        var first = await AuthSessionRegistry.CreateAsync(db, owner.Id, 100, "", "", false);
        var second = await AuthSessionRegistry.CreateAsync(db, owner.Id, 200, "", "", true);
        var unrelated = await AuthSessionRegistry.CreateAsync(db, stranger.Id, 100, "", "", false);

        var rejected = await AuthCredentialRotation.ChangePasswordAsync(
            db, manager, owner, "Wrong123!", "Changed123!");
        Assert.False(rejected.Succeeded);
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, owner.Id, first, 100));

        var changed = await AuthCredentialRotation.ChangePasswordAsync(
            db, manager, owner, "Secure123!", "Changed123!");
        Assert.True(changed.Succeeded, string.Join(";", changed.Errors.Select(x => x.Description)));
        Assert.True(await manager.CheckPasswordAsync(owner, "Changed123!"));
        Assert.False(await manager.CheckPasswordAsync(owner, "Secure123!"));
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, owner.Id, first, 100));
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, owner.Id, second, 200));
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, stranger.Id, unrelated, 100));
    }

    [Fact]
    public async Task Password_reset_revokes_all_institution_sessions_and_rejects_token_replay()
    {
        using var provider = CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<EduOSDbContext>();
        var user = new ApplicationUser { UserName = "reset@example.test", Email = "reset@example.test",
            FullName = "Recovery", EmailConfirmed = true };
        Assert.True((await manager.CreateAsync(user, "Secure123!")).Succeeded);
        var first = await AuthSessionRegistry.CreateAsync(db, user.Id, 100, "", "", false);
        var second = await AuthSessionRegistry.CreateAsync(db, user.Id, 200, "", "", false);
        var invalid = await AuthCredentialRotation.ResetPasswordAsync(
            db, manager, user, "invalid-token", "Changed123!");
        Assert.False(invalid.Succeeded);
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, user.Id, first, 100));

        var token = await manager.GeneratePasswordResetTokenAsync(user);
        var reset = await AuthCredentialRotation.ResetPasswordAsync(
            db, manager, user, token, "Changed123!");
        Assert.True(reset.Succeeded, string.Join(";", reset.Errors.Select(x => x.Description)));
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, user.Id, first, 100));
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, user.Id, second, 200));
        Assert.True(await manager.CheckPasswordAsync(user, "Changed123!"));
        Assert.False((await AuthCredentialRotation.ResetPasswordAsync(
            db, manager, user, token, "Again123!")).Succeeded);
        Assert.True(await manager.CheckPasswordAsync(user, "Changed123!"));
    }

    [Fact]
    public async Task Sql_password_recovery_persists_password_and_revocations_in_same_database()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        using var provider = CreateServices(connection).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<EduOSDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = "recovery-" + suffix + "@example.test",
            Email = "recovery-" + suffix + "@example.test", FullName = "SQL Recovery",
            EmailConfirmed = true };
        var created = await manager.CreateAsync(user, "Secure123!");
        Assert.True(created.Succeeded, string.Join(";", created.Errors.Select(x => x.Description)));
        var sidA = await AuthSessionRegistry.CreateAsync(db, user.Id, 100, "", "", false);
        var sidB = await AuthSessionRegistry.CreateAsync(db, user.Id, 200, "", "", false);
        var token = await manager.GeneratePasswordResetTokenAsync(user);
        var result = await AuthCredentialRotation.ResetPasswordAsync(
            db, manager, user, token, "Changed123!");
        Assert.True(result.Succeeded, string.Join(";", result.Errors.Select(x => x.Description)));

        using var fresh = provider.CreateScope();
        var readDb = fresh.ServiceProvider.GetRequiredService<EduOSDbContext>();
        var readManager = fresh.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var persisted = await readManager.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(persisted);
        Assert.True(await readManager.CheckPasswordAsync(persisted, "Changed123!"));
        Assert.False(await readManager.CheckPasswordAsync(persisted, "Secure123!"));
        Assert.False(await AuthSessionRegistry.IsValidAsync(readDb, user.Id, sidA, 100));
        Assert.False(await AuthSessionRegistry.IsValidAsync(readDb, user.Id, sidB, 200));
    }

    [Fact]
    public async Task Sql_mfa_secret_rotation_failure_rolls_back_key_and_all_session_revocations()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        using var provider = CreateServices(connection).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<EduOSDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser
        {
            UserName = "mfa-rollback-" + suffix + "@example.test",
            Email = "mfa-rollback-" + suffix + "@example.test",
            FullName = "MFA Rollback", EmailConfirmed = true
        };
        Assert.True((await manager.CreateAsync(user, "Secure123!")).Succeeded);
        Assert.True((await manager.ResetAuthenticatorKeyAsync(user)).Succeeded);
        var originalKey = await manager.GetAuthenticatorKeyAsync(user);
        Assert.False(string.IsNullOrWhiteSpace(originalKey));
        var first = await AuthSessionRegistry.CreateAsync(db, user.Id, 101, "", "", false);
        var second = await AuthSessionRegistry.CreateAsync(db, user.Id, 202, "", "", false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.ExecuteInTransactionAsync<bool>(async ct =>
            {
                var reset = await manager.ResetAuthenticatorKeyAsync(user);
                Assert.True(reset.Succeeded);
                await AuthSessionRegistry.RevokeAllAsync(db, user.Id);
                throw new InvalidOperationException("Injected failure after session revocation.");
            }));

        using var fresh = provider.CreateScope();
        var freshDb = fresh.ServiceProvider.GetRequiredService<EduOSDbContext>();
        var freshManager = fresh.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var persisted = await freshManager.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(persisted);
        Assert.Equal(originalKey, await freshManager.GetAuthenticatorKeyAsync(persisted));
        Assert.True(await AuthSessionRegistry.IsValidAsync(freshDb, user.Id, first, 101));
        Assert.True(await AuthSessionRegistry.IsValidAsync(freshDb, user.Id, second, 202));
    }


    [Fact]
    public async Task Mfa_recovery_code_is_single_use_and_atomically_revokes_only_owner_sessions()
    {
        using var provider = CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<EduOSDbContext>();
        var owner = new ApplicationUser { UserName = "mfa-owner@example.test",
            Email = "mfa-owner@example.test", FullName = "Owner", EmailConfirmed = true };
        var stranger = new ApplicationUser { UserName = "mfa-other@example.test",
            Email = "mfa-other@example.test", FullName = "Other", EmailConfirmed = true };
        Assert.True((await manager.CreateAsync(owner, "Secure123!")).Succeeded);
        Assert.True((await manager.CreateAsync(stranger, "Secure123!")).Succeeded);
        Assert.True((await manager.SetTwoFactorEnabledAsync(owner, true)).Succeeded);
        var codes = (await manager.GenerateNewTwoFactorRecoveryCodesAsync(owner, 8))!.ToArray();
        Assert.Equal(8, codes.Length);
        var originalStamp = await manager.GetSecurityStampAsync(owner);
        var tenantA = await AuthSessionRegistry.CreateAsync(db, owner.Id, 101, "", "", false);
        var tenantB = await AuthSessionRegistry.CreateAsync(db, owner.Id, 202, "", "", false);
        var other = await AuthSessionRegistry.CreateAsync(db, stranger.Id, 101, "", "", false);

        Assert.Equal(MfaLoginConsumption.InvalidCode,
            await AuthMfaLoginTransition.ConsumeAsync(db, manager, owner, "invalid-code", true));
        Assert.Equal(originalStamp, await manager.GetSecurityStampAsync(owner));
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, owner.Id, tenantA, 101));

        Assert.Equal(MfaLoginConsumption.Completed,
            await AuthMfaLoginTransition.ConsumeAsync(db, manager, owner, codes[0], true));
        Assert.NotEqual(originalStamp, await manager.GetSecurityStampAsync(owner));
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, owner.Id, tenantA, 101));
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, owner.Id, tenantB, 202));
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, stranger.Id, other, 101));
        Assert.Equal(MfaLoginConsumption.InvalidCode,
            await AuthMfaLoginTransition.ConsumeAsync(db, manager, owner, codes[0], true));
    }

    [Fact]
    public async Task Sql_failed_mfa_completion_rolls_back_recovery_code_stamp_and_sessions()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        using var provider = CreateServices(connection).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<EduOSDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = "mfa-atomic-" + suffix + "@example.test",
            Email = "mfa-atomic-" + suffix + "@example.test",
            FullName = "SQL MFA", EmailConfirmed = true };
        Assert.True((await manager.CreateAsync(user, "Secure123!")).Succeeded);
        Assert.True((await manager.SetTwoFactorEnabledAsync(user, true)).Succeeded);
        var codes = (await manager.GenerateNewTwoFactorRecoveryCodesAsync(user, 8))!.ToArray();
        var stamp = await manager.GetSecurityStampAsync(user);
        var first = await AuthSessionRegistry.CreateAsync(db, user.Id, 101, "", "", false);
        var second = await AuthSessionRegistry.CreateAsync(db, user.Id, 202, "", "", false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.ExecuteInTransactionAsync<bool>(async ct =>
            {
                Assert.True((await manager.RedeemTwoFactorRecoveryCodeAsync(user, codes[0])).Succeeded);
                Assert.True((await manager.UpdateSecurityStampAsync(user)).Succeeded);
                await AuthSessionRegistry.RevokeAllAsync(db, user.Id);
                throw new InvalidOperationException("Injected failure after recovery-code redemption.");
            }));

        using var fresh = provider.CreateScope();
        var freshDb = fresh.ServiceProvider.GetRequiredService<EduOSDbContext>();
        var freshManager = fresh.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var persisted = await freshManager.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(persisted);
        Assert.Equal(stamp, await freshManager.GetSecurityStampAsync(persisted));
        Assert.True(await AuthSessionRegistry.IsValidAsync(freshDb, user.Id, first, 101));
        Assert.True(await AuthSessionRegistry.IsValidAsync(freshDb, user.Id, second, 202));
        Assert.Equal(MfaLoginConsumption.Completed,
            await AuthMfaLoginTransition.ConsumeAsync(freshDb, freshManager, persisted, codes[0], true));

        using var check = provider.CreateScope();
        var checkedDb = check.ServiceProvider.GetRequiredService<EduOSDbContext>();
        var checkedManager = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var checkedUser = await checkedManager.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(checkedUser);
        Assert.NotEqual(stamp, await checkedManager.GetSecurityStampAsync(checkedUser));
        Assert.False(await AuthSessionRegistry.IsValidAsync(checkedDb, user.Id, first, 101));
        Assert.False(await AuthSessionRegistry.IsValidAsync(checkedDb, user.Id, second, 202));
        Assert.Equal(MfaLoginConsumption.InvalidCode,
            await AuthMfaLoginTransition.ConsumeAsync(checkedDb, checkedManager, checkedUser, codes[0], true));
    }

    private static ServiceCollection CreateServices(string? sqlConnection = null)
    {
        var services = new ServiceCollection();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(Environments.Development);
        services.AddLogging();
        services.AddDataProtection();
        var databaseName = "auth-stamp-" + Guid.NewGuid().ToString("N");
        services.AddDbContext<EduOSDbContext>(options =>
        {
            if (string.IsNullOrWhiteSpace(sqlConnection)) options.UseInMemoryDatabase(databaseName);
            else options.UseSqlServer(sqlConnection);
        });
        services.AddIdentityConfiguration(environment.Object);
        return services;
    }
}
