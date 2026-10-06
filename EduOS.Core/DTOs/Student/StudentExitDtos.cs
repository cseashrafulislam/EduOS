using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Student;

public sealed class ProcessStudentExitDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentReference { get; set; }
    [Required, RegularExpression("^(Transfer|Completed|Dropout)$")] public string ExitType { get; set; } = string.Empty;
    [StringLength(1000)] public string? Reason { get; set; }
    [StringLength(500)] public string? ConductRemark { get; set; }
    [Required] public string StudentRowVersion { get; set; } = string.Empty;
}

public sealed class StudentExitResultDto
{
    public Guid Reference { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string ExitType { get; set; } = string.Empty;
    public string FinalStatus { get; set; } = string.Empty;
    public string? CertificateNo { get; set; }
    public decimal DueAtExit { get; set; }
    public bool FeesCleared { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}
