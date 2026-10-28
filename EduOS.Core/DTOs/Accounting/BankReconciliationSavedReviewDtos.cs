namespace EduOS.Core.DTOs.Accounting;

// Recomputed on request. ReviewedAtUtc is NOT a durable approval event.
public sealed class BankReconciliationSavedReviewDto
{
    public BankReconciliationBatchDto Draft { get; set; } = new();
    public BankStatementPreviewDto Comparison { get; set; } = new();
    public DateTime ReviewedAtUtc { get; set; }
}
