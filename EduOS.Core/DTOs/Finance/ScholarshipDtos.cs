namespace EduOS.Core.DTOs.Finance;

public sealed class SubmitScholarshipRequest
{
    public Guid RequestId { get; set; }
    public Guid EnrollmentReference { get; set; }
    public long DiscountRuleId { get; set; }
    public Guid? EvidenceFileReference { get; set; }
    public Guid? RenewalOfReference { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string EligibilityStatement { get; set; } = "";
}

public sealed class ScholarshipDecisionRequest
{
    public Guid RequestId { get; set; }
    public string RowVersion { get; set; } = "";
    public bool Approve { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class ScholarshipCommandRequest
{
    public Guid RequestId { get; set; }
    public string RowVersion { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class ScholarshipRow
{
    public Guid Reference { get; set; }
    public Guid EnrollmentReference { get; set; }
    public long CampusId { get; set; }
    public long DiscountRuleId { get; set; }
    public string State { get; set; } = "";
    public string EligibilityStatement { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public long SubmittedByUserId { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public long? ReviewedByUserId { get; set; }
    public string? DecisionReason { get; set; }
    public long? StudentDiscountId { get; set; }
    public bool HasEvidence { get; set; }
    public string RowVersion { get; set; } = "";
}

public sealed class ScholarshipPage
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<ScholarshipRow> Items { get; set; } = [];
}

public sealed class ScholarshipReconciliationRow
{
    public Guid ApplicationReference { get; set; }
    public Guid EnrollmentReference { get; set; }
    public string ApplicationState { get; set; } = "";
    public long? LinkedStudentDiscountId { get; set; }
    public long? CanonicalStudentDiscountId { get; set; }
    public bool? IsCanonicalDiscountActive { get; set; }
    public string LineageStatus { get; set; } = "";
}

public sealed class ScholarshipReconciliationPage
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<ScholarshipReconciliationRow> Items { get; set; } = [];
}

/// <summary>Exact invoice-time reductions, never estimated from mutable rules.
/// Cancelled invoice snapshots remain available but are excluded from active totals.</summary>
public sealed class ScholarshipInvoiceCreditRow
{
    public Guid InvoiceReference { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public DateOnly InvoiceDate { get; set; }
    public string InvoiceState { get; set; } = "";
    public string FeeHeadName { get; set; } = "";
    public decimal CreditedAmount { get; set; }
    public decimal RuleValueSnapshot { get; set; }
    public bool IsPercentageSnapshot { get; set; }
}

public sealed class ScholarshipInvoiceCreditPage
{
    public Guid ApplicationReference { get; set; }
    public bool HasCanonicalAward { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public decimal ActiveInvoiceCreditTotal { get; set; }
    public decimal CancelledInvoiceCreditTotal { get; set; }
    public IReadOnlyList<ScholarshipInvoiceCreditRow> Items { get; set; } = [];
}
