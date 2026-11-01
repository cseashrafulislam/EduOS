using EduOS.App.Authorization;
using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class CareerEmployerContractTests
{
    [Fact]
    public void Page_and_api_are_role_module_and_csrf_gated()
    {
        Assert.Contains(typeof(CareerEmployersController).GetCustomAttributes(true),
            a => a is RequireModuleAttribute m && m.Policy == "EduOSModule:ALUMNI");
        Assert.Contains(typeof(CareerEmployersController).GetCustomAttributes(true),
            a => a is AuthorizeAttribute auth && auth.Roles == "TenantAdmin");
        Assert.Contains(typeof(AlumniApiController).GetCustomAttributes(true),
            a => a is AutoValidateAntiforgeryTokenAttribute);
        foreach (var name in new[] { "CreateEmployer", "VerifyEmployer", "Employers", "EmployerReport" })
        {
            Assert.Contains(typeof(AlumniApiController).GetMethod(name)!.GetCustomAttributes(true),
                a => a is AuthorizeAttribute auth && auth.Roles == "TenantAdmin");
        }
    }
    [Fact]
    public void Career_service_and_schema_enforce_tenant_campus_replay_and_independent_verification()
    {
        var service = Read("EduOS.Service", "Services", "Alumni", "AlumniCareerEmployerService.cs");
        Assert.Contains("UserCampusAccesses", service);
        Assert.Contains("current.CreatedByUserId == actor", service);
        Assert.Contains("FixedTimeEquals", service);
        Assert.Contains("IsolationLevel.Serializable", service);
        Assert.Contains("ClientRequestId", service);
        Assert.Contains("Status != 1", service);
        Assert.DoesNotContain("IgnoreQueryFilters", service);
        var migration = Read("EduOS.Persistence", "Migrations", "20261101110000_CareerEmployerVerification.cs");
        Assert.Contains("UX_CareerEmployers_Tenant_Campus_Name", migration);
        Assert.Contains("UX_CareerEmployerDecisions_Tenant_Request", migration);
        Assert.Contains("FK_CareerEmployers_Campus", migration);
        Assert.Contains("RowVersion", migration);
    }
    private static string Read(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }
}
