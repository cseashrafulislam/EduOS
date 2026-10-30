using EduOS.Core.Enums.Domain;
namespace EduOS.Core.DTOs.Payroll;

public sealed class PayrollPaymentPostingRowDto
{
    public Guid Reference { get; set; }
    public Guid RunReference { get; set; }
    public string RunNumber { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public long? CampusId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public PaymentMethodType PaymentMethod { get; set; }
    public PaymentState State { get; set; }
    public decimal Amount { get; set; }
    public decimal NetSalaryAmount { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string ReconciliationStatus { get; set; } = string.Empty;
    public string? ReconciliationIssue { get; set; }
    public long? BankAccountId { get; set; }
    public long? BankLedgerId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public Guid? JournalReference { get; set; }
    public Guid? ReturnReference { get; set; }
    public JournalState? ReturnState { get; set; }
    public string? ReturnRowVersion { get; set; }
    public string? ReturnEvidenceReference { get; set; }
    public string? ReturnReason { get; set; }
    public long? ReturnMakerUserId { get; set; }
}
public sealed class PayrollBankReviewExportDto
{
    public string FileName { get; set; } = string.Empty;
    public string Csv { get; set; } = string.Empty;
    public int PaymentCount { get; set; }
}
public sealed class PayrollPaymentPostingRegisterDto
{
    public IReadOnlyList<PayrollPaymentPostingRowDto> Payments { get; set; } = Array.Empty<PayrollPaymentPostingRowDto>();
    public IReadOnlyList<PayrollPostingPeriodDto> OpenPeriods { get; set; } = Array.Empty<PayrollPostingPeriodDto>();
    public IReadOnlyList<PayrollPostingAccountDto> Accounts { get; set; } = Array.Empty<PayrollPostingAccountDto>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}
public sealed class PostPayrollPaymentJournalRequestDto
{
    public long AccountingPeriodId { get; set; }
    public long SettlementAccountId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class PostPayrollPaymentJournalResultDto
{
    public Guid PaymentReference { get; set; }
    public Guid JournalReference { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool AlreadyPosted { get; set; }
}

public sealed class RequestPayrollPaymentReturnDto
{
    public Guid ClientRequestId { get; set; }
    public long AccountingPeriodId { get; set; }
    public DateOnly ReturnDate { get; set; }
    public string EvidenceReference { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ReviewPayrollPaymentReturnDto
{
    public bool Approve { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
}

public sealed class PayrollPaymentReturnResultDto
{
    public Guid PaymentReference { get; set; }
    public Guid ReturnReference { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public JournalState State { get; set; }
    public decimal Amount { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public bool AlreadyProcessed { get; set; }
}
