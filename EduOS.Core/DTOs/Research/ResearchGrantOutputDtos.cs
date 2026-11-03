using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Research;

public sealed class ResearchGrantOutputSubmitDto
{
    public Guid ClientRequestId { get; set; }
    [Required, StringLength(24)] public string KindCode { get; set; } = "";
    [Required, StringLength(200, MinimumLength = 5)] public string Title { get; set; } = "";
    [Required, StringLength(2000, MinimumLength = 20)] public string Summary { get; set; } = "";
    [StringLength(500)] public string? ReferenceUrl { get; set; }
    public long? MilestoneId { get; set; }
    public DateTime OccurredOn { get; set; }
}

public sealed class ResearchGrantOutputReviewDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = "";
    [Required, RegularExpression("^(verify|reject)$")] public string Decision { get; set; } = "";
    [Required, StringLength(500, MinimumLength = 10)] public string Reason { get; set; } = "";
    public bool DeclaresNoConflict { get; set; }
}

public sealed class ResearchGrantOutputDto
{
    public Guid Reference { get; set; }
    public Guid GrantReference { get; set; }
    public string KindCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? ReferenceUrl { get; set; }
    public long? MilestoneId { get; set; }
    public DateTime OccurredOn { get; set; }
    public string StateCode { get; set; } = "";
    public long SubmittedByUserId { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public long? DecisionByUserId { get; set; }
    public DateTime? DecisionAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public string RowVersion { get; set; } = "";
    public bool CanReview { get; set; }
}

public sealed class ResearchGrantOutcomeReportDto
{
    public string KindCode { get; set; } = "";
    public int Submitted { get; set; }
    public int Verified { get; set; }
    public int Rejected { get; set; }
    public int Total { get; set; }
}
