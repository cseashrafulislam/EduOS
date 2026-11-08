using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Students;
using EduOS.Core.DTOs.Students;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests;

public sealed class StudentSupportA3A4ContractsTests
{
    [Theory]
    [InlineData(typeof(ScholarshipApplication))]
    [InlineData(typeof(ScholarshipApplicationEvent))]
    [InlineData(typeof(CounselingCase))]
    [InlineData(typeof(CounselingCaseEvent))]
    public void New_student_support_entities_are_tenant_isolated_and_versioned(Type entityType)
    {
        using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("student-support-model-" + Guid.NewGuid().ToString("N")).Options);
        var entity = db.Model.FindEntityType(entityType);
        entity.Should().NotBeNull();
        entity!.GetQueryFilter().Should().NotBeNull();
        entity.FindProperty("RowVersion")!.IsConcurrencyToken.Should().BeTrue();
        entity.GetKeys().Should().Contain(x =>
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "Id" }),
            "new child records reference tenant+ID composite ownership");
        entity.GetIndexes().Should().Contain(x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "ClientRequestId" }));
    }

    [Theory]
    [InlineData(typeof(ScholarshipApplicationsController), "STUDENT")]
    [InlineData(typeof(CounselingCasesController), "STUDENT")]
    [InlineData(typeof(StudentSupportDirectoryController), "STUDENT")]
    public void Student_support_apis_require_tenant_module_csrf_no_store_and_rate_limit(
        Type type, string module)
    {
        type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Should().NotBeEmpty();
        var modules = type.GetCustomAttributes(typeof(RequireModuleAttribute), true)
            .Cast<RequireModuleAttribute>().Select(x => x.Policy).ToList();
        modules.Should().Contain(RequireModuleAttribute.PolicyPrefix + module);
        type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true)
            .Should().NotBeEmpty();
        type.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)
            .Cast<EnableRateLimitingAttribute>().Should().ContainSingle(x => x.PolicyName == "ApiPolicy");
        type.GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Cast<ResponseCacheAttribute>().Should().ContainSingle(x => x.NoStore);
    }

    [Theory]
    [InlineData(nameof(ScholarshipApplicationsController.Submit))]
    [InlineData(nameof(ScholarshipApplicationsController.Decide))]
    [InlineData(nameof(ScholarshipApplicationsController.Award))]
    [InlineData(nameof(ScholarshipApplicationsController.Cancel))]
    public void All_scholarship_decisions_and_financial_actions_require_post(string method)
    {
        typeof(ScholarshipApplicationsController).GetMethods().Single(x => x.Name == method)
            .GetCustomAttributes(typeof(HttpPostAttribute), true).Should().ContainSingle();
    }

    [Theory]
    [InlineData(nameof(CounselingCasesController.RequestCase))]
    [InlineData(nameof(CounselingCasesController.Assign))]
    [InlineData(nameof(CounselingCasesController.Action))]
    [InlineData(nameof(CounselingCasesController.PrivateView))]
    [InlineData(nameof(CounselingCasesController.GuardianContactConsent))]
    public void Confidential_case_writes_and_access_are_post_only(string method)
    {
        typeof(CounselingCasesController).GetMethods().Single(x => x.Name == method)
            .GetCustomAttributes(typeof(HttpPostAttribute), true).Should().ContainSingle();
    }

    [Fact]
    public void Private_scholarship_evidence_is_audited_post_not_a_public_get()
    {
        var route = typeof(ScholarshipApplicationsController).GetMethods()
            .Single(x => x.Name == nameof(ScholarshipApplicationsController.DownloadEvidence));
        route.GetCustomAttributes(typeof(HttpPostAttribute), true)
            .Should().ContainSingle();
        route.GetCustomAttributes(typeof(HttpGetAttribute), true).Should().BeEmpty();
        var upload = typeof(ScholarshipApplicationsController).GetMethods()
            .Single(x => x.Name == nameof(ScholarshipApplicationsController.UploadEvidence));
        upload.GetCustomAttributes(typeof(HttpPostAttribute), true).Should().ContainSingle();
    }

    [Fact]
    public void Confidential_aggregate_report_contains_no_case_or_student_identifiers()
    {
        var fields = typeof(CounselingCampusAggregate).GetProperties()
            .Select(x => x.Name).ToArray();
        fields.Should().BeEquivalentTo(new[] { "CampusId", "CaseCount" });
        typeof(CounselingAggregateReport).GetProperties()
            .Should().Contain(x => x.Name == "MinimumDisclosureCount");
        typeof(CounselingCasesController).GetMethods()
            .Single(x => x.Name == nameof(CounselingCasesController.Aggregates))
            .GetCustomAttributes(typeof(HttpGetAttribute), true).Should().ContainSingle();
    }
}
