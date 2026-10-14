namespace EduOS.Core.DTOs.Finance;
public sealed class StudentInvoicePostingPeriodDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}
public sealed class StudentInvoicePostingCampusDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
public sealed class StudentInvoicePostingRowDto
{
    public Guid InvoiceReference { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public bool Posted { get; set; }
    public Guid? JournalReference { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class StudentInvoicePostingRegisterDto
{
    public IReadOnlyList<StudentInvoicePostingPeriodDto> OpenPeriods { get; set; } = Array.Empty<StudentInvoicePostingPeriodDto>();
    public IReadOnlyList<StudentInvoicePostingCampusDto> Campuses { get; set; } = Array.Empty<StudentInvoicePostingCampusDto>();
    public IReadOnlyList<StudentInvoicePostingRowDto> Invoices { get; set; } = Array.Empty<StudentInvoicePostingRowDto>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}
public sealed class PostStudentInvoiceJournalRequestDto
{
    public long AccountingPeriodId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class PostStudentInvoiceJournalResultDto
{
    public Guid InvoiceReference { get; set; }
    public Guid JournalReference { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool AlreadyPosted { get; set; }
}
