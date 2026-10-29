namespace EduOS.Core.DTOs.SaaS;

/// <summary>Read-only comparison of saved daily usage and explicitly identified
/// live capacity with the current subscription plan. Not a billing meter.</summary>
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
    /// <summary>Current active students, independent of historical daily snapshots.</summary>
    public UsageQuotaMetricDto? CurrentStudents { get; set; }
    /// <summary>Current active employees, independent of historical daily snapshots.</summary>
    public UsageQuotaMetricDto? CurrentEmployees { get; set; }
    /// <summary>Current committed tenant file storage, not a historical billing meter.</summary>
    public UsageStorageQuotaMetricDto? CurrentStorage { get; set; }
    public IReadOnlyList<UsageQuotaMetricDto> Metrics { get; set; } = Array.Empty<UsageQuotaMetricDto>();
}

public sealed class UsageStorageQuotaMetricDto
{
    public long UsedBytes { get; set; }
    public long? LimitBytes { get; set; }
    public bool IsUnlimited { get; set; }
    public decimal? UtilizationPercent { get; set; }
    public bool IsNearLimit { get; set; }
    public bool IsOverLimit { get; set; }

    public static UsageStorageQuotaMetricDto FromBytes(long usedBytes, int? maxStorageMb)
    {
        var used = Math.Max(0, usedBytes);
        var limit = maxStorageMb is > 0 ? (long?)maxStorageMb.Value * 1048576L : null;
        return new UsageStorageQuotaMetricDto
        {
            UsedBytes = used, LimitBytes = limit,
            IsUnlimited = maxStorageMb.HasValue && maxStorageMb.Value <= 0,
            UtilizationPercent = limit.HasValue ? decimal.Round(used * 100m / limit.Value, 1) : null,
            IsNearLimit = limit.HasValue && used >= limit.Value - limit.Value / 10,
            IsOverLimit = limit.HasValue && used > limit.Value
        };
    }
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
