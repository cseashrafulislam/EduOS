namespace EduOS.Core.DTOs.LMS;

public sealed class TeacherCpdEnrollRequestDto
{
    public Guid CourseReference { get; set; }
    public Guid ClientRequestId { get; set; }
}

public sealed class TeacherCpdLessonRequestDto
{
    public Guid LessonReference { get; set; }
    public bool IsCompleted { get; set; }
    [Range(typeof(decimal), "0", "100")] public decimal ProgressPercent { get; set; }
}

public sealed class TeacherCpdCourseDto
{
    public Guid Reference { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long? EnrollmentId { get; set; }
    public string State { get; set; } = "NotEnrolled";
    public string? RowVersion { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? CertificateReference { get; set; }
    public int TotalLessons { get; set; }
    public int CompletedLessons { get; set; }
}

public sealed class TeacherCpdLessonDto
{
    public Guid Reference { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? ContentUrl { get; set; }
    public bool IsCompleted { get; set; }
    public decimal ProgressPercent { get; set; }
}

public sealed class TeacherCpdParticipantDto
{
    public long EnrollmentId { get; set; }
    public Guid EmployeeReference { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime EnrolledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? CertificateReference { get; set; }
}
