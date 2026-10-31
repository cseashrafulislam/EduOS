namespace EduOS.Core.DTOs.Finance;

public sealed class ReservationCreditSourceDto
{
    public Guid InvoiceReference { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public decimal OriginalTotal { get; set; }
    public decimal GrossPaid { get; set; }
    public decimal GrossDue { get; set; }
}

public sealed class ReservationCreditAccountDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class ReservationCreditOptionsDto
{
    public IReadOnlyList<ReservationCreditSourceDto> Invoices { get; set; } = [];
    public IReadOnlyList<ReservationCreditAccountDto> LiabilityAccounts { get; set; } = [];
    public bool MoreInvoices { get; set; }
    public bool MoreAccounts { get; set; }
}
