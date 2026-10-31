namespace EduOS.Core.DTOs.Inventory;

// Evidence discovery only: neither a receipt candidate nor an existing book authorizes a GL posting.
public sealed class AssetCapitalizationReadinessDto
{
    public Guid AssetReference { get; set; }
    public string AssetTag { get; set; } = string.Empty;
    public long? CampusId { get; set; }
    public decimal? RegisteredPurchaseCost { get; set; }
    public bool IsAssetTrackedItem { get; set; }
    public bool HasBook { get; set; }
    public bool BookIsActive { get; set; }
    public decimal? BookCostBasis { get; set; }
    public string? BookMethod { get; set; }
    public int? BookPolicyVersion { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public int CandidateLineCount { get; set; }
    public bool CandidateListTruncated { get; set; }
    public List<AssetCapitalizationReceiptCandidateDto> Candidates { get; set; } = [];
}
public sealed class AssetCapitalizationReceiptCandidateDto
{
    public Guid ReceiptReference { get; set; }
    public Guid PostedMovementReference { get; set; }
    public long PurchaseOrderId { get; set; }
    public int LineNo { get; set; }
    public decimal Quantity { get; set; }
    public decimal? PostedUnitCost { get; set; }
    public string LotOrBatchNo { get; set; } = string.Empty;
    public DateTime? ReviewedAtUtc { get; set; }
}
