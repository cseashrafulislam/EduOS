using EduOS.Core.Enums.Domain;
namespace EduOS.Core.DTOs.Finance;
public sealed class RefundOperationsRegisterDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<StudentInvoicePostingPeriodDto> OpenPeriods { get; set; } = Array.Empty<StudentInvoicePostingPeriodDto>();
    public IReadOnlyList<RefundPaymentRegisterRowDto> Payments { get; set; } = Array.Empty<RefundPaymentRegisterRowDto>();
    public IReadOnlyList<RefundRegisterRowDto> Refunds { get; set; } = Array.Empty<RefundRegisterRowDto>();
}
public sealed class RefundPaymentRegisterRowDto
{
    public Guid PaymentReference { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public decimal Amount { get; set; }
    public decimal ReservedAmount { get; set; }
}
public sealed class RefundRegisterRowDto : RefundDto
{
    public string ReceiptNumber { get; set; } = string.Empty;
    public string? CreditNoteNumber { get; set; }
}
