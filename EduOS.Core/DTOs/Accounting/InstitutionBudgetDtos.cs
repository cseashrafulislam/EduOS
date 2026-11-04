namespace EduOS.Core.DTOs.Accounting;

public sealed class InstitutionBudgetLineInputDto
{
    public long AccountId { get; set; }
    public decimal PlannedAmountBdt { get; set; }
}

public sealed class SubmitInstitutionBudgetRequestDto
{
    public Guid ClientRequestId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public IReadOnlyList<InstitutionBudgetLineInputDto> Lines { get; set; } = Array.Empty<InstitutionBudgetLineInputDto>();
}

public sealed class ReviewInstitutionBudgetRequestDto
{
    public Guid ReviewRequestId { get; set; }
    public bool Approve { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class InstitutionBudgetDto
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string StateCode { get; set; } = string.Empty;
    public long SubmittedByUserId { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewReason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<InstitutionBudgetLineReportDto> Lines { get; set; } = Array.Empty<InstitutionBudgetLineReportDto>();
}

public sealed class InstitutionBudgetLineReportDto
{
    public long AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal PlannedAmountBdt { get; set; }
    public decimal ActualAmountBdt { get; set; }
    public decimal VarianceBdt { get; set; }
    public bool IsUnbudgeted { get; set; }
}

public sealed class InstitutionBudgetActualDto
{
    public long BudgetId { get; set; }
    public string BudgetName { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal PlannedIncomeBdt { get; set; }
    public decimal ActualIncomeBdt { get; set; }
    public decimal PlannedExpenseBdt { get; set; }
    public decimal ActualExpenseBdt { get; set; }
    public IReadOnlyList<InstitutionBudgetLineReportDto> Lines { get; set; } = Array.Empty<InstitutionBudgetLineReportDto>();
}
