using System.Security.Claims;

namespace EduOS.App.Authorization;

/// <summary>
/// V2 role-specific shortcuts. The module set comes from the current tenant's
/// live entitlement query. Links are presentation only; controller authorization
/// and resource ownership remain mandatory on every request.
/// </summary>
public sealed record V2RolePortalLink(string ModuleCode, string Path, string LabelKey);

public static class V2RolePortalNavigation
{
    public static IReadOnlyList<V2RolePortalLink> GetLinks(
        IReadOnlySet<string> availableModules, ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return [];
        var links = new List<V2RolePortalLink>();
        void Add(string module, string path, string label, params string[] roles)
        {
            if (availableModules.Contains(module) && roles.Any(user.IsInRole))
                links.Add(new V2RolePortalLink(module, path, label));
        }

        Add("LMS", "/Learning/CourseCompletions", "V2CourseCompletions", "TenantAdmin", "Principal", "Teacher");
        Add("LMS", "/Learning/MySubmissions", "V2MySubmissions", "Student");
        Add("LIBRARY", "/Library/Circulation", "V2LibraryCirculation", "TenantAdmin", "Principal", "Librarian");
        Add("LIBRARY", "/Library/Digital", "V2DigitalResources", "TenantAdmin", "Principal", "Librarian", "Student", "Teacher", "Employee", "Staff");
        Add("HOSTEL", "/hostel/guardian-stays/print", "V2GuardianHostelPrint", "Guardian", "Parent");
        Add("HOSTEL", "/hostel/my-history/print", "V2MyHostelHistory", "Student", "Staff");
        Add("TRANSPORT", "/TransportOperations/Family", "V2FamilyTransport", "Student", "Guardian", "Parent");
        Add("TRANSPORT", "/TransportOperations/Trips", "V2TripDispatch", "TenantAdmin", "Principal", "TransportManager");
        Add("TRANSPORT", "/TransportOperations/DriverTrips", "V2MyDriverTrips", "Staff");
        return links;
    }
}
