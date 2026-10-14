namespace EduOS.Core.DTOs.Finance;
public sealed class FeeReceivablesInvoiceDto
{
    public Guid Reference { get; set; }
    public Guid StudentReference { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CampusName { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal DueAmount { get; set; }
    public int DaysOverdue { get; set; }
    public string AgingBucket { get; set; } = string.Empty;
}
