using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Core.Settings;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Tenants;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TenantProfileServiceTests
{
    [Fact]
    public async Task Subdomain_validation_rejects_reserved_and_existing_values()
    {
        await using var setup = await CreateSetupAsync();
        (await setup.Service.CheckSubdomainAvailabilityAsync("www")).Data!
            .IsAvailable.Should().BeFalse();
        (await setup.Service.CheckSubdomainAvailabilityAsync("green-school")).Data!
            .IsAvailable.Should().BeTrue();
        setup.Db.Tenants.Add(new Tenant { Name = "Other", Code = "OTHER",
            Email = "other@example.test", Subdomain = "occupied-school" });
        await setup.Db.SaveChangesAsync();
        (await setup.Service.CheckSubdomainAvailabilityAsync("occupied-school")).Data!
            .IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Regional_settings_reject_unknown_time_zone()
    {
        await using var setup = await CreateSetupAsync();
        var response = await setup.Service.UpdateRegionalSettingsAsync(new UpdateTenantRegionalSettingsRequestDto
        {
            CurrencyCode = "BDT", TimeZoneId = "Etc/Unknown-EduOS-Timezone",
            DefaultLanguage = "en-BD",
            RowVersion = Convert.ToBase64String(setup.Tenant.RowVersion)
        });
        response.Success.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        setup.Tenant.TimeZoneId.Should().Be("Asia/Dhaka");
    }

    [Fact]
    public async Task Branding_rejects_pdf_before_invoking_file_storage()
    {
        await using var setup = await CreateSetupAsync();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("%PDF-not-an-image"));
        var result = await setup.Service.UploadLogoAsync(new PrivateFileUploadDto
        {
            FileName = "logo.pdf", ContentType = "application/pdf",
            Length = stream.Length, Content = stream
        });
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        setup.Storage.Verify(x => x.UploadAsync(It.IsAny<Microsoft.AspNetCore.Http.IFormFile>(),
            It.IsAny<string>()), Times.Never);
    }

    private static async Task<TestSetup> CreateSetupAsync()
    {
        var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("tenant-profile-" + Guid.NewGuid().ToString("N")).Options);
        var tenant = new Tenant { Id = 812, Name = "Green School", Code = "GREEN-SCHOOL",
            Email = "admin@example.test", RowVersion = [1, 2, 3, 4, 5, 6, 7, 8] };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var storage = new Mock<IFileUploadService>();
        var service = new TenantProfileService(
            new GenericRepository<Tenant>(db), new GenericRepository<TenantDomain>(db),
            new GenericRepository<InstitutionTypeDefinition>(db), new TestUser(812),
            storage.Object, db, Options.Create(new FileUploadSettings()),
            NullLogger<TenantProfileService>.Instance);
        return new TestSetup(db, tenant, service, storage);
    }

    private sealed record TestSetup(EduOSDbContext Db, Tenant Tenant,
        TenantProfileService Service, Mock<IFileUploadService> Storage) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestUser(long tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => 71;
        public string? FullName => "Admin";
        public string? Email => "admin@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string value) => value == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
