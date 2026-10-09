namespace EduOS.Core.DTOs.System;

/// <summary>Client-supplied filters only. Tenant ownership and privileges are resolved from the authenticated server context.</summary>
public sealed class AuditLogFilterDto
{
    public long? UserId { get; set; }
    [MaxLength(150)] public string? EntityName { get; set; }
    public long? EntityId { get; set; }
    [MaxLength(100)] public string? Action { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    [MaxLength(100)] public string? IpAddress { get; set; }
    public bool? IsSuccess { get; set; }
    private int _page = 1;
    private int _pageSize = 20;
    public int Page { get => _page; set => _page = Math.Clamp(value, 1, 1_000_000); }
    public int PageSize { get => _pageSize; set => _pageSize = Math.Clamp(value, 1, 100); }
}

public sealed class AuditLogStatisticsDto
{
    public long TotalLogs { get; set; }
    public long TodayLogs { get; set; }
    public long ThisWeekLogs { get; set; }
    public long ThisMonthLogs { get; set; }
    public long CreateActions { get; set; }
    public long UpdateActions { get; set; }
    public long DeleteActions { get; set; }
    public long SuccessfulOperations { get; set; }
    public long FailedOperations { get; set; }
    public IReadOnlyList<AuditEntityActivityDto> TopEntities { get; set; } = Array.Empty<AuditEntityActivityDto>();
    public IReadOnlyList<AuditUserActivityDto> TopUsers { get; set; } = Array.Empty<AuditUserActivityDto>();
}

public sealed class AuditEntityActivityDto
{
    public string EntityName { get; set; } = string.Empty;
    public long ActivityCount { get; set; }
}

public sealed class AuditUserActivityDto
{
    public long UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public long ActivityCount { get; set; }
}
