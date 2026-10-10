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

public sealed class AcademicSetupWorkflowContractTests
{
    [Fact]
    public void Academic_setup_api_enforces_auth_antiforgery_rate_limit_and_privileged_mutations()
    {
        var type = typeof(AcademicSetupController);
        type.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Should().NotBeEmpty();
        type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true)
            .Should().NotBeEmpty();
        type.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)
            .Should().NotBeEmpty();
        type.GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Should().NotBeEmpty();
        foreach (var method in type.GetMethods().Where(x =>
            x.GetCustomAttributes(typeof(HttpPostAttribute), true).Length > 0))
        {
            var authorize = method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>().Single();
            authorize.Roles.Should().Contain("TenantAdmin");
            authorize.Roles.Should().Contain("Principal");
            method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Should().BeEmpty();
        }
    }

    [Fact]
    public void Setup_service_implements_canonical_nine_method_contract()
    {
        var iface = typeof(IAcademicSetupService);
        var implementation = typeof(AcademicSetupService);
        iface.GetMethods().Should().HaveCount(9);
        implementation.GetInterfaces().Should().Contain(iface);
        iface.GetMethod(nameof(IAcademicSetupService.CreateProgramAsync))!
            .GetParameters()[0].ParameterType.Should().Be(typeof(SaveAcademicProgramRequestDto));
        iface.GetMethod(nameof(IAcademicSetupService.CreateLevelAsync))!
            .GetParameters()[0].ParameterType.Should().Be(typeof(SaveAcademicLevelRequestDto));
        iface.GetMethod(nameof(IAcademicSetupService.CreateCurriculumAsync))!
            .GetParameters()[0].ParameterType.Should().Be(typeof(SaveAcademicCurriculumRequestDto));
        iface.GetMethod(nameof(IAcademicSetupService.CreateBatchAsync))!
            .GetParameters()[0].ParameterType.Should().Be(typeof(SaveAcademicBatchRequestDto));
    }

    [Fact]
    public void Academic_setup_source_uses_bounded_queries_and_unit_of_work_transactions()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "EduOS.Service", "Services", "Academic", "AcademicSetupService.cs"));
        source.Should().Contain("ExecuteInTransactionAsync");
        source.Should().Contain("Take(take)");
        source.Should().Contain("x.TenantId == tenant");
        source.Should().Contain("catch (DbUpdateException ex)");
        source.Should().NotContain("CreateExecutionStrategy");
        source.Should().NotContain("TransactionScope");
        source.Should().NotContain("CreateAcademicSubjectDto");
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
