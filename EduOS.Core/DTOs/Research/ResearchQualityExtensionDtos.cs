using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Research;

public sealed class ProgramOutcomeCreateDto
{
    public Guid ClientRequestId { get; set; }
    public long AcademicProgramId { get; set; }
    [Required, StringLength(40, MinimumLength = 2)] public string Code { get; set; } = "";
    [Range(1, 1000)] public int VersionNo { get; set; } = 1;
    [Required, StringLength(1000, MinimumLength = 10)] public string Statement { get; set; } = "";
}

public sealed class ProgramOutcomeDto
{
    public long Id { get; set; }
    public long AcademicProgramId { get; set; }
    public string Code { get; set; } = "";
    public int VersionNo { get; set; }
    public string Statement { get; set; } = "";
}

public sealed class StandardOutcomeMapCreateDto
{
    public Guid ClientRequestId { get; set; }
    public long StandardId { get; set; }
    public long OutcomeId { get; set; }
}

public sealed class StandardOutcomeMapDto
{
    public long StandardId { get; set; }
    public long OutcomeId { get; set; }
    public long AcademicProgramId { get; set; }
    public string OutcomeCode { get; set; } = "";
    public int VersionNo { get; set; }
    public string Statement { get; set; } = "";
}

public sealed class AcademicPublicationEvidenceLinkDto
{
    public Guid ClientRequestId { get; set; }
    public long ResultPublicationId { get; set; }
}

public sealed class AcademicEvidenceDto
{
    public long ResultPublicationId { get; set; }
    public int PublicationVersionNo { get; set; }
    public DateTime PublishedAtUtc { get; set; }
    public DateTime LinkedAtUtc { get; set; }
}

public sealed class GrantFinanceAccountCreateDto
{
    public Guid ClientRequestId { get; set; }
    public long SchemeId { get; set; }
    public long AccountId { get; set; }
    [Required, RegularExpression("^(Disbursement|Expense|Refund)$")] public string FlowCode { get; set; } = "";
    [Required, RegularExpression("^(Debit|Credit)$")] public string NormalSide { get; set; } = "";
}

public sealed class GrantFinanceAccountDto
{
    public long Id { get; set; }
    public long SchemeId { get; set; }
    public long AccountId { get; set; }
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string FlowCode { get; set; } = "";
    public string NormalSide { get; set; } = "";
}

public sealed class GrantJournalLinkCreateDto
{
    public Guid ClientRequestId { get; set; }
    public long JournalId { get; set; }
}

public sealed class GrantJournalLinkDto
{
    public long JournalId { get; set; }
    public string JournalNumber { get; set; } = "";
    public DateOnly JournalDate { get; set; }
    public string CurrencyCode { get; set; } = "";
}

public sealed class GrantFinanceReportDto
{
    public Guid GrantReference { get; set; }
    public string CurrencyCode { get; set; } = "";
    public decimal ApprovedBudget { get; set; }
    public decimal Disbursed { get; set; }
    public decimal Expenses { get; set; }
    public decimal Refunds { get; set; }
    public decimal AvailableBudget { get; set; }
    public decimal Undisbursed { get; set; }
    public int LinkedJournals { get; set; }
    public int JournalsMissingAccountMapping { get; set; }
}
