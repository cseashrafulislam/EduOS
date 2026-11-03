using EduOS.App.Authorization;
using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ResearchEthicsContractTests
{
    [Fact]
    public void Research_page_and_api_are_entitlement_and_csrf_gated()
    {
        Assert.Contains(typeof(ResearchProposalsController).GetCustomAttributes(true),
            a => a is RequireModuleAttribute m && m.Policy == "EduOSModule:RESEARCH");
        Assert.Contains(typeof(ResearchProposalsApiController).GetCustomAttributes(true),
            a => a is RequireModuleAttribute m && m.Policy == "EduOSModule:RESEARCH");
        Assert.Contains(typeof(ResearchProposalsApiController).GetCustomAttributes(true),
            a => a is AutoValidateAntiforgeryTokenAttribute);
    }

    [Fact]
    public void Research_schema_and_service_preserve_revision_audit_and_separation()
    {
        var migration = Read("EduOS.Persistence", "Migrations", "20261103090000_ResearchEthicsWorkflow.cs");
        var service = Read("EduOS.Service", "Services", "Research", "ResearchProposalService.cs");
        Assert.Contains("ResearchProposalRevisions", migration);
        Assert.Contains("ResearchProposalEvents", migration);
        Assert.Contains("UX_ResearchProposalEvents_Request", migration);
        Assert.Contains("CK_ResearchProposals_State", migration);
        Assert.Contains("rowversion", migration);
        Assert.Contains("UserCampusAccesses", service);
        Assert.Contains("TenantMemberships", service);
        Assert.Contains("IsolationLevel.Serializable", service);
        Assert.Contains("FixedTimeEquals", service);
        Assert.Contains("row.OwnerUserId != user.UserId", service);
        Assert.Contains("row.SupervisorUserId != user.UserId && row.ReviewerUserId != user.UserId", service);
        Assert.Contains("DeclaresNoConflict", service);
        Assert.Contains("ResultSnapshot", service);
        Assert.DoesNotContain("IgnoreQueryFilters().AnyAsync", service);
    }

    [Fact]
    public void Research_is_opt_in_not_installed_default()
    {
        var seeder = Read("EduOS.Persistence", "Seed", "PlatformCatalogSeeder.cs");
        var optional = Read("EduOS.App", "Authorization", "OptionalModuleNavigation.cs");
        var pilot = Read("EduOS.App", "Authorization", "ModuleAccessHandler.cs");
        Assert.Contains("Module(\"RESEARCH\"", seeder);
        Assert.Contains("Module(\"ACCREDITATION\"", seeder);
        Assert.Contains("\"RESEARCH\", \"ACCREDITATION\"", optional);
        Assert.Contains("\"RESEARCH\", \"ACCREDITATION\"", pilot);
        Assert.DoesNotContain("common.Concat(new[] { \"RESEARCH\"", seeder);
    }

    private static string Read(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", pathParts));
    }
}
