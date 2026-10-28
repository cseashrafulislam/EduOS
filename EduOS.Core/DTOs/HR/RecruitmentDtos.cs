namespace EduOS.Core.DTOs.HR;

public sealed class RecruitmentCreateRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    [Range(1, long.MaxValue)] public long DesignationId { get; set; }
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    public DateOnly AppliedOn { get; set; }
    public bool ConsentConfirmed { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}
public sealed class RecruitmentTransitionRequestDto
{
    public RecruitmentStage NextStage { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    public DateOnly? InterviewOn { get; set; }
    public Guid? EmployeeReference { get; set; }
    public bool IdentityVerified { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
}
public sealed class RecruitmentCandidateDto
{
    public Guid Reference { get; set; }
    public long CampusId { get; set; }
    public long DesignationId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public DateOnly AppliedOn { get; set; }
    public DateOnly? InterviewOn { get; set; }
    public RecruitmentStage Stage { get; set; }
    public Guid? LinkedEmployeeReference { get; set; }
    public string? Notes { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class RecruitmentStageHistoryDto
{
    public RecruitmentStage? PreviousStage { get; set; }
    public RecruitmentStage NewStage { get; set; }
    public DateTime TransitionAt { get; set; }
    public string? Reason { get; set; }
}
