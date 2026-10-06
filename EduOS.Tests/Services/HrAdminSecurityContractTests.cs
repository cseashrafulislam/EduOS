using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public class HrAdminSecurityContractTests
{
    [Fact]
    public void Hr_admin_queries_and_leave_review_keep_tenant_guards()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.Service", "Services", "HR", "HrAdminService.cs"));
        source.Should().Contain("Where(x=>x.TenantId==_user.TenantId)");
        source.Should().Contain("where l.TenantId==t&&l.UserType==\"Employee\"");
        source.Should().Contain("x=>x.TenantId==t&&x.Id==r.Id&&x.UserType==\"Employee\"");
        source.Should().Contain("x=>x.TenantId==t&&x.UserId==row.UserId");
        source.Should().Contain("row.Status!=\"Pending\"");
        source.Should().Contain("Math.Clamp(r.PageSize,1,100)");
    }

    [Fact]
    public void Hr_admin_api_requires_module_and_hr_roles()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.App", "Controllers", "Api", "HrAdminController.cs"));
        source.Should().Contain("[RequireModule(\"HR\")]");
        source.Should().Contain("[Authorize(Roles=\"TenantAdmin,Principal,HR\")]");
        source.Should().Contain("[AutoValidateAntiforgeryToken]");
        source.Should().Contain("[EnableRateLimiting(\"ApiPolicy\")]");
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }
}
