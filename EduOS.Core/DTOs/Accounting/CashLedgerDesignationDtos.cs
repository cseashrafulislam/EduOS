namespace EduOS.Core.DTOs.Accounting;

public sealed class SubmitCashLedgerDesignationDto
{
    public Guid ClientRequestId { get; set; }
    public long AccountId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class ReviewCashLedgerDesignationDto
{
    public Guid ReviewRequestId { get; set; }
    public bool Approve { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CashLedgerDesignationDto
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string StateCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public long SubmittedByUserId { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewReason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
