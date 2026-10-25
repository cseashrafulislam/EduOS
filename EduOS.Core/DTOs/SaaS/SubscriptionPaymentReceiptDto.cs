namespace EduOS.Core.DTOs.SaaS;

/// <summary>Read-only receipt for a provider-confirmed or bank-verified successful payment.</summary>
public sealed class SubscriptionPaymentReceiptDto
{
    public long PaymentId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public DateTime CompletedAtUtc { get; set; }
    public string? ProviderTransactionId { get; set; }
}
