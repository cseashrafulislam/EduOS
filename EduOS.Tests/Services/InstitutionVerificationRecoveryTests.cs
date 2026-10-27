using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Settings;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Tenants;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class InstitutionVerificationRecoveryTests
{
    [Fact]
    public async Task Pending_owner_receives_one_recovery_job_and_a_durable_cooldown()
    {
        await using var setup = await SetupAsync();
        var first = await setup.Service.ResendVerificationEmailAsync(" OWNER@EXAMPLE.TEST ");
        var retry = await setup.Service.ResendVerificationEmailAsync("owner@example.test");
        first.Success.Should().BeTrue();
        retry.Success.Should().BeTrue();
        first.Message.Should().Be(retry.Message);
        (await setup.Db.AuditLogs.CountAsync(x =>
            x.Action == "InstitutionVerificationEmailRequested")).Should().Be(1);
        setup.Jobs.Verify(x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Once);
    }

    [Fact]
    public async Task Verified_and_unknown_accounts_do_not_disclose_registration_state_or_queue_email()
    {
        await using var setup = await SetupAsync();
        var unknown = await setup.Service.ResendVerificationEmailAsync("unknown@example.test");
        setup.User.EmailConfirmed = true;
        var verified = await setup.Service.ResendVerificationEmailAsync("owner@example.test");
        unknown.Success.Should().BeTrue();
        verified.Success.Should().BeTrue();
        unknown.Message.Should().Be(verified.Message);
        (await setup.Db.AuditLogs.CountAsync()).Should().Be(0);
        setup.Jobs.Verify(x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("a@example.test\\r\\nBcc:someone@example.test")]
    public async Task Invalid_recovery_addresses_are_rejected(string email)
    {
        await using var setup = await SetupAsync();
        (await setup.Service.ResendVerificationEmailAsync(email)).StatusCode.Should().Be(400);
        (await setup.Db.AuditLogs.CountAsync()).Should().Be(0);
    }

    private static async Task<Setup> SetupAsync()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("institution-verification-recovery-" + Guid.NewGuid().ToString("N"))
            .Options;
        var db = new EduOSDbContext(options, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var user = new ApplicationUser
        {
            Id = 77, Email = "owner@example.test", UserName = "owner@example.test",
            FullName = "Owner", IsActive = true, EmailConfirmed = false
        };
        db.Users.Add(user);
        db.Tenants.Add(new Tenant
        {
            Id = 410, Name = "Test Institution", Code = "TN-410", Email = "owner@example.test",
            State = TenantState.PendingVerification, OnboardingStage = OnboardingStage.EmailVerification
        });
        db.TenantMemberships.Add(new TenantMembership
        {
            TenantId = 410, UserId = user.Id, IsOwner = true, Status = MembershipStatus.Active
        });
        await db.SaveChangesAsync();

        var store = new Mock<IUserStore<ApplicationUser>>();
        var manager = new Mock<UserManager<ApplicationUser>>(store.Object,
            Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(), Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);
        manager.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((string email) => email == "owner@example.test" ? user : null);
        manager.SetupGet(x => x.Users).Returns(db.Users);
        manager.Setup(x => x.GenerateEmailConfirmationTokenAsync(user))
            .ReturnsAsync("test-verification-token");
        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(x => x.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("job-1");

        var service = new InstitutionRegistrationService(manager.Object,
            new GenericRepository<Tenant>(db),
            new GenericRepository<TenantMembership>(db),
            new GenericRepository<InstitutionTypeDefinition>(db),
            new GenericRepository<InstitutionTypeModule>(db),
            new GenericRepository<TenantModule>(db),
            new GenericRepository<LegalDocument>(db),
            new GenericRepository<UserLegalAcceptance>(db),
            new GenericRepository<AuditLog>(db),
            db, jobs.Object, Options.Create(new TenantPortalSettings { BaseDomain = "eduos.example.test" }),
            TimeProvider.System, NullLogger<InstitutionRegistrationService>.Instance);
        return new Setup(db, user, service, jobs);
    }

    private sealed record Setup(EduOSDbContext Db, ApplicationUser User,
        InstitutionRegistrationService Service, Mock<IBackgroundJobClient> Jobs) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }
}
