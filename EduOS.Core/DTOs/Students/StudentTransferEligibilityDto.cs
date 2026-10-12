namespace EduOS.Core.DTOs.Students;

public sealed class StudentTransferEligibilityDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string StudentRowVersion { get; set; } = string.Empty;
    public Guid EnrollmentReference { get; set; }
    public string EnrollmentRowVersion { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public string RollNo { get; set; } = string.Empty;
}
