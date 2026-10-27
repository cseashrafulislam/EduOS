using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
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
    public async Task Canonical_legal_name_and_registration_number_round_trip_without_erasing_omitted_fields()
    {
        await using var setup = await CreateSetupAsync();
        var changed = await setup.Service.UpdateProfileAsync(new UpdateTenantProfileRequestDto
        {
            Name = "Green School and College", Email = setup.Tenant.Email, CountryCode = "BD",
            LegalName = "Green School Limited", RegistrationNumber = "REG-2026-100",
            RowVersion = Convert.ToBase64String(setup.Tenant.RowVersion)
        });
        changed.Success.Should().BeTrue(changed.Message);
        changed.Data!.LegalName.Should().Be("Green School Limited");
        changed.Data.RegistrationNumber.Should().Be("REG-2026-100");
        setup.Tenant.LegalName.Should().Be("Green School Limited");
        setup.Tenant.RegistrationNumber.Should().Be("REG-2026-100");

        var next = await setup.Service.UpdateProfileAsync(new UpdateTenantProfileRequestDto
        {
            Name = "Green School and College", Email = setup.Tenant.Email, CountryCode = "BD",
            RowVersion = Convert.ToBase64String(setup.Tenant.RowVersion)
        });
        next.Success.Should().BeTrue(next.Message);
        setup.Tenant.LegalName.Should().Be("Green School Limited");
        setup.Tenant.RegistrationNumber.Should().Be("REG-2026-100");
    }

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
    public async Task Regional_preferences_save_canonical_values_and_reject_bad_format_or_stale_version()
    {
        await using var setup = await CreateSetupAsync();
        var valid = await setup.Service.UpdateRegionalSettingsAsync(new UpdateTenantRegionalSettingsRequestDto
        {
            CurrencyCode = "USD", TimeZoneId = "Asia/Dhaka", DefaultLanguage = "en",
            DateFormat = "yyyy-MM-dd", RowVersion = Convert.ToBase64String(setup.Tenant.RowVersion)
        });
        valid.Success.Should().BeTrue(valid.Message);
        setup.Tenant.CurrencyCode.Should().Be("USD");
        setup.Tenant.TimeZoneId.Should().Be("Asia/Dhaka");
        setup.Tenant.DefaultLanguage.Should().Be("en");
        setup.Tenant.DateFormat.Should().Be("yyyy-MM-dd");
        valid.Data!.DateFormat.Should().Be("yyyy-MM-dd");
        valid.Data.CurrencyCode.Should().Be("USD");

        var bad = await setup.Service.UpdateRegionalSettingsAsync(new UpdateTenantRegionalSettingsRequestDto
        {
            CurrencyCode = "USD", TimeZoneId = "Asia/Dhaka", DefaultLanguage = "en",
            DateFormat = "yyyy/MM/dd", RowVersion = Convert.ToBase64String(setup.Tenant.RowVersion)
        });
        bad.Success.Should().BeFalse();
        bad.StatusCode.Should().Be(400);
        setup.Tenant.DateFormat.Should().Be("yyyy-MM-dd");

        var stale = await setup.Service.UpdateRegionalSettingsAsync(new UpdateTenantRegionalSettingsRequestDto
        {
            CurrencyCode = "BDT", TimeZoneId = "Asia/Dhaka", DefaultLanguage = "en",
            DateFormat = "dd-MM-yyyy", RowVersion = Convert.ToBase64String(new byte[8])
        });
        stale.StatusCode.Should().Be(409);
        setup.Tenant.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void Regional_preferences_wizard_posts_to_canonical_endpoint_with_csrf_and_rowversion()
    {
        var view = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "GeneralSettings.cshtml"));
        var js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "general-settings.js"));
        var controller = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "TenantProfileController.cs"));
        view.Should().Contain("@Html.AntiForgeryToken()");
        view.Should().Contain("id=\"currencySymbol\" maxlength=\"10\" readonly");
        controller.Should().Contain("[HttpPut(\"regional-settings\")]");
        js.Should().Contain("'/api/tenant-profile/regional-settings'");
        js.Should().Contain("currencyCode: valueOf('currency')");
        js.Should().Contain("timeZoneId: valueOf('timeZone')");
        js.Should().Contain("defaultLanguage: valueOf('language')");
        js.Should().Contain("dateFormat: valueOf('dateFormat')");
        js.Should().Contain("rowVersion: currentRowVersion");
        js.Should().Contain("'RequestVerificationToken': antiforgeryToken()");
        js.Should().Contain("responseError(payload, i18n.saveFailed");
        js.Should().Contain("'/api/onboarding/complete-step'");
        js.Should().NotContain("/api/tenant-profile/general-settings");
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


    [Fact]
    public async Task Configuration_mutations_write_private_value_free_tenant_audit_and_paged_readback()
    {
        await using var setup = await CreateSetupAsync();
        var version = Convert.ToBase64String(setup.Tenant.RowVersion);
        (await setup.Service.UpdateProfileAsync(new UpdateTenantProfileRequestDto
        {
            Name = "Updated institution", Email = setup.Tenant.Email,
            CountryCode = "BD", RowVersion = version
        })).Success.Should().BeTrue();
        (await setup.Service.UpdateRegionalSettingsAsync(new UpdateTenantRegionalSettingsRequestDto
        {
            CurrencyCode = "USD", TimeZoneId = "Asia/Dhaka",
            DefaultLanguage = "en", DateFormat = "yyyy-MM-dd", RowVersion = version
        })).Success.Should().BeTrue();
        (await setup.Service.UpdateSubdomainAsync(new UpdateTenantSubdomainRequestDto
        {
            Subdomain = "green-school-new", RowVersion = version
        })).Success.Should().BeTrue();
        setup.Db.AuditLogs.Add(new AuditLog
        {
            TenantId = 999, EntityName = nameof(Tenant), EntityId = 999,
            Action = "TenantProfileUpdated", NewValue = "Other institution",
            OccurredAt = DateTime.UtcNow.AddMinutes(1)
        });
        await setup.Db.SaveChangesAsync();
        var first = (await setup.Service.GetChangeHistoryAsync(1, 2)).Data!;
        var second = (await setup.Service.GetChangeHistoryAsync(2, 2)).Data!;
        first.TotalCount.Should().Be(3);
        first.Items.Should().HaveCount(2);
        second.Items.Should().ContainSingle();
        first.Items.Concat(second.Items).Select(x => x.Action).Should().BeEquivalentTo(
            new[] { "TenantProfileUpdated", "TenantRegionalSettingsUpdated", "TenantSubdomainUpdated" });
        setup.Db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == 812)
            .Should().OnlyContain(x => x.UserId == 71 && x.EntityId == 812 &&
                !string.IsNullOrWhiteSpace(x.RequestId) && x.NewValue != null &&
                !x.NewValue.Contains("admin@example.test"));
    }

    [Fact]
    public async Task Branding_removal_is_audited_without_exposing_private_file_url()
    {
        await using var setup = await CreateSetupAsync();
        setup.Tenant.LogoUrl = "/private/old-logo.png";
        await setup.Db.SaveChangesAsync();
        setup.Storage.Setup(x => x.DeleteAsync("/private/old-logo.png")).ReturnsAsync(true);
        (await setup.Service.RemoveLogoAsync(Convert.ToBase64String(setup.Tenant.RowVersion)))
            .Success.Should().BeTrue();
        var history = (await setup.Service.GetChangeHistoryAsync()).Data!;
        history.Items.Should().ContainSingle(x =>
            x.Action == "TenantLogoRemoved" && x.ChangedFields == "Logo");
        history.Items[0].ChangedFields.Should().NotContain("/private/");
    }

    [Fact]
    public void Institution_history_ui_has_authorized_api_and_safe_dom_rendering()
    {
        var view = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "InstitutionManagement.cshtml"));
        var js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "institution-management.js"));
        var api = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "TenantProfileController.cs"));
        view.Should().Contain("id=\"institutionHistoryBody\"");
        view.Should().Contain("id=\"institutionHistoryPrevious\"");
        js.Should().Contain("'/api/tenant-profile/history?page='");
        js.Should().Contain("body.replaceChildren()");
        js.Should().Contain("make('td', entry.changedFields");
        api.Should().Contain("[HttpGet(\"history\")]");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
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
            new GenericRepository<InstitutionTypeDefinition>(db), new GenericRepository<AuditLog>(db),
            new TestUser(812),
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
