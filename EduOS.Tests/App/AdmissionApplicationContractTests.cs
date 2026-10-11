using FluentAssertions;
using Xunit;

namespace EduOS.Tests.App;

public class AdmissionApplicationContractTests
{
    [Fact]
    public void Admission_api_has_role_module_antiforgery_and_rate_limit_guards()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AdmissionApplicationsController.cs"));
        source.Should().Contain("[Authorize(Roles = \"TenantAdmin,AdmissionOfficer\")]");
        source.Should().Contain("[RequireModule(\"ADMISSION\")]");
        source.Should().Contain("[AutoValidateAntiforgeryToken]");
        source.Should().Contain("[EnableRateLimiting(\"AdmissionIntakePolicy\")]");
        source.Should().Contain("[HttpPost(\"{reference:guid}/admit\")]");
        source.Should().Contain("IAdmissionEnrollmentService");
        source.Should().NotContain("IgnoreQueryFilters");

        var middleware = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "PrivilegedMfaMiddleware.cs"));
        middleware.Should().Contain("AdmissionOfficer");
    }

    [Fact]
    public void Admission_ui_is_localized_accessible_and_uses_safe_dom_rendering()
    {
        var view = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AdmissionsIndex.cshtml"));
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "admissions-index.js"));

        view.Should().Contain("@T[\"AdmissionApplications\"]");
        view.Should().Contain("aria-live=\"polite\"");
        view.Should().Contain("autocomplete=\"tel\"");
        view.Should().NotContain("onclick=");
        view.Should().NotContain("style=\"");
        script.Should().Contain("clientRequestId");
        script.Should().Contain("/admit");
        // UI contracts must match canonical AdmissionApplicantState and AdmitAdmissionApplicationDto.
        script.Should().Contain("const transitions = { 2: [3, 6, 7], 3: [6, 7], 4: [3], 5: [6, 7] }");
        script.Should().Contain("payload.data.academicBatches");
        script.Should().Contain("payload.data.academicTracks");
        script.Should().Contain("academicBatchId: positiveInteger(");
        script.Should().Contain("admissionFormReference: selectedForm.reference");
        script.Should().Contain("academicLevelId: positiveInteger(valueOf('intakeUnitId'))");
        script.Should().Contain("fieldKey: row.querySelector(");
        script.Should().Contain("dataType: type");
        script.Should().Contain("optionsJson: [7, 8].includes(type)");
        script.Should().Contain("item.currencyCode");
        script.Should().NotContain("Number(item.status)");
        view.Should().Contain("Add document (not available)");
        typeof(EduOS.Core.DTOs.Admission.AdmissionIntakeFormDto).GetProperty("AcademicTermId").Should().NotBeNull();
        script.Should().Contain("customResponses: collectAdmissionResponses()");
        script.Should().Contain("state: status");
        script.Should().Contain("params.set('state', String(status))");
        script.Should().Contain("state.options.academicLevels");
        script.Should().Contain("Number(payload.data.state)");
        script.Should().NotContain("Number(payload.data.status)");
        view.Should().Contain("admissionFormReference");
        view.Should().Contain("admissionCustomFields");
        script.Should().Contain("academicTrackId: positiveInteger(");
        script.Should().NotContain("payload.data.sections");
        script.Should().NotContain("payload.data.groups");
        script.Should().NotContain("sectionId: positiveInteger(");
        script.Should().NotContain("groupId: positiveInteger(");
        view.Should().Contain("<option value=\"6\">Qualified");
        script.Should().Contain("/api/admission-intake/forms");
        script.Should().Contain("dataset.documentId");
        view.Should().Contain("id=\"intakeFormEditor\"");
        view.Should().Contain("id=\"applicantDocumentList\"");
        script.Should().Contain("credentials: 'same-origin'");
        script.Should().Contain("textContent");
        script.Should().NotContain("innerHTML");
        script.Should().NotContain("localStorage");
    }
}
