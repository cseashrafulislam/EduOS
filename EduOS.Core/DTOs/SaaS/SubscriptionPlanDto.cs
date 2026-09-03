namespace EduOS.Core.DTOs.SaaS
{
    /// <summary>
    /// Public plan listing - shown on pricing page (no auth required)
    /// </summary>
    public class SubscriptionPlanDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? NameBangla { get; set; }
        public string Code { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? DescriptionBangla { get; set; }
        public string? ShortDescription { get; set; }
        public string? ShortDescriptionBangla { get; set; }
        public string? IconUrl { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsRecommended { get; set; }
        public bool IsFreeTrial { get; set; }
        public int? TrialDays { get; set; }

        // Pricing
        public decimal MonthlyPrice { get; set; }
        public decimal QuarterlyPrice { get; set; }
        public decimal HalfYearlyPrice { get; set; }
        public decimal YearlyPrice { get; set; }
        public decimal SetupFee { get; set; }
        public string Currency { get; set; } = "BDT";

        // Limits
        public int MaxStudents { get; set; }
        public int MaxTeachers { get; set; }
        public int MaxCampuses { get; set; }
        public int MaxAdminUsers { get; set; }
        public int MaxStorageMb { get; set; }
        public int MaxSmsPerMonth { get; set; }
        public int MaxEmailsPerMonth { get; set; }

        // Features included in this plan
        public List<PlanFeatureDto> Features { get; set; } = new();
    }

    public class PlanFeatureDto
    {
        public long FeatureId { get; set; }
        public string FeatureName { get; set; } = string.Empty;
        public string? FeatureNameBangla { get; set; }
        public string FeatureCode { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string? IconName { get; set; }
        public bool IsEnabled { get; set; }
        public int? LimitValue { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>
    /// Comparison view - all plans side by side
    /// </summary>
    public class PlanComparisonDto
    {
        public List<SubscriptionPlanDto> Plans { get; set; } = new();
        public List<FeatureCategoryDto> FeatureCategories { get; set; } = new();
    }

    public class FeatureCategoryDto
    {
        public string Category { get; set; } = string.Empty;
        public List<FeatureItemDto> Features { get; set; } = new();
    }

    public class FeatureItemDto
    {
        public long FeatureId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? NameBangla { get; set; }
        public string Code { get; set; } = string.Empty;
        public Dictionary<long, bool> PlanAvailability { get; set; } = new();
    }
}
