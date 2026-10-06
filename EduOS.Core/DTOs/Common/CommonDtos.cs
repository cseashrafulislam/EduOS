namespace EduOS.Core.DTOs.Common;

public class PageRequestDto
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 200)] public int PageSize { get; set; } = 20;
    [MaxLength(200)] public string? Search { get; set; }
    [MaxLength(100)] public string? SortBy { get; set; }
    public bool SortDescending { get; set; }
}

public class PageResultDto<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public class LookupDto
{
    public long Id { get; set; }
    public Guid? Reference { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class RowVersionDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class DateRangeQueryDto : PageRequestDto
{
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
}

public class IdempotentRequestDto
{
    public Guid ClientRequestId { get; set; }
}
