namespace EduOS.Core.DTOs.Academic;

/// <summary>
/// Original enrollment identity and registration snapshots. Subject labels are
/// current catalog display values, never reconstructed historical snapshots.
/// </summary>
public sealed class StudentSubjectHistoryRowDto
{
    public Guid EnrollmentReference { get; set; }
    public long CampusId { get; set; }
    public long AcademicBatchId { get; set; }
    public string RollNo { get; set; } = string.Empty;
    public DateOnly EnrollmentDate { get; set; }
    public string EnrollmentState { get; set; } = string.Empty;
    public bool IsCurrentEnrollment { get; set; }
    public string SubjectCode { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public decimal? CreditHoursSnapshot { get; set; }
    public string RegistrationState { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? Remarks { get; set; }
}
