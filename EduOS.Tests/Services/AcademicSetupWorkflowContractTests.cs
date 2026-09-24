using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public class AcademicSetupWorkflowContractTests
{
    [Fact]
    public void Setup_api_requires_module_auth_antiforgery_and_privileged_writes()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.App", "Controllers", "Api", "AcademicSetupController.cs"));
        source.Should().Contain("[Authorize(Roles = \"TenantAdmin,Principal,VicePrincipal,Teacher\")]");
        source.Should().Contain("[RequireModule(\"ACADEMIC\")]");
        source.Should().Contain("[AutoValidateAntiforgeryToken]");
        source.Should().Contain("[EnableRateLimiting(\"ApiPolicy\")]");
        source.Should().Contain("[ResponseCache(NoStore = true");
        source.Should().Contain("[Authorize(Roles = \"TenantAdmin,Principal,VicePrincipal\")]");
        source.Should().NotContain("AllowAnonymous");
    }

    [Fact]
    public void Setup_writes_are_serialized_retry_safe_and_tenant_scoped()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.Service", "Services", "Academic", "AcademicSetupService.cs"));
        source.Should().Contain("IsolationLevel = IsolationLevel.Serializable");
        source.Should().Contain("CreateExecutionStrategy");
        source.Should().Contain("x.TenantId == tenantId");
        source.Should().Contain("Programme already exists.");
        source.Should().Contain("Subject already exists.");
        source.Should().Contain("Academic batch already exists.");
        source.Should().Contain("Academic track already exists.");
        source.Should().Contain("catch (DbUpdateException ex)");
        source.Should().Contain("Reload and try again.\", 409");
    }

    [Fact]
    public void Canonical_subject_schema_is_present_in_the_squashed_baseline()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.Persistence", "Migrations", "20260924114506_InitialCreate.cs"));
        source.Should().Contain("name: \"Subjects\"");
        source.Should().Contain("ClassId = table.Column<long>");
        source.Should().Contain("nullable: true");
        source.Should().Contain("UX_Subjects_Tenant_CanonicalCode");
        source.Should().NotContain("ClassId = table.Column<int>");
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
