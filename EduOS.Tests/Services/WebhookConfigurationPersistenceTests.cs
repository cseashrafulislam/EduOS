using EduOS.Core.DTOs.System;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class WebhookConfigurationPersistenceTests
{
    [Fact]
    public async Task Rejected_create_is_not_saved_by_a_later_unrelated_save()
    {
        await using var fixture = Create();
        var rejected = await fixture.Service.SaveWebhookEndpointAsync(null, Request(true));
        Assert.False(rejected.Success);
        Assert.Empty(fixture.Db.ChangeTracker.Entries<WebhookEndpoint>());
        await fixture.Db.SaveChangesAsync();
        Assert.Empty(await fixture.Db.WebhookEndpoints.ToListAsync());
        var accepted = Request(true);
        accepted.Secret = "0123456789abcdef";
        Assert.True((await fixture.Service.SaveWebhookEndpointAsync(null, accepted)).Success);
        Assert.Equal("Valid", Assert.Single(await fixture.Db.WebhookEndpoints.ToListAsync()).Name);
    }

    [Fact]
    public async Task Rejected_update_does_not_dirty_existing_entity()
    {
        await using var fixture = Create();
        var original = new WebhookEndpoint
        {
            TenantId = 101, Name = "Original", Url = "https://hooks.example.org/old",
            EventCodes = "WEBHOOK.TEST", IsActive = false, RowVersion = [1, 2, 3, 4]
        };
        fixture.Db.WebhookEndpoints.Add(original);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var request = Request(true);
        request.RowVersion = Convert.ToBase64String(original.RowVersion);
        Assert.False((await fixture.Service.SaveWebhookEndpointAsync(original.Id, request)).Success);
        var tracked = Assert.Single(fixture.Db.ChangeTracker.Entries<WebhookEndpoint>());
        Assert.Equal(EntityState.Unchanged, tracked.State);
        Assert.Equal("Original", tracked.Entity.Name);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal("Original", (await fixture.Db.WebhookEndpoints.SingleAsync()).Name);
    }

    [Fact]
    public async Task Stale_version_does_not_change_existing_webhook()
    {
        await using var fixture = Create();
        var row = new WebhookEndpoint
        {
            TenantId = 101, Name = "Original", Url = "https://hooks.example.org/old",
            EventCodes = "WEBHOOK.TEST", IsActive = false, RowVersion = [1, 2, 3, 4]
        };
        fixture.Db.WebhookEndpoints.Add(row);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var request = Request(false);
        request.RowVersion = Convert.ToBase64String(new byte[] { 9, 8, 7, 6 });
        var response = await fixture.Service.SaveWebhookEndpointAsync(row.Id, request);
        Assert.Equal(409, response.StatusCode);
        Assert.Equal("Original", (await fixture.Db.WebhookEndpoints.SingleAsync()).Name);
    }

    private static SaveWebhookEndpointRequestDto Request(bool active) => new()
    {
        Name = "Valid", Url = "https://hooks.example.org/notify",
        EventCodes = ["WEBHOOK.TEST"], IsActive = active
    };

    private static Fixture Create()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("webhook-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        var service = new IntegrationAdministrationService(db, new Admin(),
            new EphemeralDataProtectionProvider(), db,
            NullLogger<IntegrationAdministrationService>.Instance);
        return new Fixture(db, service);
    }

    private sealed record Fixture(EduOSDbContext Db, IntegrationAdministrationService Service) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class Admin : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 71;
        public long TenantId => 101;
        public string? FullName => "Admin";
        public string? Email => null;
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
