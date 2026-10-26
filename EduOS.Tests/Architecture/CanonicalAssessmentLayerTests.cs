using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.AssessmentAdministration;
using EduOS.Service.Services.Tenants;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Xunit;

namespace EduOS.Tests.Architecture;

public sealed class CanonicalAssessmentLayerTests
{
    [Fact]
    public void Canonical_implementations_match_their_service_interfaces()
    {
        typeof(IAssessmentAdministrationService).IsAssignableFrom(typeof(AssessmentAdministrationService))
            .Should().BeTrue();
        typeof(IInstitutionRegistrationService).IsAssignableFrom(typeof(InstitutionRegistrationService))
            .Should().BeTrue();
    }

    [Fact]
    public void Assessment_api_navigation_and_browser_share_one_canonical_route()
    {
        var route = typeof(AssessmentAdministrationApiController)
            .GetCustomAttribute<RouteAttribute>()?.Template;
        route.Should().Be("api/assessment-administration");
        var root = RepositoryRoot();
        var scripts = Path.Combine(root, "EduOS.App", "wwwroot", "js", "assessment");
        var views = Path.Combine(root, "EduOS.App", "Views", "AssessmentAdministration");
        Directory.Exists(scripts).Should().BeTrue();
        Directory.Exists(views).Should().BeTrue();
        File.ReadAllText(Path.Combine(root, "EduOS.App", "Views", "Shared", "_Layout.cshtml"))
            .Should().Contain("/AssessmentAdministration/Index");
        foreach (var script in Directory.GetFiles(scripts, "*.js", SearchOption.TopDirectoryOnly))
            File.ReadAllText(script).Should().Contain("/" + route + "/");
    }

    [Fact]
    public void Retired_assessment_implementation_and_presentation_names_do_not_return()
    {
        var root = RepositoryRoot();
        foreach (var project in new[] { "EduOS.Service", "EduOS.App" })
        {
            var sourceRoot = Path.Combine(root, project);
            var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                .Where(file => !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                    !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                    (file.EndsWith(".cs") || file.EndsWith(".cshtml") || file.EndsWith(".js")));
            foreach (var file in files)
            {
                var source = File.ReadAllText(file);
                source.Should().NotContain("ExamWorkflow", because: file);
                source.Should().NotContain("ExamOperations", because: file);
                source.Should().NotContain("InstitutionOnboardingService", because: file);
            }
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EduOS.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("EduOS solution root not found.");
    }
}
