using EduOS.Core.Common;
namespace EduOS.Core.DTOs.Finance;
// Prospective award policy; amounts realized on invoices are a separate source of truth.
public sealed class DiscountAwardRowDto
{
    public long Id { get; set; }
    public Guid EnrollmentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public long RuleId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public decimal RuleValue { get; set; }
    public bool IsPercentage { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public long ApprovedByUserId { get; set; }
}
public sealed class DiscountAwardRegisterDto
{
    public int TotalCount { get; set; }
    public int ActiveCount { get; set; }
    public int InactiveCount { get; set; }
    public PagedResult<DiscountAwardRowDto> Awards { get; set; } = new();
}
