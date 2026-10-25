using EduOS.Core.Enums.Domain;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Admission;

public sealed class AdmissionCashReceiptWorkspaceDto
{
    public DateOnly Today { get; set; }
    public IReadOnlyList<AdmissionCashApplicantDto> Applicants { get; set; } = Array.Empty<AdmissionCashApplicantDto>();
    public IReadOnlyList<AdmissionCashOptionDto> OpenPeriods { get; set; } = Array.Empty<AdmissionCashOptionDto>();
    public IReadOnlyList<AdmissionCashOptionDto> CashAccounts { get; set; } = Array.Empty<AdmissionCashOptionDto>();
    public IReadOnlyList<AdmissionCashOptionDto> IncomeAccounts { get; set; } = Array.Empty<AdmissionCashOptionDto>();
}

public sealed class AdmissionCashOptionDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}

public sealed class AdmissionCashApplicantDto
{
    public Guid ApplicantReference { get; set; }
    public string ApplicationNumber { get; set; } = string.Empty;
    public string ApplicantName { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public decimal ApplicationFee { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public AdmissionApplicantState State { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public Guid? ReceiptReference { get; set; }
    public string? ReceiptNumber { get; set; }
}

public sealed class CollectAdmissionCashReceiptRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid ApplicantReference { get; set; }
    public DateOnly PaymentDate { get; set; }
    [Range(1, long.MaxValue)] public long AccountingPeriodId { get; set; }
    [Range(1, long.MaxValue)] public long CashAccountId { get; set; }
    [Range(1, long.MaxValue)] public long IncomeAccountId { get; set; }
    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public decimal ExpectedAmount { get; set; }
    [Required] public string ApplicantRowVersion { get; set; } = string.Empty;
}

public sealed class AdmissionCashReceiptDto
{
    public Guid ReceiptReference { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public Guid ApplicantReference { get; set; }
    public string ApplicationNumber { get; set; } = string.Empty;
    public string ApplicantName { get; set; } = string.Empty;
    public string CampusName { get; set; } = string.Empty;
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public PaymentState State { get; set; }
    public Guid JournalReference { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public bool AlreadyRecorded { get; set; }
}
