using EduOS.Core.Enums.Domain;
namespace EduOS.Core.DTOs.Payroll;

public sealed class PayrollPostingPeriodDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}
public sealed class PayrollPostingAccountDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
}
public sealed class PayrollPostingComponentDto
{
    public long SalaryComponentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SalaryComponentType Type { get; set; }
    public decimal Amount { get; set; }
    public bool IsLoanRecovery { get; set; }
}
public sealed class PayrollPostingRowDto
{
    public Guid Reference { get; set; }
    public string RunNumber { get; set; } = string.Empty;
    public DateOnly PeriodEnd { get; set; }
    public long? CampusId { get; set; }
    public PayrollRunState State { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DeductionAmount { get; set; }
    public decimal NetAmount { get; set; }
    public Guid? JournalReference { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<PayrollPostingComponentDto> Components { get; set; } = Array.Empty<PayrollPostingComponentDto>();
}
public sealed class PayrollPostingRegisterDto
{
    public IReadOnlyList<PayrollPostingPeriodDto> OpenPeriods { get; set; } = Array.Empty<PayrollPostingPeriodDto>();
    public IReadOnlyList<PayrollPostingAccountDto> Accounts { get; set; } = Array.Empty<PayrollPostingAccountDto>();
    public IReadOnlyList<PayrollPostingRowDto> Runs { get; set; } = Array.Empty<PayrollPostingRowDto>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}
public sealed class PayrollComponentAccountMappingDto
{
    public long SalaryComponentId { get; set; }
    public long AccountId { get; set; }
    public long? OffsetAccountId { get; set; }
}
public sealed class PostPayrollJournalRequestDto
{
    public long AccountingPeriodId { get; set; }
    public long SalaryPayableAccountId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<PayrollComponentAccountMappingDto> Mappings { get; set; } = Array.Empty<PayrollComponentAccountMappingDto>();
}
public sealed class PostPayrollJournalResultDto
{
    public Guid PayrollReference { get; set; }
    public Guid JournalReference { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public bool AlreadyPosted { get; set; }
    public int LoanRecoveriesSettled { get; set; }
    public decimal LoanAmountSettled { get; set; }
}
