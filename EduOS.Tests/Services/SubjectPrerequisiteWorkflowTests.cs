using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.Academic;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class SubjectPrerequisiteWorkflowTests
{
    [Fact]
    public void Rejects_direct_self_dependency() =>
        SubjectPrerequisiteService.CreatesCycle(10, 10, Array.Empty<(long, long)>()).Should().BeTrue();

    [Fact]
    public void Rejects_transitive_cycles()
    {
        var graph = new (long SubjectId, long PrerequisiteSubjectId)[] { (20, 10), (30, 20) };
        SubjectPrerequisiteService.CreatesCycle(10, 30, graph).Should().BeTrue();
    }

    [Fact]
    public void Accepts_acyclic_dependency()
    {
        var graph = new (long SubjectId, long PrerequisiteSubjectId)[] { (20, 10), (30, 20) };
        SubjectPrerequisiteService.CreatesCycle(40, 30, graph).Should().BeFalse();
    }

    [Fact]
    public void Ignores_unrelated_graph_components()
    {
        var graph = new (long SubjectId, long PrerequisiteSubjectId)[] { (90, 80), (80, 70) };
        SubjectPrerequisiteService.CreatesCycle(20, 10, graph).Should().BeFalse();
    }

    [Fact]
    public void Api_requires_tenant_roles_antiforgery_and_rate_limit()
    {
        var type = typeof(SubjectPrerequisitesController);
        type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Should().NotBeEmpty();
        type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true).Should().NotBeEmpty();
        type.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Should().NotBeEmpty();
        foreach (var name in new[] { "Create", "Update", "Archive" })
        {
            var method = type.GetMethod(name)!;
            var auth = method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>().Single();
            auth.Roles.Should().Contain("TenantAdmin").And.Contain("Principal");
            auth.Roles.Should().NotContain("Teacher");
        }
    }

    [Fact]
    public void Uses_canonical_subject_prerequisite_contract()
    {
        var contract = typeof(ISubjectPrerequisiteService);
        contract.GetMethods().Should().HaveCount(5);
        typeof(SubjectPrerequisiteService).GetInterfaces().Should().Contain(contract);
        contract.GetMethod("CreateAsync")!.GetParameters()[0].ParameterType
            .Should().Be(typeof(SaveSubjectPrerequisiteRequestDto));
    }
}
