namespace EduOS.Core.DTOs.Inventory;

// A bounded, read-only register of canonical V3 stock/asset evidence.
// Journal status is computed from bounded canonical Finance lines, not inferred PO/asset linkage.
// "NoExplicitReference" does not prove that a Finance posting is absent.
public sealed class V3EnterpriseReconciliationRegisterDto
{
    public string Kind { get; set; } = "";
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public long? CampusId { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<V3EnterpriseReconciliationRegisterRowDto> Items { get; set; } =
        Array.Empty<V3EnterpriseReconciliationRegisterRowDto>();
}

public sealed class V3EnterpriseReconciliationRegisterRowDto
{
    public Guid Reference { get; set; }
    public string Number { get; set; } = "";
    public DateOnly Date { get; set; }
    public long? CampusId { get; set; }
    public string State { get; set; } = "";
    public decimal RecordedValue { get; set; }
    public int LineCount { get; set; }
    public string EvidenceStatus { get; set; } = "";
}
