namespace EduOS.Core.DTOs.SaaS;

public sealed class SubscriptionPlanConfigurationDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "BDT";
    public decimal MonthlyPrice { get; set; }
    public decimal YearlyPrice { get; set; }
    public int TrialDays { get; set; }
    public int MaxStudents { get; set; }
    public int MaxEmployees { get; set; }
    public int MaxUsers { get; set; }
    public int MaxCampuses { get; set; }
    public int MaxStorageMb { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public List<SubscriptionPlanFeatureConfigurationDto> Features { get; set; } = new();
}

public sealed class SubscriptionPlanFeatureConfigurationDto
{
    public long FeatureId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public long? LimitValue { get; set; }
}

public sealed class SaveSubscriptionPlanConfigurationRequestDto
{
    public Guid RequestId { get; set; }
    [global::System.ComponentModel.DataAnnotations.Required]
    public string RowVersion { get; set; } = string.Empty;
    public decimal MonthlyPrice { get; set; }
    public decimal YearlyPrice { get; set; }
    public int TrialDays { get; set; }
    public int MaxStudents { get; set; }
    public int MaxEmployees { get; set; }
    public int MaxUsers { get; set; }
    public int MaxCampuses { get; set; }
    public int MaxStorageMb { get; set; }
    public List<SubscriptionPlanFeatureSelectionDto> Features { get; set; } = new();
}

public sealed class SubscriptionPlanFeatureSelectionDto
{
    public long FeatureId { get; set; }
    public bool IsEnabled { get; set; }
    public long? LimitValue { get; set; }
}
