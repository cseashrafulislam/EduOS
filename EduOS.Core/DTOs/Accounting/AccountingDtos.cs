namespace EduOS.Core.DTOs.Accounting;

public class AccountDto
{
    public long Id { get; set; }
    public long? ParentAccountId { get; set; }
    public string? ParentAccountName { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountType AccountType { get; set; }
    public bool IsGroup { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAccountRequestDto
{
    public long? ParentAccountId { get; set; }
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public AccountType AccountType { get; set; }
    public bool IsGroup { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class AccountingPeriodDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsClosed { get; set; }
    public DateTime? ClosedAt { get; set; }
    public long? ClosedByUserId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAccountingPeriodRequestDto
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? RowVersion { get; set; }
}

public class CloseAccountingPeriodRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class BankAccountDto
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string LedgerAccountName { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string? BranchName { get; set; }
    public string? RoutingNumber { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveBankAccountRequestDto
{
    public long AccountId { get; set; }
    [Required, MaxLength(150)] public string BankName { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string AccountName { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string AccountNumber { get; set; } = string.Empty;
    [MaxLength(100)] public string? BranchName { get; set; }
    [MaxLength(100)] public string? RoutingNumber { get; set; }
    [Required, MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class JournalDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public DateOnly JournalDate { get; set; }
    public long AccountingPeriodId { get; set; }
    public string AccountingPeriodName { get; set; } = string.Empty;
    public JournalState State { get; set; }
    public string? SourceType { get; set; }
    public long? SourceId { get; set; }
    public long? ReversalOfJournalId { get; set; }
    public string? Description { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? PostedByUserId { get; set; }
    public DateTime? PostedAt { get; set; }
    public IReadOnlyList<JournalLineDto> Lines { get; set; } = Array.Empty<JournalLineDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class CreateJournalRequestDto
{
    public Guid ClientRequestId { get; set; }
    public DateOnly JournalDate { get; set; }
    public long AccountingPeriodId { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    public IReadOnlyList<CreateJournalLineRequestDto> Lines { get; set; } = Array.Empty<CreateJournalLineRequestDto>();
}

public class JournalLineDto
{
    public long Id { get; set; }
    public int LineNo { get; set; }
    public long AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string? Description { get; set; }
}

public class CreateJournalLineRequestDto
{
    public int LineNo { get; set; }
    public long AccountId { get; set; }
    [Range(typeof(decimal), "0", "9999999999999999")] public decimal DebitAmount { get; set; }
    [Range(typeof(decimal), "0", "9999999999999999")] public decimal CreditAmount { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
}

public class SubmitJournalRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class ReviewJournalRequestDto
{
    public bool Approve { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class PostJournalRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class ReverseJournalRequestDto
{
    public Guid ClientRequestId { get; set; }
    public DateOnly ReversalDate { get; set; }
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class LedgerLineDto
{
    public DateOnly Date { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public decimal RunningBalance { get; set; }
}

public class TrialBalanceRowDto
{
    public long AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal DebitBalance { get; set; }
    public decimal CreditBalance { get; set; }
}
