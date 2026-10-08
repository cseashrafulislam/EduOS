namespace EduOS.Core.DTOs.Accounting;
public sealed class LedgerStatementDto
{
    public long AccountId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public IReadOnlyList<LedgerLineDto> Entries { get; set; } = Array.Empty<LedgerLineDto>();
}
