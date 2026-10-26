using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class WebhookDeliveryExportDataTests
{
    [Fact]
    public async Task Only_selected_tenant_and_endpoint_rows_are_exported()
    {
        await using var db = Database();
        var own = new WebhookEndpoint { TenantId = 101, Name = "Own", Url = "https://hooks.example.org/one" };
        var other = new WebhookEndpoint { TenantId = 202, Name = "Other", Url = "https://hooks.example.org/two" };
        db.WebhookEndpoints.AddRange(own, other);
        await TestTenantSeeder.SaveAsync(db);
        db.WebhookDeliveries.AddRange(
            new WebhookDelivery { TenantId = 101, WebhookEndpointId = own.Id, EventId = Guid.NewGuid(), EventCode = "=1+1", PayloadJson = "{}" },
            new WebhookDelivery { TenantId = 202, WebhookEndpointId = own.Id, EventId = Guid.NewGuid(), EventCode = "OTHER_TENANT", PayloadJson = "{}" },
            new WebhookDelivery { TenantId = 202, WebhookEndpointId = other.Id, EventId = Guid.NewGuid(), EventCode = "OTHER_ENDPOINT", PayloadJson = "{}" });
        await TestTenantSeeder.SaveAsync(db);
        var service = Service(db);
        var response = await service.ExportWebhookDeliveriesCsvAsync(own.Id);
        Assert.True(response.Success);
        Assert.Contains("'=1+1", response.Data);
        Assert.DoesNotContain("OTHER_TENANT", response.Data);
        Assert.DoesNotContain("OTHER_ENDPOINT", response.Data);
        Assert.Equal(404, (await service.ExportWebhookDeliveriesCsvAsync(other.Id)).StatusCode);
    }

    [Fact]
    public async Task Export_has_a_strict_row_limit()
    {
        await using var db = Database();
        var own = new WebhookEndpoint { TenantId = 101, Name = "Own", Url = "https://hooks.example.org/one" };
        db.WebhookEndpoints.Add(own);
        await db.SaveChangesAsync();
        db.WebhookDeliveries.AddRange(Enumerable.Range(0, 1001).Select(_ =>
            new WebhookDelivery { TenantId = 101, WebhookEndpointId = own.Id, EventId = Guid.NewGuid(), EventCode = "TEST", PayloadJson = "{}" }));
        await db.SaveChangesAsync();
        Assert.Equal(413, (await Service(db).ExportWebhookDeliveriesCsvAsync(own.Id)).StatusCode);
    }

    private static EduOSDbContext Database()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("webhook-csv-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
    }

    private static IntegrationAdministrationService Service(EduOSDbContext db)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.IsTenantAdmin).Returns(true);
        user.SetupGet(x => x.TenantId).Returns(101);
        return new IntegrationAdministrationService(db, user.Object,
            new EphemeralDataProtectionProvider(), db, NullLogger<IntegrationAdministrationService>.Instance);
    }
}
