namespace EduOS.Core.DTOs.SaaS;

/// <summary>In-app reminder status for an outstanding renewal invoice.</summary>
public sealed class SubscriptionRenewalReminderDto
{
    public long InvoiceId { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal DueAmount { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public DateTime? LastReminderAt { get; set; }
    public bool HasActiveOwner { get; set; }
    public bool CanSendToday { get; set; }
}
