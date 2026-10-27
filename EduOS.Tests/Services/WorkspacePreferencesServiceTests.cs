using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Tenants;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class WorkspacePreferencesServiceTests
{
    [Fact]
    public async Task User_and_institution_preferences_are_private_and_versioned()
    {
        await using var test = Create();
        var owner = Service(test.Db, 101, 71);
        var saved = await owner.SaveAsync(new SaveWorkspacePreferencesRequestDto
        {
            Theme = "dark", Palette = "teal", Navigation = "right", Density = "compact", FontScale = "large",
            DashboardPanels =
            [
                new() { Id = "quick-actions", Visible = false },
                new() { Id = "setup-checklist" },
                new() { Id = "capacity" }
            ]
        });
        Assert.True(saved.Success, saved.Message);
        var row = await test.Db.TenantSettings.SingleAsync();
        Assert.Equal("Workspace.User.71", row.Key);
        Assert.Equal("UserWorkspace", row.Category);
        Assert.DoesNotContain("UserId", row.Value);
        Assert.Equal("dark", (await owner.GetAsync()).Data!.Theme);
        Assert.Equal(new[] { "quick-actions", "setup-checklist", "capacity" },
            (await owner.GetAsync()).Data!.DashboardPanels.Select(x => x.Id));
        Assert.False((await owner.GetAsync()).Data!.DashboardPanels[0].Visible);
        Assert.True((await Service(test.Db, 101, 72).GetAsync()).Data!.DashboardPanels.All(x => x.Visible));
        Assert.Equal("system", (await Service(test.Db, 101, 72).GetAsync()).Data!.Theme);
        Assert.Equal("system", (await Service(test.Db, 202, 71).GetAsync()).Data!.Theme);

        // InMemory does not generate SQL Server rowversion values.
        row.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
        await test.Db.SaveChangesAsync();
        var version = Convert.ToBase64String(row.RowVersion);
        var conflict = await owner.SaveAsync(new SaveWorkspacePreferencesRequestDto
        {
            Theme = "light", RowVersion = Convert.ToBase64String(new byte[] { 8, 7, 6, 5 })
        });
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal("dark", (await owner.GetAsync()).Data!.Theme);
        var changed = await owner.SaveAsync(new SaveWorkspacePreferencesRequestDto
        {
            Theme = "light", RowVersion = version
        });
        Assert.True(changed.Success, changed.Message);
        Assert.Equal("light", (await owner.GetAsync()).Data!.Theme);
    }

    [Theory]
    [InlineData("javascript:alert(1)", "blue", "left", "comfortable", "normal")]
    [InlineData("light", "#fff", "left", "comfortable", "normal")]
    [InlineData("dark", "blue", "outside", "comfortable", "normal")]
    [InlineData("dark", "blue", "left", "broken", "normal")]
    [InlineData("dark", "blue", "left", "comfortable", "url(evil)")]
    public async Task Unsupported_values_never_reach_storage(string theme, string palette,
        string navigation, string density, string font)
    {
        await using var test = Create();
        var response = await Service(test.Db, 101, 71).SaveAsync(new SaveWorkspacePreferencesRequestDto
        {
            Theme = theme, Palette = palette, Navigation = navigation, Density = density, FontScale = font
        });
        Assert.Equal(400, response.StatusCode);
        Assert.Empty(await test.Db.TenantSettings.ToListAsync());
    }

    [Fact]
    public async Task General_institution_settings_api_cannot_read_or_change_private_workspace_rows()
    {
        await using var test = Create();
        var personal = Service(test.Db, 101, 71);
        Assert.True((await personal.SaveAsync(new SaveWorkspacePreferencesRequestDto { Theme = "dark" })).Success);
        var admin = new TenantSettingService(new GenericRepository<TenantSetting>(test.Db),
            new GenericRepository<TenantTerminology>(test.Db), test.Db,
            new TestUser(101, 71), new EphemeralDataProtectionProvider(),
            NullLogger<TenantSettingService>.Instance);
        Assert.Empty((await admin.GetSettingsAsync(null, 1, 20)).Data!.Items);
        Assert.Equal(404, (await admin.GetSettingAsync("Workspace.User.71")).StatusCode);
        Assert.Equal(403, (await admin.SaveSettingAsync(null, new SaveTenantSettingRequestDto
        {
            Key = "Workspace.User.71", Value = "overwritten"
        })).StatusCode);
    }

    [Fact]
    public async Task Reset_keeps_a_versioned_row_and_restores_safe_defaults()
    {
        await using var test = Create();
        var service = Service(test.Db, 101, 71);
        Assert.True((await service.SaveAsync(new SaveWorkspacePreferencesRequestDto { Theme = "dark" })).Success);
        var row = await test.Db.TenantSettings.SingleAsync();
        row.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
        await test.Db.SaveChangesAsync();
        var reset = await service.ResetAsync(new ResetWorkspacePreferencesRequestDto
        {
            RowVersion = Convert.ToBase64String(row.RowVersion)
        });
        Assert.True(reset.Success, reset.Message);
        Assert.Equal("system", reset.Data!.Theme);
        Assert.Equal("blue", reset.Data.Palette);
        Assert.Equal(new[] { "capacity", "quick-actions", "setup-checklist" }, reset.Data.DashboardPanels.Select(x => x.Id));
        Assert.All(reset.Data.DashboardPanels, x => Assert.True(x.Visible));
        Assert.Single(await test.Db.TenantSettings.ToListAsync());
    }

    [Fact]
    public async Task Missing_tenant_context_is_denied_even_for_authenticated_user()
    {
        await using var test = Create();
        Assert.Equal(403, (await Service(test.Db, 0, 71).GetAsync()).StatusCode);
        Assert.Equal(403, (await Service(test.Db, 0, 71).SaveAsync(new SaveWorkspacePreferencesRequestDto())).StatusCode);
    }

    [Fact]
    public async Task Dashboard_panels_reject_duplicate_unknown_partial_and_null_payloads()
    {
        await using var test = Create();
        var service = Service(test.Db, 101, 71);
        List<DashboardPanelPreferenceDto>?[] invalid =
        [
            null,
            [],
            [new() { Id = "capacity" }],
            [new() { Id = "capacity" }, new() { Id = "capacity" }, new() { Id = "setup-checklist" }],
            [new() { Id = "capacity" }, new() { Id = "quick-actions" }, new() { Id = "external" }]
        ];
        foreach (var panels in invalid)
        {
            var response = await service.SaveAsync(new SaveWorkspacePreferencesRequestDto
            {
                DashboardPanels = panels!
            });
            Assert.Equal(400, response.StatusCode);
        }
        Assert.Empty(await test.Db.TenantSettings.ToListAsync());
    }

    [Fact]
    public async Task Corrupted_stored_panel_configuration_falls_back_to_safe_defaults()
    {
        await using var test = Create();
        var service = Service(test.Db, 101, 71);
        Assert.True((await service.SaveAsync(new SaveWorkspacePreferencesRequestDto())).Success);
        var row = await test.Db.TenantSettings.SingleAsync();
        row.Value = """{"SchemaVersion":1,"Theme":"dark","DashboardPanels":[{"Id":"capacity","Visible":false},{"Id":"capacity","Visible":false},{"Id":"external","Visible":false}]}""";
        await test.Db.SaveChangesAsync();
        var response = await service.GetAsync();
        Assert.True(response.Success);
        Assert.Equal("dark", response.Data!.Theme);
        Assert.Equal(new[] { "capacity", "quick-actions", "setup-checklist" },
            response.Data.DashboardPanels.Select(x => x.Id));
        Assert.All(response.Data.DashboardPanels, x => Assert.True(x.Visible));
    }

    private static TestSetup Create()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("workspace-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        return new TestSetup(db);
    }

    private static WorkspacePreferencesService Service(EduOSDbContext db, long tenant, long user) =>
        new(new GenericRepository<TenantSetting>(db), db, new TestUser(tenant, user),
            NullLogger<WorkspacePreferencesService>.Instance);

    private sealed record TestSetup(EduOSDbContext Db) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestUser(long tenant, long user) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => user;
        public long TenantId => tenant;
        public string? FullName => "Test User";
        public string? Email => "user@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
