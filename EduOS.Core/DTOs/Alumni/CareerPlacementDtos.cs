using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Alumni;

public sealed class CareerOpportunityDraftDto
{
    public Guid ClientRequestId { get; set; }
    public Guid EmployerReference { get; set; }
    [Required, StringLength(160, MinimumLength = 3)] public string Title { get; set; } = "";
    [StringLength(2000)] public string? Description { get; set; }
    [Range(1, 2)] public int Kind { get; set; } = 1;
    public DateTime ApplyUntilUtc { get; set; }
}
public sealed class CareerActionDto : CareerActionDtoBase { }
public sealed class CareerOpportunityQueryDto
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 20;
    [Range(0, 3)] public int Status { get; set; }
}
public sealed class CareerOpportunityItemDto
{
    public Guid Reference { get; set; }
    public Guid EmployerReference { get; set; }
    public long CampusId { get; set; }
    public string EmployerName { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int Kind { get; set; }
    public int Status { get; set; }
    public DateTime ApplyUntilUtc { get; set; }
    public string RowVersion { get; set; } = "";
    public bool CanPublish { get; set; }
    public bool CanApply { get; set; }
    public long CreatedByUserId { get; set; }
}
public sealed class CareerEligibleStudentDto
{
    public Guid StudentReference { get; set; }
    public long CampusId { get; set; }
    public string StudentCode { get; set; } = "";
    public string FullName { get; set; } = "";
}
public sealed class CareerApplyDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentReference { get; set; }
}
public sealed class CareerApplicationQueryDto
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 20;
    public Guid? OpportunityReference { get; set; }
    [Range(0, 4)] public int Status { get; set; }
}
public sealed class CareerApplicationItemDto
{
    public Guid Reference { get; set; }
    public Guid OpportunityReference { get; set; }
    public string OpportunityTitle { get; set; } = "";
    public long CampusId { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = "";
    public string FullName { get; set; } = "";
    public int Status { get; set; }
    public int? ProposedOutcome { get; set; }
    public bool CanApprove { get; set; }
    public string RowVersion { get; set; } = "";
    public DateTime AppliedAtUtc { get; set; }
}
public sealed class CareerDecisionDto : CareerActionDtoBase
{
    [Range(2, 3)] public int Outcome { get; set; }
}
public class CareerActionDtoBase
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = "";
}
public sealed class CareerPlacementReportItemDto
{
    public Guid OpportunityReference { get; set; }
    public long CampusId { get; set; }
    public string EmployerName { get; set; } = "";
    public string OpportunityTitle { get; set; } = "";
    public int Applications { get; set; }
    public int Pending { get; set; }
    public int Rejected { get; set; }
    public int Placed { get; set; }
    public int Withdrawn { get; set; }
}
