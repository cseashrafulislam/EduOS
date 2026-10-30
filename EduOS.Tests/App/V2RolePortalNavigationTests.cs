using System.Security.Claims;
using EduOS.App.Authorization;
using Xunit;

namespace EduOS.Tests.App;

public sealed class V2RolePortalNavigationTests
{
    private static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "LMS", "LIBRARY", "HOSTEL", "TRANSPORT" };

    private static ClaimsPrincipal Principal(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(x => new Claim(ClaimTypes.Role, x)), "Test"));

    [Theory]
    [InlineData("Student", "/Learning/MySubmissions", true)]
    [InlineData("Student", "/Learning/CourseCompletions", false)]
    [InlineData("Teacher", "/Learning/CourseCompletions", true)]
    [InlineData("Guardian", "/hostel/guardian-stays/print", true)]
    [InlineData("Guardian", "/hostel/residents/print", false)]
    [InlineData("HostelWarden", "/hostel/residents/print", true)]
    [InlineData("Parent", "/TransportOperations/Family", true)]
    [InlineData("TransportManager", "/TransportOperations/Trips", true)]
    [InlineData("Staff", "/TransportOperations/DriverTrips", true)]
    [InlineData("Staff", "/Library/Digital", true)]
    [InlineData("Librarian", "/Library/Circulation", true)]
    [InlineData("Student", "/Library/Circulation", false)]
    [InlineData("Accountant", "/TransportOperations/Trips", false)]
    public void Links_match_destination_role_contract(string role, string path, bool expected)
    {
        var links = V2RolePortalNavigation.GetLinks(All, Principal(role));
        Assert.Equal(expected, links.Any(x => x.Path == path));
        Assert.Equal(links.Count, links.Select(x => x.Path).Distinct().Count());
    }

    [Fact]
    public void Anonymous_unentitled_and_tenant_switch_fail_closed()
    {
        Assert.Empty(V2RolePortalNavigation.GetLinks(All, new ClaimsPrincipal()));
        Assert.Empty(V2RolePortalNavigation.GetLinks(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), Principal("TenantAdmin")));
        var first = V2RolePortalNavigation.GetLinks(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "LMS", "LIBRARY" },
            Principal("Teacher", "Student"));
        var second = V2RolePortalNavigation.GetLinks(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "HOSTEL", "TRANSPORT" },
            Principal("Teacher", "Student"));
        Assert.Contains(first, x => x.Path == "/Learning/MySubmissions");
        Assert.DoesNotContain(second, x => x.ModuleCode is "LMS" or "LIBRARY");
        Assert.Contains(second, x => x.Path == "/TransportOperations/Family");
        Assert.DoesNotContain(first, x => x.ModuleCode is "HOSTEL" or "TRANSPORT");
    }

    [Fact]
    public void Shared_Razor_shell_uses_role_links_from_live_entitlement()
    {
        var layout = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "_Layout.cshtml"));
        Assert.Contains("V2RolePortalNavigation.GetLinks(availableOptionalModules, User)", layout);
        foreach (var module in new[] { "LMS", "LIBRARY", "HOSTEL", "TRANSPORT" })
            Assert.Contains("v2RoleLinks.Where(x => x.ModuleCode == \"" + module + "\")", layout);
        Assert.Contains("@T[link.LabelKey]", layout);
    }
}
