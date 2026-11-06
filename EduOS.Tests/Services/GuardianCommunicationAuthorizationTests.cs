using EduOS.Core.Entities.Auth;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Students;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class GuardianCommunicationAuthorizationTests
{
    [Fact]
    public async Task Guardian_preferences_require_active_person_and_live_membership_in_selected_tenant()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("guardian-pref-auth-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        var account = new ApplicationUser { Id = 51, UserName = "guardian-51", IsActive = true };
        var membership = new TenantMembership { Id = 11, TenantId = 101, UserId = 51,
            Status = MembershipStatus.Active };
        db.Users.Add(account);
        db.TenantMemberships.Add(membership);
        await db.SaveChangesAsync();

        var actor = new Mock<ICurrentUserService>();
        actor.SetupGet(x => x.IsAuthenticated).Returns(true);
        actor.SetupGet(x => x.UserId).Returns(51);
        actor.SetupGet(x => x.TenantId).Returns(101);
        actor.Setup(x => x.IsInRole("Guardian")).Returns(true);
        var service = new GuardianCommunicationPreferenceService(db, actor.Object,
            NullLogger<GuardianCommunicationPreferenceService>.Instance);
        Assert.Equal(200, (await service.GetMineAsync()).StatusCode);

        http.Items["TenantId"] = 202L;
        Assert.Equal(403, (await service.GetMineAsync()).StatusCode);
        http.Items["TenantId"] = 101L;
        membership.Status = MembershipStatus.Suspended;
        await db.SaveChangesAsync();
        Assert.Equal(403, (await service.GetMineAsync()).StatusCode);
        Assert.Equal(403, (await service.UpdateMineAsync(123, null!)).StatusCode);

        membership.Status = MembershipStatus.Active;
        account.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(403, (await service.GetMineAsync()).StatusCode);
        Assert.False(db.ChangeTracker.HasChanges());
    }
}
