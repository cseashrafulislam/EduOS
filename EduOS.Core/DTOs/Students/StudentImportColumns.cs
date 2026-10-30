namespace EduOS.Core.DTOs.Students;

/// <summary>
/// Stable field contract for the downloadable student import template.
/// Institution and student counts are determined by the uploaded data, never this schema.
/// </summary>
public static class StudentImportColumns
{
    public static readonly string[] Required =
        ["SourceKey", "StudentCode", "FullName", "CampusId", "AcademicBatchId", "RollNo"];

    public static readonly string[] Optional =
        ["DateOfBirth", "GuardianName", "GuardianPhone", "GuardianSourceKey"];

    public static readonly string[] All = [.. Required, .. Optional];
}
