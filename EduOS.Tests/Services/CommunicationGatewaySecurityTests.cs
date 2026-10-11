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

    [Fact]
    public async Task Onboarding_sms_and_smtp_fields_are_stored_in_canonical_gateway_and_secrets_stay_masked()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("gateway-setup-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        var service = Create(db, 101);
        var sms = await service.SaveGatewayAsync(null, new SaveCommunicationGatewayRequestDto
        {
            ProviderCode = "BulkSMSBD", Channel = NotificationChannelType.Sms,
            Endpoint = "https://sms.example.com/send", SmsSenderId = "MY-SCHOOL",
            Credential = "sms-private-token", IsDefault = true, IsActive = true
        });
        sms.Success.Should().BeTrue(sms.Message);
        var smtp = await service.SaveGatewayAsync(null, new SaveCommunicationGatewayRequestDto
        {
            ProviderCode = "SMTP", Channel = NotificationChannelType.Email,
            SmtpHost = "smtp.example.com", SmtpPort = 587,
            SmtpUsername = "sender@example.com", FromEmail = "no-reply@example.com",
            FromName = "My School", UseSsl = true, Credential = "smtp-private-password",
            IsDefault = true, IsActive = true
        });
        smtp.Success.Should().BeTrue(smtp.Message);
        var rows = (await service.GetGatewaysAsync()).Data!;
        var smsRow = rows.Single(x => x.Channel == NotificationChannelType.Sms);
        smsRow.ProviderCode.Should().Be("BULKSMSBD");
        smsRow.SmsSenderId.Should().Be("MY-SCHOOL");
        smsRow.HasCredential.Should().BeTrue();
        var smtpRow = rows.Single(x => x.Channel == NotificationChannelType.Email);
        smtpRow.SmtpHost.Should().Be("smtp.example.com");
        smtpRow.SmtpPort.Should().Be(587);
        smtpRow.FromEmail.Should().Be("no-reply@example.com");
        smtpRow.UseSsl.Should().BeTrue();
        smtpRow.HasCredential.Should().BeTrue();
        var stored = await db.CommunicationGateways.ToListAsync();
        stored.Single(x => x.Channel == NotificationChannelType.Sms)
            .ProtectedCredential.Should().NotContain("sms-private-token");
        stored.Single(x => x.Channel == NotificationChannelType.Email)
            .ProtectedCredential.Should().NotContain("smtp-private-password");
        (await Create(db, 202).GetGatewaysAsync()).Data.Should().BeEmpty();
    }

    [Theory]
    [InlineData("http://sms.example.com/send")]
    [InlineData("https://127.0.0.1/send")]
    [InlineData("https://10.0.0.5/send")]
    [InlineData("https://localhost/send")]
    public async Task Enabled_sms_rejects_non_public_or_insecure_endpoint(string endpoint)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("sms-security-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        var response = await Create(db, 101).SaveGatewayAsync(null, new SaveCommunicationGatewayRequestDto
        {
            ProviderCode = "CUSTOM", Channel = NotificationChannelType.Sms,
            Endpoint = endpoint, SmsSenderId = "SENDER", Credential = "secret",
            IsActive = true, IsDefault = true
        });
        response.Success.Should().BeFalse();
        (await db.CommunicationGateways.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Enabled_smtp_requires_tls_and_public_host()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("smtp-security-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        var service = Create(db, 101);
        var noTls = await service.SaveGatewayAsync(null, new SaveCommunicationGatewayRequestDto
        {
            ProviderCode = "SMTP", Channel = NotificationChannelType.Email,
            SmtpHost = "smtp.example.com", SmtpPort = 587,
            SmtpUsername = "u@example.com", FromEmail = "u@example.com",
            Credential = "secret", UseSsl = false, IsActive = true
        });
        noTls.StatusCode.Should().Be(400);
        var privateHost = await service.SaveGatewayAsync(null, new SaveCommunicationGatewayRequestDto
        {
            ProviderCode = "SMTP", Channel = NotificationChannelType.Email,
            SmtpHost = "192.168.0.10", SmtpPort = 587,
            SmtpUsername = "u@example.com", FromEmail = "u@example.com",
            Credential = "secret", UseSsl = true, IsActive = true
        });
        privateHost.StatusCode.Should().Be(400);
        (await db.CommunicationGateways.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Switching_sms_provider_moves_default_without_removing_previous_credentials()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("sms-switch-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        var service = Create(db, 101);
        foreach (var provider in new[] { "BULKSMSBD", "TWILIO" })
        {
            var saved = await service.SaveGatewayAsync(null, new SaveCommunicationGatewayRequestDto
            {
                ProviderCode = provider, Channel = NotificationChannelType.Sms,
                Endpoint = "https://api.example.com/send", SmsSenderId = "SCHOOL",
                Credential = "different-secret-" + provider, IsActive = true, IsDefault = true
            });
            saved.Success.Should().BeTrue(saved.Message);
        }
        var rows = (await service.GetGatewaysAsync()).Data!;
        rows.Where(x => x.IsDefault && x.Channel == NotificationChannelType.Sms)
            .Select(x => x.ProviderCode).Should().Equal("TWILIO");
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(x => x.HasCredential);
    }

    private static CommunicationAdministrationService Create(EduOSDbContext db, long tenant) => new(
        new GenericRepository<CommunicationGateway>(db), new GenericRepository<MessageTemplate>(db),
        new GenericRepository<NoticeCategory>(db), new InMemoryUnitOfWork(db), new User(tenant),
        new EphemeralDataProtectionProvider(),
        NullLogger<CommunicationAdministrationService>.Instance);

    private sealed class InMemoryUnitOfWork(EduOSDbContext db) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) => operation(ct);
        // Test-only EF InMemory provider has no relational isolation level.
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation, System.Data.IsolationLevel isolationLevel,
            CancellationToken cancellationToken = default) => operation(cancellationToken);
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
