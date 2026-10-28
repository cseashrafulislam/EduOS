namespace EduOS.Core.DTOs.HR;

public sealed class CreateEmployeeContractRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid EmployeeReference { get; set; }
    [Required, MaxLength(50)] public string ContractCode { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
}
public sealed class ReviewEmployeeContractRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    [MaxLength(500)] public string? Reason { get; set; }
}
public sealed class EmployeeContractDto
{
    public Guid Reference { get; set; }
    public Guid EmployeeReference { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string ContractCode { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Remarks { get; set; }
    public EmployeeContractState State { get; set; }
    public bool CanReview { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewReason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
