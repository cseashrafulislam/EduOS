namespace EduOS.Core.DTOs.Finance;

// Advisory read-only projection. GenerateInvoicesAsync rechecks all financial invariants
// under its own transaction; preflight is never an authorization or posting token.
public sealed class InvoiceBatchPreflightDto
{
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public int EligibleCount { get; set; }
    public int NewCount { get; set; }
    public int ExistingCount { get; set; }
    public int ConflictCount { get; set; }
    public decimal NewSubtotal { get; set; }
    public decimal NewDiscount { get; set; }
    public decimal NewTotal { get; set; }
    public bool CanGenerate { get; set; }
    public IReadOnlyList<InvoiceBatchPreflightRowDto> Rows { get; set; } =
        Array.Empty<InvoiceBatchPreflightRowDto>();
}
public sealed class InvoiceBatchPreflightRowDto
{
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? InvoiceNumber { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
}
