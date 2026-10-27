namespace EduOS.Core.DTOs.Accounting;

// Preview only: the close operation rechecks all conditions inside its serializable transaction.
public sealed class AccountingPeriodCloseReadinessDto
{
    public long PeriodId { get; set; }
    public string PeriodName { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public bool IsClosed { get; set; }
    public bool CanClose { get; set; }
    public int DraftCount { get; set; }
    public int SubmittedCount { get; set; }
    public int ApprovedCount { get; set; }
    public int PostedCount { get; set; }
    public int ReversedCount { get; set; }
    public int HistoricalDeletedJournalCount { get; set; }
    public int InvalidJournalCount { get; set; }
    public int InvalidLineCount { get; set; }
    public int UnbalancedJournalCount { get; set; }
    public int MissingAccountCount { get; set; }
    public IReadOnlyList<string> Blockers { get; set; } = Array.Empty<string>();
}
