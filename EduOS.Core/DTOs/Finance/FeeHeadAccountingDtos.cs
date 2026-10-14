using EduOS.Core.Enums.Domain;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Finance;

public sealed class FeeHeadAccountingDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public FeeFrequencyType DefaultFrequency { get; set; }
    public long? IncomeAccountId { get; set; }
    public long? ReceivableAccountId { get; set; }
    public bool IsRefundable { get; set; }
    public bool IsActive { get; set; }
    public bool HasInvoiceHistory { get; set; }
    public bool ReadyForPosting { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class FeeHeadAccountOptionDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountType AccountType { get; set; }
}
public sealed class FeeHeadAccountingSetupDto
{
    public IReadOnlyList<FeeHeadAccountingDto> FeeHeads { get; set; } = Array.Empty<FeeHeadAccountingDto>();
    public IReadOnlyList<FeeHeadAccountOptionDto> PostingAccounts { get; set; } = Array.Empty<FeeHeadAccountOptionDto>();
}
public sealed class SaveFeeHeadAccountingHeadRequestDto
{
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public FeeFrequencyType DefaultFrequency { get; set; } = FeeFrequencyType.Monthly;
    public bool IsRefundable { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}
public sealed class SaveFeeHeadAccountMappingRequestDto
{
    [Range(1, long.MaxValue)] public long IncomeAccountId { get; set; }
    [Range(1, long.MaxValue)] public long ReceivableAccountId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
