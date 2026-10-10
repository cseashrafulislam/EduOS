using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Admission;
using EduOS.Core.Interfaces.IServices;
using FluentAssertions;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AdmissionAssessmentRosterContractTests
{
    [Fact]
    public void Roster_exposes_only_assessment_fields_and_no_private_contacts()
    {
        var names = typeof(AdmissionAssessmentApplicantDto).GetProperties()
            .Select(x => x.Name).ToArray();
        names.Should().Contain(new[] { "ApplicantId", "ApplicantReference", "ApplicationNumber", "ApplicantName", "ObtainedMarks" });
        names.Should().NotContain(new[] { "Phone", "Email", "Address", "DateOfBirth", "GuardianName", "NationalId" });
        typeof(IAdmissionAssessmentService).GetMethod("GetApplicantsAsync").Should().NotBeNull();
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 101)]
    [InlineData(1, 0)]
    public void Invalid_paging_is_rejected_by_dto_validation(int page, int pageSize)
    {
        var query = new AdmissionAssessmentRosterQueryDto { Page = page, PageSize = pageSize };
        Validator.TryValidateObject(query, new ValidationContext(query), new List<ValidationResult>(), true)
            .Should().BeFalse();
    }

    [Fact]
    public void Roster_endpoint_uses_existing_guarded_assessment_controller()
    {
        var type = typeof(AdmissionAssessmentsController);
        type.GetCustomAttributes(false).Select(x => x.GetType().Name)
            .Should().Contain(new[] { "AuthorizeAttribute", "RequireModuleAttribute", "AutoValidateAntiforgeryTokenAttribute" });
        type.GetMethod("GetApplicants").Should().NotBeNull();
    }
}
