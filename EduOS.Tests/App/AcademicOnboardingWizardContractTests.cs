using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using EduOS.App.Controllers.Api;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

public sealed class AcademicOnboardingWizardContractTests
{
    [Fact]
    public async Task Academic_year_wizard_accepts_missing_code_and_allocates_it_on_the_server()
    {
        // The canonical Foundation API requires Code, but the onboarding form does not request one.
        const string json = "{\"name\":\"2026-2027\",\"startDate\":\"2026-07-01\",\"endDate\":\"2027-06-30\",\"isCurrent\":true}";
        var request = JsonSerializer.Deserialize<InstitutionAcademicYearWizardRequestDto>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var errors = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(request, new ValidationContext(request), errors, true));
        Assert.Null(request.Code);

        var canonical = new SaveAcademicYearRequestDto
        {
            Name = request.Name, StartDate = request.StartDate, EndDate = request.EndDate
        };
        errors.Clear();
        Assert.False(Validator.TryValidateObject(canonical, new ValidationContext(canonical), errors, true));
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(canonical.Code)));

        var foundation = new Mock<IInstitutionFoundationService>();
        foundation.Setup(x => x.SaveAcademicYearAsync(null, It.IsAny<SaveAcademicYearRequestDto>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(
            ApiResponse<AcademicYearDto>.SuccessResponse(new AcademicYearDto { Id = 10, Code = "AY-VALID" }));
        var controller = Controller(foundation.Object);
        var response = await controller.SaveAcademicYear(request, CancellationToken.None);
        Assert.Equal(200, Assert.IsType<ObjectResult>(response).StatusCode);
        foundation.Verify(x => x.SaveAcademicYearAsync(null, It.Is<SaveAcademicYearRequestDto>(x =>
            x.Code.StartsWith("AY-") && x.Code.Length == 15 && x.Name == "2026-2027" &&
            x.IsCurrent && x.IsActive && x.StartDate == new DateOnly(2026, 7, 1) &&
            x.EndDate == new DateOnly(2027, 6, 30)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Term_wizard_maps_required_date_range_without_a_client_generated_code()
    {
        var foundation = new Mock<IInstitutionFoundationService>();
        foundation.Setup(x => x.SaveAcademicTermAsync(null, It.IsAny<SaveAcademicTermRequestDto>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(
            ApiResponse<AcademicTermDto>.SuccessResponse(new AcademicTermDto { Id = 12 }));
        var request = new InstitutionAcademicTermWizardRequestDto
        {
            AcademicYearId = 10, Name = "Term 1", StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 12, 31)
        };
        var response = await Controller(foundation.Object).SaveAcademicTerm(request, CancellationToken.None);
        Assert.Equal(200, Assert.IsType<ObjectResult>(response).StatusCode);
        foundation.Verify(x => x.SaveAcademicTermAsync(null, It.Is<SaveAcademicTermRequestDto>(x =>
            x.AcademicYearId == 10 && x.Name == "Term 1" && x.Code == null &&
            x.StartDate == new DateOnly(2026, 7, 1) && x.EndDate == new DateOnly(2026, 12, 31) &&
            x.IsActive), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Academic_onboarding_form_explains_required_dates_and_preserves_server_errors()
    {
        var root = AppContext.BaseDirectory;
        var view = File.ReadAllText(Path.Combine(root, "TestAssets", "AcademicSetup.cshtml"));
        var js = File.ReadAllText(Path.Combine(root, "TestAssets", "academic-setup.js"));
        Assert.Contains("id=\"termStartDate\" name=\"startDate\" class=\"form-control\" required", view);
        Assert.Contains("id=\"termEndDate\" name=\"endDate\" class=\"form-control\" required", view);
        Assert.Contains("responseMessage(response, i18n.saveFailed)", js);
        Assert.Contains("message: payload?.message, errors: payload?.errors", js);
        Assert.Contains("i18n.startDateRequired", js);
        Assert.Contains("i18n.endDateRequired", js);
    }

    private static InstitutionOnboardingController Controller(IInstitutionFoundationService foundation) => new(
        Mock.Of<IInstitutionRegistrationService>(), foundation,
        Mock.Of<IInstitutionProfileWizardService>(), Mock.Of<IOnboardingService>());
}
