using System.Data;
using EduOS.Core.DTOs.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class IntegrationAdministrationRevocationTests
{
    private sealed class RevokeOnTransaction(EduOSDbContext db, Action revoke) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) => ExecuteInTransactionAsync(operation, IsolationLevel.Serializable, ct);
        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
            IsolationLevel isolationLevel, CancellationToken ct = default)
        {
            revoke();
            await db.SaveChangesAsync(ct);
            return await operation(ct);
        }
    }

    [Fact]
    public async Task Revoked_admin_cannot_mint_credentials_claim_webhook_or_issue_outbound_attempt()
    {
        var context = new DefaultHttpContext();
        context.Items["TenantId"] = 11L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("integration-revocation-" + Guid.NewGuid().ToString("N"))
            .Options, new HttpContextAccessor { HttpContext = context });
        CentralPrivilegedTestIdentity.Seed(db, 11, 10);
        var member = db.TenantMemberships.Single(x => x.UserId == 10);
        var actor = new Mock<ICurrentUserService>();
        actor.SetupGet(x => x.IsAuthenticated).Returns(true);
        actor.SetupGet(x => x.UserId).Returns(10);
        actor.SetupGet(x => x.TenantId).Returns(11);
        actor.SetupGet(x => x.IsTenantAdmin).Returns(true);
        var service = new IntegrationAdministrationService(db, actor.Object, null!,
            new RevokeOnTransaction(db, () => member.Status = MembershipStatus.Suspended),
            NullLogger<IntegrationAdministrationService>.Instance);

        Assert.Equal(403, (await service.CreateApiCredentialAsync(new CreateApiCredentialRequestDto {
            ClientRequestId = Guid.NewGuid(), Name = "ERP connector", AllowedScopes = ["integration:read"]
        })).StatusCode);
        Assert.False(db.ApiCredentials.Any());
        Assert.False(db.IdempotencyRecords.Any());

        member.Status = MembershipStatus.Active;
        await db.SaveChangesAsync();
        Assert.Equal(403, (await service.TestWebhookAsync(123, new CreateWebhookTestRequestDto {
            ClientRequestId = Guid.NewGuid()
        })).StatusCode);
        Assert.False(db.WebhookDeliveries.Any());

        member.Status = MembershipStatus.Active;
        await db.SaveChangesAsync();
        Assert.Equal(403, (await service.RetryWebhookAsync(123, new RetryWebhookDeliveryRequestDto {
            ClientRequestId = Guid.NewGuid(), ExpectedAttemptCount = 1
        })).StatusCode);
        Assert.False(db.WebhookDeliveries.Any());
    }
}
