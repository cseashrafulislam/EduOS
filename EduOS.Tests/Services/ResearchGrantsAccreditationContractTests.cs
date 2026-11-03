using EduOS.App.Authorization;
using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ResearchGrantsAccreditationContractTests
{
    [Fact]
    public void Grants_and_accreditation_routes_enforce_opt_in_modules_and_csrf()
    {
        Assert.Contains(typeof(ResearchGrantsController).GetCustomAttributes(true),
            x => x is RequireModuleAttribute m && m.Policy == "EduOSModule:RESEARCH");
        Assert.Contains(typeof(ResearchGrantsApiController).GetCustomAttributes(true),
            x => x is RequireModuleAttribute m && m.Policy == "EduOSModule:RESEARCH");
        Assert.Contains(typeof(AccreditationController).GetCustomAttributes(true),
            x => x is RequireModuleAttribute m && m.Policy == "EduOSModule:ACCREDITATION");
        Assert.Contains(typeof(AccreditationApiController).GetCustomAttributes(true),
            x => x is RequireModuleAttribute m && m.Policy == "EduOSModule:ACCREDITATION");
        Assert.Contains(typeof(ResearchGrantsApiController).GetCustomAttributes(true),
            x => x is AutoValidateAntiforgeryTokenAttribute);
        Assert.Contains(typeof(AccreditationApiController).GetCustomAttributes(true),
            x => x is AutoValidateAntiforgeryTokenAttribute);
    }

    [Fact]
    public void Research_budgets_and_accreditation_evidence_have_tenant_scoped_audited_versions()
    {
        var migration = Read("EduOS.Persistence", "Migrations", "20261103120000_ResearchGrantsAccreditation.cs");
        var grants = Read("EduOS.Service", "Services", "Research", "ResearchGrantService.cs");
        var accreditation = Read("EduOS.Service", "Services", "Research", "AccreditationService.cs");
        var access = Read("EduOS.Service", "Services", "Research", "ResearchWorkflowAccess.cs");
        foreach (var table in new[] { "ResearchGrantSchemes", "ResearchGrants", "ResearchGrantBudgets",
                     "ResearchGrantMilestones", "ResearchGrantEvents", "AccreditationFrameworks",
                     "AccreditationStandards", "AccreditationAssessments", "AccreditationEvidenceVersions", "AccreditationEvents" })
            Assert.Contains("CreateTable(\"" + table + "\"", migration);
        Assert.Contains("decimal(19,2)", migration);
        Assert.Contains("rowversion", migration);
        Assert.Contains("UX_ResearchGrantEvents_Request", migration);
        Assert.Contains("UX_AccreditationEvents_Request", migration);
        Assert.Contains("AK_ResearchGrants_Tenant_Id", migration);
        Assert.Contains("AK_AccreditationAssessments_Tenant_Id", migration);
        Assert.Contains("FK_AccreditationAssessments_Program", migration);
        Assert.Contains("IsolationLevel.Serializable", grants);
        Assert.Contains("IsolationLevel.Serializable", accreditation);
        Assert.Contains("FixedTimeEquals", grants);
        Assert.Contains("FixedTimeEquals", accreditation);
        Assert.Contains("Reviewer(row, roles, access.UserId)", accreditation);
        Assert.Contains("DeclaresNoConflict", accreditation);
        Assert.Contains("StateCode != \"EthicsApproved\"", grants);
        Assert.Contains("UserCampusAccesses", access);
        Assert.Contains("TenantMemberships", access);
        Assert.DoesNotContain("INSERT INTO dbo.AccJournal", grants);
    }

    [Fact]
    public void Audit_csv_excludes_confidential_evidence_and_guards_formula_prefixes()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "AccreditationApiController.cs");
        Assert.Contains("ReportAsync(campusId, from, to", api);
        Assert.Contains("=+-@", api);
        Assert.DoesNotContain("EvidenceSummary", api);
        Assert.DoesNotContain("ResultSnapshot", api);
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
