namespace EduOS.Core.DTOs.Payroll;

public sealed class LoanRecoveryLoanDto
{
    public long LoanId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public decimal PrincipalAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public bool IsClosed { get; set; }
    public Guid DisbursementJournalReference { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class LoanRecoveryRegisterDto
{
    public IReadOnlyList<LoanRecoveryLoanDto> Loans { get; set; } = Array.Empty<LoanRecoveryLoanDto>();
    public IReadOnlyList<PayrollPostingPeriodDto> OpenPeriods { get; set; } = Array.Empty<PayrollPostingPeriodDto>();
    public IReadOnlyList<PayrollPostingAccountDto> CashAccounts { get; set; } = Array.Empty<PayrollPostingAccountDto>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}
public sealed class LoanRecoveryHistoryDto
{
    public long RecoveryId { get; set; }
    public DateOnly RecoveryDate { get; set; }
    public decimal Amount { get; set; }
    public string? Remarks { get; set; }
    public Guid JournalReference { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public bool IsCompensated { get; set; }
    public string? CompensationJournalNumber { get; set; }
}
public sealed class PostLoanRecoveryRequestDto
{
    public Guid ClientRequestId { get; set; }
    public DateOnly RecoveryDate { get; set; }
    public decimal Amount { get; set; }
    public long AccountingPeriodId { get; set; }
    public long CashAccountId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public string? Remarks { get; set; }
}
public sealed class PostLoanRecoveryResultDto
{
    public long LoanId { get; set; }
    public long RecoveryId { get; set; }
    public Guid JournalReference { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public bool AlreadyPosted { get; set; }
}
