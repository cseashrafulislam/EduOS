namespace EduOS.Core.DTOs.Inventory;

// Read-only evidence. No accounting or stock posting is inferred from a missing link.
public sealed class V3EnterpriseLedgerTraceDto
{
    public Guid MovementReference { get; set; }
    public string MovementNumber { get; set; } = "";
    public string MovementType { get; set; } = "";
    public string State { get; set; } = "";
    public DateTime? PostedAtUtc { get; set; }
    public string? SourceType { get; set; }
    public long? SourceId { get; set; }
    public Guid? TransferDispatchReference { get; set; }
    public Guid? ReversalReference { get; set; }
    public decimal RecordedMovementValue { get; set; }
    public int MovementLineCount { get; set; }
    public string FinanceEvidenceStatus { get; set; } = "NoExplicitReference";
    public IReadOnlyList<V3EnterpriseJournalEvidenceDto> Journals { get; set; } =
        Array.Empty<V3EnterpriseJournalEvidenceDto>();
}

public sealed class V3EnterpriseJournalEvidenceDto
{
    public Guid Reference { get; set; }
    public string Number { get; set; } = "";
    public string State { get; set; } = "";
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public bool Balanced { get; set; }
    public long? CampusId { get; set; }
}
