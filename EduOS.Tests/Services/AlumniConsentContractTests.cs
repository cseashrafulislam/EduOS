using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AlumniConsentContractTests
{
    [Fact]
    public void All_alumni_routes_require_the_dedicated_entitlement_and_csrf_on_writes()
    {
        var api = typeof(AlumniApiController);
        Assert.Contains(api.GetCustomAttributes(typeof(RequireModuleAttribute), true),
            x => ((RequireModuleAttribute)x).Policy == "EduOSModule:ALUMNI");
        Assert.NotEmpty(api.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute), true));
        var directory = api.GetMethod(nameof(AlumniApiController.Directory))!;
        Assert.Contains(directory.GetCustomAttributes(typeof(AuthorizeAttribute), true),
            x => ((AuthorizeAttribute)x).Roles == "TenantAdmin");
        var export = api.GetMethod(nameof(AlumniApiController.ExportPage))!;
        Assert.Contains(export.GetCustomAttributes(typeof(AuthorizeAttribute), true),
            x => ((AuthorizeAttribute)x).Roles == "TenantAdmin");
    }

    [Fact]
    public void Consent_is_bound_to_verified_student_completion_and_cannot_be_published_without_opt_in()
    {
        var source = Read("EduOS.Service", "Services", "Alumni", "AlumniService.cs");
        Assert.Contains("student.UserId == userId", source);
        Assert.Contains("student.TenantId == tenantId", source);
        Assert.Contains("exit.ExitType == StudentExitType.Completed", source);
        Assert.Contains("exit.FeesCleared", source);
        Assert.Contains("enrollment.State == EnrollmentState.Completed", source);
        Assert.Contains("IsolationLevel.Serializable", source);
        Assert.Contains("CryptographicOperations.FixedTimeEquals", source);
        Assert.Contains("DirectoryOptIn = 1", source);
        Assert.Contains("p.CampusId = en.CampusId", source);
        Assert.Contains("UserCampusAccesses", source);
        Assert.Contains("AlumniConsentCommands", source);
        Assert.Contains("AlumniProfileAudits", source);
        Assert.DoesNotContain("IgnoreQueryFilters", source);
    }

    [Fact]
    public void Migration_enforces_tenant_owned_unique_profiles_and_private_contact_clearing()
    {
        var source = Read("EduOS.Persistence", "Migrations", "20261031220000_V3AlumniConsentDirectory.cs");
        Assert.Contains("UX_AlumniProfiles_Tenant_Student", source);
        Assert.Contains("UX_AlumniConsentCommands_Tenant_Request", source);
        Assert.Contains("CK_AlumniProfiles_PrivateContact", source);
        Assert.Contains("StudentExitRecordId", source);
        Assert.Contains("RowVersion", source);
    }

    private static string Read(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var file = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(file)) return File.ReadAllText(file);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }
}
