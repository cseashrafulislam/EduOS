using EduOS.Core.DTOs.Accounting;

namespace EduOS.Core.DTOs.Accounting;

// A historical, tenant-owned evidence projection. It does not read or change live journals.
public sealed class BankReconciliationEvidenceRowDto
{
    public int RowNumber { get; set; }
    public DateOnly Date { get; set; }
    public string? Reference { get; set; }
    public decimal Deposit { get; set; }
    public decimal Withdrawal { get; set; }
    public long? MatchEventId { get; set; }
    public long? JournalLineId { get; set; }
    public string? JournalLineVersion { get; set; }
}

public sealed class BankReconciliationEvidenceReportDto
{
    public BankReconciliationBatchDto Statement { get; set; } = new();
    public int StateCode { get; set; }
    public int ConfirmedCount { get; set; }
    public int UnmatchedCount { get; set; }
    public decimal TotalDeposits { get; set; }
    public decimal TotalWithdrawals { get; set; }
    public string? ClearanceFingerprint { get; set; }
    public IReadOnlyList<BankReconciliationEvidenceRowDto> Rows { get; set; } =
        Array.Empty<BankReconciliationEvidenceRowDto>();
    public IReadOnlyList<BankReconciliationApprovalEventDto> ApprovalEvents { get; set; } =
        Array.Empty<BankReconciliationApprovalEventDto>();
}
