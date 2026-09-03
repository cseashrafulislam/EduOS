using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Interfaces;
using EduOS.Core.Settings;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Tenants;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public class TenantProfileServiceTests
{
    [Fact]
    public async Task Subdomain_result_uses_the_configured_portal_domain()
    {
        await using var setup = await CreateSetupAsync("schools.eduos.example");

        var result = await setup.Service.CheckSubdomainAvailabilityAsync("green-school");

        result.Success.Should().BeTrue();
        result.Data!.IsAvailable.Should().BeTrue();
        result.Data.FullUrl.Should().Be("https://green-school.schools.eduos.example");
    }

    [Fact]
    public async Task General_settings_reject_values_outside_the_supported_contract()
    {
        await using var setup = await CreateSetupAsync();

        var result = await setup.Service.UpdateGeneralSettingsAsync(
            new UpdateGeneralSettingsDto
            {
                Currency = "BDT",
                CurrencySymbol = "৳",
                TimeZone = "Etc/Untrusted",
                Language = "en",
                DateFormat = "dd-MM-yyyy"
            });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        setup.Tenant.TimeZone.Should().Be("Asia/Dhaka");
    }

    [Fact]
    public async Task Branding_upload_rejects_documents_even_when_global_storage_allows_them()
    {
        await using var setup = await CreateSetupAsync();
        await using var stream = new MemoryStream("%PDF-not-an-image"u8.ToArray());
        var file = new FormFile(stream, 0, stream.Length, "file", "logo.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var result = await setup.Service.UploadLogoAsync(file);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        setup.FileStorage.Verify(
            x => x.UploadAsync(It.IsAny<IFormFile>(), It.IsAny<string>()),
            Times.Never);
    }

    private static async Task<TestSetup> CreateSetupAsync(
        string portalDomain = "eduos.com")
    {
        const long tenantId = 812;
        var httpContext = new DefaultHttpContext();
        httpContext.Items["TenantId"] = tenantId;
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "71"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenantId.ToString())
        ], "TestAuthentication"));
        var accessor = new TestHttpContextAccessor { HttpContext = httpContext };
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"tenant-profile-{Guid.NewGuid():N}")
            .Options;
        var context = new EduOSDbContext(options, accessor);
        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "Green School",
            Code = "GREEN-SCHOOL",
            InstitutionType = "PRIMARY_SCHOOL",
            Email = "admin@example.com",
            OwnerName = "Tenant Owner",
            IsEmailVerified = true
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var fileStorage = new Mock<IFileUploadService>();
        var service = new TenantProfileService(
            new GenericRepository<Tenant>(context),
            context,
            new TestCurrentUser(tenantId),
            fileStorage.Object,
            Options.Create(new FileUploadSettings()),
            Options.Create(new TenantPortalSettings { BaseDomain = portalDomain }),
            NullLogger<TenantProfileService>.Instance);
        return new TestSetup(context, tenant, service, fileStorage);
    }

    private sealed record TestSetup(
        EduOSDbContext Context,
        Tenant Tenant,
        TenantProfileService Service,
        Mock<IFileUploadService> FileStorage) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class TestCurrentUser(long tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 71;
        public long TenantId => tenantId;
        public string? FullName => "Tenant Admin";
        public string? Email => "admin@example.com";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
