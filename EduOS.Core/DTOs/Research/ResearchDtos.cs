using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Research;

public sealed class ResearchProposalCreateDto
{
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public long? AcademicProgramId { get; set; }
    [Required, StringLength(200, MinimumLength = 5)] public string Title { get; set; } = "";
    [Required, StringLength(3000, MinimumLength = 30)] public string Abstract { get; set; } = "";
    [Required, StringLength(6000, MinimumLength = 30)] public string Protocol { get; set; } = "";
}

public sealed class ResearchProposalCommandDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = "";
    public string? Title { get; set; }
    public string? Abstract { get; set; }
    public string? Protocol { get; set; }
    public long? SupervisorUserId { get; set; }
    public long? ReviewerUserId { get; set; }
    public string? RiskCode { get; set; }
    public string? Reason { get; set; }
    public bool? DeclaresNoConflict { get; set; }
}

public sealed class ResearchProposalDto
{
    public Guid Reference { get; set; }
    public long CampusId { get; set; }
    public long? AcademicProgramId { get; set; }
    public long OwnerUserId { get; set; }
    public string Title { get; set; } = "";
    public string? Abstract { get; set; }
    public string? Protocol { get; set; }
    public string StateCode { get; set; } = "";
    public int CurrentRevision { get; set; }
    public string ContentSha256 { get; set; } = "";
    public long? SupervisorUserId { get; set; }
    public long? ReviewerUserId { get; set; }
    public string? RiskCode { get; set; }
    public string RowVersion { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    public List<string> AllowedActions { get; set; } = new();
}

public sealed class ResearchProposalHistoryDto
{
    public string ActionCode { get; set; } = "";
    public string FromState { get; set; } = "";
    public string ToState { get; set; } = "";
    public int RevisionNo { get; set; }
    public string? Reason { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class ResearchCampusDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class ResearchReviewerDto
{
    public long UserId { get; set; }
    public string Name { get; set; } = "";
    public bool CanSupervise { get; set; }
    public bool CanReview { get; set; }
}

public sealed class ResearchProgramDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}
