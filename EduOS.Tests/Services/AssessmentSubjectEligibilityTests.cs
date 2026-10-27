using EduOS.Service.Services.AssessmentAdministration;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentSubjectEligibilityTests
{
    [Theory]
    [InlineData(10, 2, 10, 2, true)]
    [InlineData(10, 2, 11, 2, false)]
    [InlineData(10, 2, 10, 3, false)]
    public void Curriculum_and_level_are_both_required(long subjectCurriculum, long subjectLevel,
        long enrollmentCurriculum, long enrollmentLevel, bool expected) =>
        AssessmentSubjectEligibility.AppliesToEnrollment(subjectCurriculum, subjectLevel,
            enrollmentCurriculum, enrollmentLevel).Should().Be(expected);

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Mandatory_subjects_require_marks_and_electives_only_when_approved(
        bool optional, bool registered, bool expected) =>
        AssessmentSubjectEligibility.RequiresMark(optional, registered).Should().Be(expected);
}
