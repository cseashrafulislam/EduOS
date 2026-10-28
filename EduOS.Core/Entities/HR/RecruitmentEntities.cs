using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.HR;

/// <summary>Tenant-owned recruitment record, separate from verified Person and Employee identity.</summary>
public sealed class RecruitmentCandidate : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public long DesignationId { get; set; }
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    public DateOnly AppliedOn { get; set; }
    public DateOnly? InterviewOn { get; set; }
    public DateTime ConsentConfirmedAt { get; set; }
    public RecruitmentStage Stage { get; set; } = RecruitmentStage.Applied;
    public long? LinkedEmployeeId { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}

public sealed class RecruitmentStageHistory : BaseTenantEntity
{
    public long RecruitmentCandidateId { get; set; }
    public RecruitmentStage? PreviousStage { get; set; }
    public RecruitmentStage NewStage { get; set; }
    public DateTime TransitionAt { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
    public long ActorUserId { get; set; }
}
