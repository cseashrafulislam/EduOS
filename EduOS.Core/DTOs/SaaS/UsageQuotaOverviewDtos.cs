namespace EduOS.Core.DTOs.SaaS;

/// <summary>Read-only comparison of saved daily usage and explicitly identified
/// live user seats with the current subscription plan. Not a billing meter.</summary>
public sealed class UsageQuotaOverviewDto
{
    public bool HasActiveSubscription { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string SubscriptionStatus { get; set; } = "None";
    public DateTime? SubscriptionEndsAt { get; set; }
    public DateOnly? SnapshotDay { get; set; }
    public bool HasSnapshot => SnapshotDay.HasValue;
    /// <summary>Current reserved member seats, independent of historical daily snapshots.</summary>
    public UsageQuotaMetricDto? CurrentUserSeats { get; set; }
    public IReadOnlyList<UsageQuotaMetricDto> Metrics { get; set; } = Array.Empty<UsageQuotaMetricDto>();
}

public sealed class UsageQuotaMetricDto
{
    public string Name { get; set; } = string.Empty;
    public int Used { get; set; }
    public int? Limit { get; set; }
    public bool IsUnlimited { get; set; }
    public decimal? UtilizationPercent { get; set; }
    public bool IsNearLimit { get; set; }
    public bool IsOverLimit { get; set; }

    public static UsageQuotaMetricDto FromCount(string name, int used, int? planLimit)
    {
        var count = Math.Max(0, used);
        var bounded = planLimit is > 0;
        return new UsageQuotaMetricDto
        {
            Name = name, Used = count,
            Limit = bounded ? planLimit : null,
            IsUnlimited = planLimit.HasValue && planLimit.Value <= 0,
            UtilizationPercent = bounded ? decimal.Round(count * 100m / planLimit!.Value, 1) : null,
            IsNearLimit = bounded && count * 10L >= planLimit!.Value * 9L,
            IsOverLimit = bounded && count > planLimit!.Value
        };
    }
}
