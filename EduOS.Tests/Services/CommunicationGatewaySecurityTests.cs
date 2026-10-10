using EduOS.Core.DTOs.Communication;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Communication;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class CommunicationGatewaySecurityTests
{
    [Fact]
    public async Task Credentials_are_protected_and_tenant_scoped()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("gateway-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        CommunicationAdministrationService Service(long tenant) => new(
            new GenericRepository<CommunicationGateway>(db),
            new GenericRepository<MessageTemplate>(db),
            new GenericRepository<NoticeCategory>(db),
            new InMemoryUnitOfWork(db), new User(tenant),
            new EphemeralDataProtectionProvider(),
            NullLogger<CommunicationAdministrationService>.Instance);
        var saved = await Service(101).SaveGatewayAsync(null, new SaveCommunicationGatewayRequestDto
        {
            ProviderCode = "MAIL", Channel = NotificationChannelType.Email,
            Endpoint = "https://gateway.example.test", Credential = "private-token", IsDefault = true
        });
        saved.Success.Should().BeTrue(saved.Message);
        saved.Data!.HasCredential.Should().BeTrue();
        var row = await db.CommunicationGateways.SingleAsync();
        row.ProtectedCredential.Should().NotContain("private-token");
        (await Service(101).GetGatewaysAsync()).Data.Should().ContainSingle(x => x.HasCredential && x.IsDefault);
        (await Service(202).GetGatewaysAsync()).Data.Should().BeEmpty();
    }

    private sealed class InMemoryUnitOfWork(EduOSDbContext db) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) => operation(ct);
    }
    private sealed class User(long tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 10;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
