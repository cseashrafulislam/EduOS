namespace EduOS.Core.DTOs.SaaS
{
    /// <summary>
    /// Public plan listing - shown on pricing page (no auth required)
    /// </summary>
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
