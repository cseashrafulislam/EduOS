using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Finance;

// Gross cash receipts only; refunds and GL posting must be reconciled separately.
public sealed class DailyCashCollectionQueryDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public long? CampusId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}
public sealed class DailyCashCollectionCampusDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
public sealed class DailyCashCollectionSummaryDto
{
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public int ReceiptCount { get; set; }
    public decimal GrossCollected { get; set; }
}
public sealed class DailyCashCollectionRowDto
{
    public DateOnly PaymentDate { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string CampusName { get; set; } = string.Empty;
    public long ReceivedByUserId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string PaymentState { get; set; } = string.Empty;
    // Link presence is NOT proof that the journal is posted or reconciled.
    public bool HasJournalLink { get; set; }
}
public sealed class DailyCashCollectionReportDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public IReadOnlyList<DailyCashCollectionCampusDto> Campuses { get; set; } =
        Array.Empty<DailyCashCollectionCampusDto>();
    public IReadOnlyList<DailyCashCollectionSummaryDto> Summary { get; set; } =
        Array.Empty<DailyCashCollectionSummaryDto>();
    public PagedResult<DailyCashCollectionRowDto> Receipts { get; set; } = new();
}
