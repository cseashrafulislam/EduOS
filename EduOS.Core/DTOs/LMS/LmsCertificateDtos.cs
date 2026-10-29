namespace EduOS.Core.DTOs.LMS;

/// <summary>Issue/reissue requests are idempotent per tenant and request GUID.</summary>
public sealed class IssueLmsCertificateRequestDto
{
    public long CourseEnrollmentId { get; set; }
    public Guid ClientRequestId { get; set; }
}

public sealed class RevokeLmsCertificateRequestDto
{
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class LmsCertificateDto
{
    public Guid Reference { get; set; }
    public long CourseEnrollmentId { get; set; }
    public Guid CourseReference { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string CourseCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
    public int RequiredLessons { get; set; }
    public int CompletedLessons { get; set; }
    public int RequiredAssignments { get; set; }
    public int GradedAssignments { get; set; }
    public int RequiredQuizzes { get; set; }
    public int PassedQuizzes { get; set; }
    public int Revision { get; set; }
    public DateTime IssuedAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class LmsCertificateVerificationDto
{
    public bool IsValid { get; set; }
    public bool IsRevoked { get; set; }
    public Guid Reference { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public int Revision { get; set; }
}
