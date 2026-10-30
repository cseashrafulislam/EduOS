namespace EduOS.Core.DTOs.Inventory;

// Read-only accounting evidence; the run's client request UUID is a tenant-scoped lookup key.
public sealed class V3EnterpriseAssetFinanceTraceDto
{
    public Guid RunReference { get; set; }
    public long CampusId { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string State { get; set; } = "";
    public decimal RunTotalAmount { get; set; }
    public decimal DepreciationLinesAmount { get; set; }
    public int DepreciationLineCount { get; set; }
    public string FinanceEvidenceStatus { get; set; } = "NoExplicitReference";
    public IReadOnlyList<V3EnterpriseJournalEvidenceDto> Journals { get; set; } =
        Array.Empty<V3EnterpriseJournalEvidenceDto>();
}
