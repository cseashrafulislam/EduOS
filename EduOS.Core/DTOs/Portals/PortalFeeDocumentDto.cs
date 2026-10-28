namespace EduOS.Core.DTOs.Portals;

/// <summary>Read-only student/guardian-owned historical fee document. Never contains internal row IDs.</summary>
public sealed class PortalFeeDocumentDto
{
    public string DocumentType { get; set; } = string.Empty;
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public Guid Reference { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public DateOnly DocumentDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FineAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal DueAmount { get; set; }
    public IReadOnlyList<PortalFeeDocumentLineDto> Lines { get; set; } = [];
}
public sealed class PortalFeeDocumentLineDto
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FineAmount { get; set; }
    public decimal NetAmount { get; set; }
}
