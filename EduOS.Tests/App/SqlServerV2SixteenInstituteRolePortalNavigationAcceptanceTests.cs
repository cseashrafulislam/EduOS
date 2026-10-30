using System.Security.Claims;
using EduOS.App.Authorization;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Live SQL entitlement and role-portal matrix for the release cohort.
/// One user switches tenant contexts in both directions without shared cache.
/// </summary>
public sealed partial class SqlServerV2SixteenInstituteEntitlementHttpAcceptanceTests
{
    [Fact]
    public async Task Sixteen_SQL_institutes_scope_bilingual_role_portal_links_to_current_entitlement()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var institutes = await SeedAsync(options);
        var expected = new (string Role, string Module, string Path)[]
        {
            ("Student", "LMS", "/Learning/MySubmissions"),
            ("Teacher", "LMS", "/Learning/CourseCompletions"),
            ("Librarian", "LIBRARY", "/Library/Circulation"),
            ("Student", "LIBRARY", "/Library/Digital"),
            ("Guardian", "HOSTEL", "/hostel/guardian-stays/print"),
            ("Student", "HOSTEL", "/hostel/my-history/print"),
            ("Parent", "TRANSPORT", "/TransportOperations/Family"),
            ("TransportManager", "TRANSPORT", "/TransportOperations/Trips"),
            ("Staff", "TRANSPORT", "/TransportOperations/DriverTrips")
        };

        foreach (var institute in institutes.Concat(institutes.AsEnumerable().Reverse()))
        {
            var admin = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "901"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", institute.TenantId.ToString())
            ], "SqlMatrix"));
            var context = new DefaultHttpContext { User = admin };
            context.Items["TenantId"] = institute.TenantId;
            var accessor = new HttpContextAccessor { HttpContext = context };
            await using var db = new EduOSDbContext(options, accessor);
            var available = await OptionalModuleNavigation.GetAvailableAsync(
                RealModules(db, new SqlMatrixUser(accessor)), false, true);
            foreach (var role in expected.Select(x => x.Role).Distinct())
            {
                var principal = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Role, role)], "SqlMatrix"));
                var links = V2RolePortalNavigation.GetLinks(available, principal);
                Assert.All(links, link => Assert.True(
                    institute.Configuration.Available(link.ModuleCode),
                    $"Unentitled module {link.ModuleCode} in tenant {institute.TenantId}"));
                foreach (var route in expected.Where(x => x.Role == role))
                    Assert.Equal(institute.Configuration.Available(route.Module),
                        links.Any(x => x.Path == route.Path));
            }
        }
    }
}
