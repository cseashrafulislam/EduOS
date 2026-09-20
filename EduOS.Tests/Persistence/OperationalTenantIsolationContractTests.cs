using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.Transport;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Persistence;

public class OperationalTenantIsolationContractTests
{
    [Theory]
    [InlineData(typeof(Book))]
    [InlineData(typeof(BookIssue))]
    [InlineData(typeof(Route))]
    [InlineData(typeof(Vehicle))]
    [InlineData(typeof(StudentTransport))]
    public void Library_and_transport_models_keep_tenant_query_filters(Type entityType)
    {
        using var context = CreateContext(101);
        var mapped = context.Model.FindEntityType(entityType);

        mapped.Should().NotBeNull($"{entityType.Name} is an operational tenant-owned model");
        mapped!.GetQueryFilter().Should().NotBeNull(
            $"{entityType.Name} must never become readable without the global tenant boundary");
    }

    [Fact]
    public async Task Library_and_transport_records_are_not_visible_across_tenants()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"operational-tenant-isolation-{Guid.NewGuid():N}")
            .Options;

        await using (var tenant101 = CreateContext(options, 101))
        {
            tenant101.Books.Add(new Book { TenantId = 101, Title = "Tenant 101 book", TotalCopies = 1, AvailableCopies = 1 });
            tenant101.Routes.Add(new Route { TenantId = 101, RouteName = "Tenant 101 route" });
            await tenant101.SaveChangesAsync();
        }

        await using (var tenant202 = CreateContext(options, 202))
        {
            (await tenant202.Books.CountAsync()).Should().Be(0);
            (await tenant202.Routes.CountAsync()).Should().Be(0);
        }
    }

    private static EduOSDbContext CreateContext(long tenantId) =>
        CreateContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"operational-tenant-model-{Guid.NewGuid():N}")
            .Options, tenantId);

    private static EduOSDbContext CreateContext(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "9001"),
                new Claim("TenantId", tenantId.ToString())
            ], "TestAuthentication"))
        };
        httpContext.Items["TenantId"] = tenantId;

        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = httpContext });
    }
}
