namespace EduOS.Core.DTOs.SaaS;

public sealed class SubscriptionPlanAvailabilityDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal MonthlyPrice { get; set; }
    public decimal YearlyPrice { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public bool IsActive { get; set; }
    public bool IsPublic { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ChangeSubscriptionPlanAvailabilityRequestDto
{
    public Guid RequestId { get; set; }
    public bool IsActive { get; set; }
    public bool IsPublic { get; set; }
    [global::System.ComponentModel.DataAnnotations.Required]
    public string RowVersion { get; set; } = string.Empty;
}
