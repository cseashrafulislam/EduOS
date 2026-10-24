using EduOS.Core.Enums.Domain;
namespace EduOS.Core.DTOs.Payroll;

public sealed class PayrollPaymentPostingRowDto
{
    public Guid Reference { get; set; }
    public string RunNumber { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public long? CampusId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public PaymentMethodType PaymentMethod { get; set; }
    public PaymentState State { get; set; }
    public decimal Amount { get; set; }
    public long? BankAccountId { get; set; }
    public long? BankLedgerId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public Guid? JournalReference { get; set; }
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
