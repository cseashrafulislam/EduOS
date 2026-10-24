namespace EduOS.Core.DTOs.Finance;

public sealed class StudentPaymentPostingCashAccountDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
public sealed class StudentPaymentPostingRowDto
{
    public Guid PaymentReference { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public bool Posted { get; set; }
    public Guid? JournalReference { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class StudentPaymentPostingRegisterDto
{
    public IReadOnlyList<StudentInvoicePostingPeriodDto> OpenPeriods { get; set; } = Array.Empty<StudentInvoicePostingPeriodDto>();
    public IReadOnlyList<StudentInvoicePostingCampusDto> Campuses { get; set; } = Array.Empty<StudentInvoicePostingCampusDto>();
    public IReadOnlyList<StudentPaymentPostingCashAccountDto> CashAccounts { get; set; } = Array.Empty<StudentPaymentPostingCashAccountDto>();
    public IReadOnlyList<StudentPaymentPostingRowDto> Payments { get; set; } = Array.Empty<StudentPaymentPostingRowDto>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}
public sealed class PostStudentPaymentJournalRequestDto
{
    public long AccountingPeriodId { get; set; }
    public long CashAccountId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class PostStudentPaymentJournalResultDto
{
    public Guid PaymentReference { get; set; }
    public Guid JournalReference { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool AlreadyPosted { get; set; }
}
