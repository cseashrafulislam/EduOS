namespace EduOS.Service.Services.AssessmentAdministration;

/// <summary>One curriculum/elective applicability rule for result preflight and publication.</summary>
public static class AssessmentSubjectEligibility
{
    public static bool AppliesToEnrollment(long subjectCurriculumId, long subjectLevelId,
        long enrollmentCurriculumId, long enrollmentLevelId) =>
        subjectCurriculumId == enrollmentCurriculumId && subjectLevelId == enrollmentLevelId;

    public static bool RequiresMark(bool isOptional, bool hasApprovedRegistration) =>
        !isOptional || hasApprovedRegistration;
}
