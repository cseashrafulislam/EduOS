using System.ComponentModel.DataAnnotations;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.Tenants;
using Xunit;

namespace EduOS.Tests.Architecture;

public sealed class TenantOnboardingModelContractTests
{
    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "TestAssets", name);

    [Fact]
    public void Tenant_profile_contract_uses_canonical_model_fields_and_is_registered_end_to_end()
    {
        var modelFields = new[] { "Name", "InstitutionTypeDefinitionId", "LegalName", "RegistrationNumber",
            "Code", "Subdomain", "Email", "Phone", "Address", "CountryCode" };
        foreach (var field in modelFields)
        {
            Assert.NotNull(typeof(Tenant).GetProperty(field));
            Assert.NotNull(typeof(TenantOnboardingProfileDto).GetProperty(field));
        }
        Assert.True(typeof(ITenantOnboardingProfileService).IsAssignableFrom(typeof(TenantOnboardingProfileService)));
        Assert.Null(typeof(Tenant).Assembly.GetType("EduOS.Core.DTOs.Tenants.InstitutionProfileWizardDto"));
        Assert.Null(typeof(Tenant).Assembly.GetType("EduOS.Core.DTOs.Tenants.InstitutionCampusWizardRequestDto"));
        Assert.Null(typeof(Tenant).Assembly.GetType("EduOS.Core.DTOs.Tenants.InstitutionAcademicYearWizardRequestDto"));
        Assert.Null(typeof(Tenant).Assembly.GetType("EduOS.Core.DTOs.Tenants.InstitutionAcademicTermWizardRequestDto"));
        Assert.Null(typeof(Tenant).Assembly.GetType("EduOS.Core.Interfaces.IServices.IInstitutionProfileWizardService"));
    }

    [Fact]
    public void Campus_and_academic_onboarding_requests_match_their_domain_folders()
    {
        Assert.Equal("EduOS.Core.DTOs.SaaS", typeof(CampusOnboardingRequestDto).Namespace);
        Assert.Equal("EduOS.Core.DTOs.Academic", typeof(AcademicYearOnboardingRequestDto).Namespace);
        Assert.Equal("EduOS.Core.DTOs.Academic", typeof(AcademicTermOnboardingRequestDto).Namespace);
        var request = new TenantOnboardingProfileDto
        {
            Name = "Green School", InstitutionTypeDefinitionId = 1, OwnerName = "Admin",
            Email = "admin@example.test", CountryCode = "BD", RowVersion = "AQID"
        };
        Assert.Empty(Validate(request));
        request.Name = "";
        Assert.NotEmpty(Validate(request));
    }

    [Fact]
    public void Browser_and_api_submit_model_names_and_preserve_onboarding_transaction()
    {
        var controller = File.ReadAllText(Asset("InstitutionOnboardingController.cs"));
        var view = File.ReadAllText(Asset("InstitutionProfile.cshtml"));
        var script = File.ReadAllText(Asset("institution-profile.js"));
        Assert.Contains("[HttpGet(\"tenant-profile\")]", controller);
        Assert.Contains("[HttpPost(\"tenant-profile\")]", controller);
        Assert.Contains("[FromForm] TenantOnboardingProfileDto request", controller);
        Assert.DoesNotContain("[HttpPost(\"institution-profile\")]", controller);
        foreach (var field in new[] { "name", "institutionTypeDefinitionId", "legalName", "registrationNumber",
            "code", "subdomain", "email", "countryCode" })
            Assert.Contains($"id=\"{field}\"", view);
        Assert.Contains("'/api/institution-onboarding/tenant-profile'", script);
        Assert.Contains("option.value = String(type.id || '')", script);
        Assert.Contains("institutionTypeDefinitionId: valueOf('institutionTypeDefinitionId')", script);
        Assert.Contains("legalName: valueOf('legalName')", script);
        Assert.Contains("registrationNumber: valueOf('registrationNumber')", script);
        Assert.Contains("'/api/onboarding/complete-step'", script);
        Assert.DoesNotContain("'/api/institution-onboarding/institution-profile'", script);
    }

    private static IReadOnlyList<ValidationResult> Validate(TenantOnboardingProfileDto dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results;
    }
}
