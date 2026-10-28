using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>Disposable SQL Server proof for tenant-scoped sign-in history,
/// paging, UTC filtering and bounded privacy-safe CSV export.</summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerLoginActivityIntegrationTests
{
    [Fact]
    public async Task Shared_account_history_never_crosses_institution_or_user_and_export_is_bounded()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var noon = DateTime.SpecifyKind(today.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Utc);
        long tenantAId, tenantBId, sharedId, strangerId;

        await using (var setup = new EduOSDbContext(options))
        {
            var tenantA = new Tenant { Code = "CI-LOGIN-A-" + suffix, Name = "Login history A",
                Email = "login-a-" + suffix + "@example.test", State = TenantState.Active };
            var tenantB = new Tenant { Code = "CI-LOGIN-B-" + suffix, Name = "Login history B",
                Email = "login-b-" + suffix + "@example.test", State = TenantState.Active };
            var sharedEmail = "login-shared-" + suffix + "@example.test";
            var strangerEmail = "login-stranger-" + suffix + "@example.test";
            var shared = new ApplicationUser { UserName = sharedEmail, NormalizedUserName = sharedEmail.ToUpperInvariant(),
                Email = sharedEmail, NormalizedEmail = sharedEmail.ToUpperInvariant(),
                FullName = "Shared account", IsActive = true, EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString() };
            var stranger = new ApplicationUser { UserName = strangerEmail, NormalizedUserName = strangerEmail.ToUpperInvariant(),
                Email = strangerEmail, NormalizedEmail = strangerEmail.ToUpperInvariant(),
                FullName = "Another account", IsActive = true, EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString() };
            setup.Tenants.AddRange(tenantA, tenantB);
            setup.Users.AddRange(shared, stranger);
            await setup.SaveChangesAsync();
            tenantAId = tenantA.Id; tenantBId = tenantB.Id;
            sharedId = shared.Id; strangerId = stranger.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantAId))
            {
                setup.TenantMemberships.Add(new TenantMembership { TenantId = tenantAId,
                    UserId = sharedId, Status = MembershipStatus.Active });
                await setup.SaveChangesAsync();
            }
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantBId))
            {
                setup.TenantMemberships.Add(new TenantMembership { TenantId = tenantBId,
                    UserId = sharedId, Status = MembershipStatus.Active });
                await setup.SaveChangesAsync();
            }
            setup.ChangeTracker.Clear();
            setup.LoginHistories.AddRange(
                new LoginHistory { UserId = sharedId, TenantId = tenantAId, OccurredAt = noon,
                    IsSuccess = true, IpAddress = "192.0.2.10", UserAgent = "=PRIVATE()" },
                new LoginHistory { UserId = sharedId, TenantId = tenantAId,
                    OccurredAt = noon.AddMinutes(-1), IsSuccess = false, FailureReason = "Wrong MFA code" },
                new LoginHistory { UserId = sharedId, TenantId = tenantAId,
                    OccurredAt = noon.AddDays(-40), IsSuccess = true },
                new LoginHistory { UserId = sharedId, TenantId = tenantBId,
                    OccurredAt = noon, IsSuccess = true },
                new LoginHistory { UserId = strangerId, TenantId = tenantAId,
                    OccurredAt = noon, IsSuccess = false },
                new LoginHistory { UserId = sharedId, TenantId = null,
                    OccurredAt = noon, IsSuccess = false });
            await setup.SaveChangesAsync();
        }

        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantAId;
        await using var db = new EduOSDbContext(options, accessor);
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.TenantId).Returns(tenantAId);
        current.SetupGet(x => x.UserId).Returns(sharedId);
        var service = new LoginActivityService(new LoginHistoryRepository(db), current.Object);

        var all = await service.GetMyHistoryAsync(1, 20, null, from: today, to: today);
        Assert.True(all.Success, all.Message);
        Assert.Equal(2, all.Data!.TotalCount);
        Assert.Equal(2, all.Data.Items.Count);
        Assert.All(all.Data.Items, row => Assert.Equal(DateTimeKind.Utc, row.OccurredAt.Kind));
        var first = await service.GetMyHistoryAsync(1, 1, null, from: today, to: today);
        var second = await service.GetMyHistoryAsync(2, 1, null, from: today, to: today);
        Assert.Equal(2, first.Data!.TotalCount);
        Assert.NotEqual(first.Data.Items[0].Id, second.Data!.Items[0].Id);
        Assert.True(first.Data.Items[0].OccurredAt >= second.Data.Items[0].OccurredAt);
        var failed = await service.GetMyHistoryAsync(1, 20, false, from: today, to: today);
        Assert.Single(failed.Data!.Items);
        Assert.False(failed.Data.Items[0].IsSuccess);

        var csv = await service.ExportMyHistoryCsvAsync(null, from: today, to: today);
        Assert.True(csv.Success, csv.Message);
        Assert.Contains("Occurred UTC,Result", csv.Data);
        Assert.Contains("Successful", csv.Data);
        Assert.Contains("Unsuccessful", csv.Data);
        Assert.DoesNotContain("192.0.2.10", csv.Data);
        Assert.DoesNotContain("PRIVATE", csv.Data);
        Assert.DoesNotContain("Wrong MFA code", csv.Data);
        Assert.Equal(400, (await service.GetMyHistoryAsync(1, 20, null,
            from: today.AddDays(-400), to: today)).StatusCode);
        Assert.Equal(400, (await service.ExportMyHistoryCsvAsync(null,
            from: today, to: null)).StatusCode);
        Assert.Equal(400, (await service.GetMyHistoryAsync(0, 20, null)).StatusCode);
        current.SetupGet(x => x.IsAuthenticated).Returns(false);
        Assert.Equal(403, (await service.GetMyHistoryAsync(1, 20, null)).StatusCode);
        Assert.Equal(403, (await service.ExportMyHistoryCsvAsync(null)).StatusCode);

        var otherAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        otherAccessor.HttpContext.Items["TenantId"] = tenantBId;
        await using var dbB = new EduOSDbContext(options, otherAccessor);
        var memberB = new Mock<ICurrentUserService>();
        memberB.SetupGet(x => x.IsAuthenticated).Returns(true);
        memberB.SetupGet(x => x.UserId).Returns(sharedId);
        memberB.SetupGet(x => x.TenantId).Returns(tenantBId);
        var serviceB = new LoginActivityService(new LoginHistoryRepository(dbB), memberB.Object);
        var onlyB = await serviceB.GetMyHistoryAsync(1, 20, null, from: today, to: today);
        Assert.Equal(1, onlyB.Data!.TotalCount);
        Assert.DoesNotContain("Unsuccessful", (await serviceB.ExportMyHistoryCsvAsync(null,
            from: today, to: today)).Data!);

        // A large export is rejected, not silently truncated or leaked.
        await using (var bulk = new EduOSDbContext(options))
        {
            bulk.LoginHistories.AddRange(Enumerable.Range(0, 1001).Select(i => new LoginHistory
            {
                UserId = sharedId, TenantId = tenantAId,
                OccurredAt = noon.AddSeconds(-i), IsSuccess = true
            }));
            await bulk.SaveChangesAsync();
        }
        // Inspect the committed SQL rows independently of the original read context.
        // A fresh request-scoped DbContext also models a real post-import export request.
        await using (var probe = new EduOSDbContext(options))
        {
            var persisted = await probe.LoginHistories.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(x => x.UserId == sharedId && x.TenantId == tenantAId &&
                    x.OccurredAt >= noon.Date && x.OccurredAt < noon.Date.AddDays(1));
            Assert.Equal(1003, persisted);
        }
        var freshAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        freshAccessor.HttpContext.Items["TenantId"] = tenantAId;
        await using var fresh = new EduOSDbContext(options, freshAccessor);
        var freshUser = new Mock<ICurrentUserService>();
        freshUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        freshUser.SetupGet(x => x.UserId).Returns(sharedId);
        freshUser.SetupGet(x => x.TenantId).Returns(tenantAId);
        var freshService = new LoginActivityService(new LoginHistoryRepository(fresh), freshUser.Object);
        Assert.Equal(1003, (await freshService.GetMyHistoryAsync(1, 20, null,
            from: today, to: today)).Data!.TotalCount);
        Assert.Equal(413, (await freshService.ExportMyHistoryCsvAsync(null,
            from: today, to: today)).StatusCode);
        Assert.Equal(1, (await freshService.GetMyHistoryAsync(1, 20, false,
            from: today.AddDays(-40), to: today)).Data!.TotalCount);
    }
}
