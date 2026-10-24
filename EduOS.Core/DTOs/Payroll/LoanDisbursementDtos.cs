namespace EduOS.Core.DTOs.Payroll;

public sealed class LoanDisbursementRowDto
{
    public long LoanId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public decimal PrincipalAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public decimal InstallmentAmount { get; set; }
    public Guid? JournalReference { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class LoanDisbursementRegisterDto
{
    public IReadOnlyList<LoanDisbursementRowDto> Loans { get; set; } = Array.Empty<LoanDisbursementRowDto>();
    public IReadOnlyList<PayrollPostingPeriodDto> OpenPeriods { get; set; } = Array.Empty<PayrollPostingPeriodDto>();
    public IReadOnlyList<PayrollPostingAccountDto> AssetAccounts { get; set; } = Array.Empty<PayrollPostingAccountDto>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}
public sealed class PostLoanDisbursementRequestDto
{
    public DateOnly DisbursementDate { get; set; }
    public long AccountingPeriodId { get; set; }
    public long LoanReceivableAccountId { get; set; }
    public long CashAccountId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class PostLoanDisbursementResultDto
{
    public long LoanId { get; set; }
    public Guid JournalReference { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool AlreadyPosted { get; set; }
}
