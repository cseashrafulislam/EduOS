using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Finance;

/// <summary>Historical evidence triage. Mutable award rules cannot prove original entitlement.</summary>
public sealed class FeeDiscountAttributionReviewQueryDto
{
    public long CampusId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class FeeDiscountAttributionReviewRowDto
{
    public Guid InvoiceReference { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PotentiallyMisattributedDiscount { get; set; }
    public bool HasJournalLink { get; set; }
    public bool NeedsReview { get; set; }
    public string EvidenceStatus { get; set; } = string.Empty;
}

public sealed class FeeDiscountAttributionReviewReportDto
{
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public int ReviewRowsOnPage { get; set; }
    public decimal PotentialAmountOnPage { get; set; }
    public string Basis { get; set; } =
        "Read-only triage, not a determination of overcharge or a financial adjustment. " +
        "Current award metadata may have changed since issuance; historical award snapshots " +
        "are not guaranteed. Potential amounts are fee-head attribution signals, not " +
        "changes to invoice grand totals. Totals cover only the displayed page; currencies " +
        "must not be combined. No posted invoice, receipt or journal is changed.";
    public PagedResult<FeeDiscountAttributionReviewRowDto> Invoices { get; set; } = new();
}
