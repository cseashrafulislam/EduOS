using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Finance;

public sealed class SaveFeeStructureDto
{
    [Range(1, long.MaxValue)] public long AcademicYearId { get; set; }
    [Range(1, long.MaxValue)] public long ClassId { get; set; }
    [Range(1, long.MaxValue)] public long FeeHeadId { get; set; }
    [Range(typeof(decimal), "0", "1000000000")] public decimal Amount { get; set; }
}

public sealed class GenerateStudentInvoicesDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long AcademicYearId { get; set; }
    [Range(1, long.MaxValue)] public long ClassId { get; set; }
    [Range(1, long.MaxValue)] public long SectionId { get; set; }
    [Range(1, 12)] public int Month { get; set; }
    [Range(2000, 2200)] public int Year { get; set; }
    public DateTime DueDate { get; set; }
}

public sealed class CollectStudentPaymentDto
{
    public Guid ClientRequestId { get; set; }
    public Guid InvoiceReference { get; set; }
    [Range(typeof(decimal), "0.01", "1000000000")] public decimal Amount { get; set; }
    [Required, RegularExpression("^(Cash|Bkash|Nagad|Card|Bank)$")] public string PaymentMethod { get; set; } = "Cash";
    [StringLength(100)] public string? TransactionId { get; set; }
    [StringLength(500)] public string? Note { get; set; }
    [Range(1, long.MaxValue)] public long? BankAccountId { get; set; }
    [Required] public string InvoiceRowVersion { get; set; } = string.Empty;
}

public sealed class SetInvoiceFineDto
{
    public Guid InvoiceReference { get; set; }
    [Range(typeof(decimal), "0", "1000000000")] public decimal FineAmount { get; set; }
    [Required] public string InvoiceRowVersion { get; set; } = string.Empty;
}

public sealed class StudentInvoiceDto
{
    public Guid Reference { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public long StudentId { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string Roll { get; set; } = string.Empty;
    public string Month { get; set; } = string.Empty;
    public int Year { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FineAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal DueAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class InvoiceBatchResultDto
{
    public Guid ClientRequestId { get; set; }
    public int Generated { get; set; }
    public int Existing { get; set; }
    public List<StudentInvoiceDto> Invoices { get; set; } = new();
}

public sealed class StudentPaymentDto
{
    public Guid Reference { get; set; }
    public Guid InvoiceReference { get; set; }
    public string ReceiptNo { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public string? TransactionId { get; set; }
    public StudentInvoiceDto Invoice { get; set; } = new();
}

public sealed class StudentLedgerDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public decimal TotalBilled { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalDue { get; set; }
    public List<StudentInvoiceDto> Invoices { get; set; } = new();
    public List<StudentPaymentDto> Payments { get; set; } = new();
}
