using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.LMS;

/// <summary>
/// One immutable issue-time evidence snapshot per LMS course-completion certificate revision.
/// Only revocation metadata may change; a reissue creates a new row, never edits the old one.
/// Academic transcripts and formal V1 credentials remain independently authoritative.
/// </summary>
public sealed class LmsCompletionCertificate : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long CourseEnrollmentId { get; set; }
    public long CourseId { get; set; }
    public long StudentId { get; set; }
    public int Revision { get; set; } = 1;
    [Required, MaxLength(200)] public string CourseTitleSnapshot { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string CourseCodeSnapshot { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string StudentNameSnapshot { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string StudentCodeSnapshot { get; set; } = string.Empty;
    public DateTime CompletedAtSnapshot { get; set; }
    public int RequiredLessonsSnapshot { get; set; }
    public int CompletedLessonsSnapshot { get; set; }
    public int RequiredAssignmentsSnapshot { get; set; }
    public int GradedAssignmentsSnapshot { get; set; }
    public int RequiredQuizzesSnapshot { get; set; }
    public int PassedQuizzesSnapshot { get; set; }
    // Opt-in verification is separate from immutable completion evidence. Only the
    // digest is persisted; disabling or rotating invalidates all previous links.
    [MaxLength(64)] public string? VerificationTokenHash { get; set; }
    public DateTime? VerificationExpiresAt { get; set; }
    public DateTime IssuedAt { get; set; }
    public long IssuedByUserId { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public long? RevokedByUserId { get; set; }
    [MaxLength(500)] public string? RevocationReason { get; set; }
}
