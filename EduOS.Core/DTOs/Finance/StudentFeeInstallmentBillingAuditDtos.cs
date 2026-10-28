namespace EduOS.Core.DTOs.Finance;

// Read-only comparison of frozen contractual months with actual issued invoice evidence.
// "Billed" means the plan/invoice/allocations reconcile; it does not certify GL posting.
public sealed class StudentFeeInstallmentBillingAuditDto
{
    public Guid PlanReference { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public int PlanStateCode { get; set; }
    public int Months { get; set; }
    public int BilledMonths { get; set; }
    public int UnbilledMonths { get; set; }
    public int ReviewMonths { get; set; }
    public decimal ContractGross { get; set; }
    public decimal FrozenLineGross { get; set; }
    public decimal VerifiedInvoiceNet { get; set; }
    public IReadOnlyList<string> PlanIssues { get; set; } = [];
    public IReadOnlyList<StudentFeeInstallmentBillingAuditMonthDto> Rows { get; set; } = [];
}

public sealed class StudentFeeInstallmentBillingAuditMonthDto
{
    public DateOnly MonthStart { get; set; }
    public DateOnly? ContractDueDate { get; set; }
    public decimal ContractGross { get; set; }
    public Guid? InvoiceReference { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal? InvoiceGross { get; set; }
    public decimal? InvoiceDiscount { get; set; }
    public decimal? InvoiceNet { get; set; }
    public decimal? InvoicePaid { get; set; }
    public decimal? InvoiceDue { get; set; }
    public bool JournalLinked { get; set; }
    public string Status { get; set; } = "Unbilled";
    public IReadOnlyList<string> Issues { get; set; } = [];
}
