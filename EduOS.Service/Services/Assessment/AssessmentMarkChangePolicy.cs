using EduOS.Core.Entities.Assessment;

namespace EduOS.Service.Services.AssessmentAdministration;

/// <summary>Compares persisted mark evidence without changing history on an identical retry.</summary>
public static class AssessmentMarkChangePolicy
{
    public static bool HasChanged(StudentAssessmentMark? row, decimal marks, bool absent,
        bool withheld, string? remarks, string? gradeLetter, decimal? gradePoint) =>
        row == null || row.ObtainedMarks != marks || row.IsAbsent != absent ||
        row.IsWithheld != withheld || row.Remarks != remarks ||
        row.GradeLetter != gradeLetter || row.GradePoint != gradePoint;

    public static bool HasChanged(StudentAssessmentComponentMark? row, decimal marks,
        bool absent, bool withheld) =>
        row == null || row.ObtainedMarks != marks ||
        row.IsAbsent != absent || row.IsWithheld != withheld;
}
