namespace EduOS.Core.DTOs.Finance;
public sealed class FeeReceivablesAgingQueryDto
{
    public DateOnly AgingDate { get; set; }
    public long? CampusId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}
