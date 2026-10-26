using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportDriverLicenseImpactTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);

    [Theory]
    [InlineData(true, 0, 0, true)]
    [InlineData(true, 10, 0, true)]
    [InlineData(true, -1, 0, false)]
    [InlineData(true, 10, 1, false)]
    [InlineData(false, -10, 0, true)]
    [InlineData(false, 10, 1, false)]
    public void License_status_preflight_fails_closed_when_coverage_would_break(
        bool active, int expiryOffset, int blocking, bool expected)
    {
        Assert.Equal(expected, TransportService.CanApplyDriverLicenseChange(
            active, Today.AddDays(expiryOffset), Today, blocking));
    }

    [Fact]
    public void Missing_expiry_is_rejected_for_active_drivers()
    {
        Assert.False(TransportService.CanApplyDriverLicenseChange(true, null, Today, 0));
        Assert.True(TransportService.CanApplyDriverLicenseChange(false, null, Today, 0));
    }

    [Fact]
    public void Preview_and_save_reuse_canonical_tenant_scoped_assignment_guards()
    {
        var service = Read("EduOS.Service", "Services", "Transport", "TransportService.DriverRegistry.cs");
        var api = Read("EduOS.App", "Controllers", "Api", "TransportController.cs");
        var view = Read("EduOS.App", "Views", "TransportOperations", "_DriverRegistry.cshtml");
        var js = Read("EduOS.App", "wwwroot", "js", "transport-operations", "driver-registry.js");
        Assert.Contains("PreviewDriverLicenseImpactAsync", service);
        Assert.Contains("x.TenantId == tenant && !x.IsDeleted && x.TransportDriverId == driver.Id", service);
        Assert.Contains("x.IsCurrent && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today)", service);
        Assert.Contains("existing.RowVersion.AsSpan().SequenceEqual(version)", service);
        Assert.Contains("IsolationLevel.Serializable", service);
        Assert.Contains("if (!request.IsActive &&", service);
        Assert.Contains("|| request.IsActive", service);
        Assert.Contains("HttpGet(\"driver-registry/{driverId:long}/license-impact\")", api);
        Assert.Contains("Authorize(Roles = \"TenantAdmin\")", api);
        Assert.Contains("transportDriverLicenseImpactCheck", view);
        Assert.Contains("RequestVerificationToken", js);
        Assert.Contains("URLSearchParams", js);
        Assert.Contains("impactBox.textContent", js);
    }

    private static string Read(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Missing repository source: " + Path.Combine(segments));
    }
}
