using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Research;

public sealed class ResearchGrantSchemeCreateDto
{
    public Guid ClientRequestId { get; set; }
    [Required, StringLength(40, MinimumLength = 2)] public string Code { get; set; } = "";
    [Required, StringLength(200, MinimumLength = 3)] public string Name { get; set; } = "";
    [Required, RegularExpression("^[A-Z]{3}$")] public string CurrencyCode { get; set; } = "BDT";
    [Range(typeof(decimal), "0.01", "99999999999999999.99")] public decimal MaxAward { get; set; }
}

public sealed class ResearchGrantSchemeDto
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public decimal MaxAward { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ResearchGrantCreateDto
{
    public Guid ClientRequestId { get; set; }
    public Guid ProposalReference { get; set; }
    public long SchemeId { get; set; }
    [Required, StringLength(200, MinimumLength = 5)] public string Title { get; set; } = "";
    [Range(typeof(decimal), "0.01", "99999999999999999.99")] public decimal RequestedAmount { get; set; }
}

public sealed class ResearchGrantCommandDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = "";
    public decimal? Amount { get; set; }
    [StringLength(200)] public string? MilestoneTitle { get; set; }
    public DateTime? DueDate { get; set; }
    public long? MilestoneId { get; set; }
    [StringLength(2000)] public string? Outcome { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
}

public sealed class ResearchGrantDto
{
    public Guid Reference { get; set; }
    public Guid ProposalReference { get; set; }
    public long CampusId { get; set; }
    public long SchemeId { get; set; }
    public long OwnerUserId { get; set; }
    public string Title { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public decimal RequestedAmount { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public decimal? PendingAmount { get; set; }
    public int BudgetVersion { get; set; }
    public string StateCode { get; set; } = "";
    public string RowVersion { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    public List<string> AllowedActions { get; set; } = new();
}

public sealed class ResearchGrantMilestoneDto
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime DueDate { get; set; }
    public string? Outcome { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public sealed class ResearchGrantHistoryDto
{
    public string ActionCode { get; set; } = "";
    public string FromState { get; set; } = "";
    public string ToState { get; set; } = "";
    public string? Reason { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
