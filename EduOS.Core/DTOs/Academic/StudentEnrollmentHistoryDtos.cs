namespace EduOS.Core.DTOs.Academic;

/// <summary>Historical enrollment identity; catalog labels are current display labels, not reconstructed snapshots.</summary>
public sealed class StudentEnrollmentHistoryRowDto
{
    public Guid EnrollmentReference { get; set; }
    public long CampusId { get; set; }
    public long AcademicBatchId { get; set; }
    public string BatchCode { get; set; } = string.Empty;
    public string BatchName { get; set; } = string.Empty;
    public string CurriculumName { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public string? RegistrationNo { get; set; }
    public DateOnly EnrollmentDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string State { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
