using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Student;
public sealed class StudentReadmissionOptionsDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = "";
    public string PreviousStatus { get; set; } = "";
    public long PreviousEnrollmentId { get; set; }
    public string StudentRowVersion { get; set; } = "";
    public string PreviousEnrollmentRowVersion { get; set; } = "";
    public IReadOnlyList<StudentReadmissionTargetDto> Targets { get; set; } = Array.Empty<StudentReadmissionTargetDto>();
}
public sealed class StudentReadmissionTargetDto
{
    public long AcademicBatchId { get; set; }
    public string Name { get; set; } = "";
}
public sealed class ProcessStudentReadmissionDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1,long.MaxValue)] public long PreviousEnrollmentId { get; set; }
    [Range(1,long.MaxValue)] public long TargetAcademicBatchId { get; set; }
    [Required,StringLength(50)] public string TargetRoll { get; set; } = "";
    [Required,StringLength(450)] public string Reason { get; set; } = "";
    [Required] public string StudentRowVersion { get; set; } = "";
    [Required] public string PreviousEnrollmentRowVersion { get; set; } = "";
}
public sealed class StudentReadmissionResultDto
{
    public Guid StudentReference { get; set; }
    public Guid EnrollmentReference { get; set; }
    public long AcademicBatchId { get; set; }
    public string Roll { get; set; } = "";
    public string Status { get; set; } = "Active";
    public bool AlreadyProcessed { get; set; }
}
