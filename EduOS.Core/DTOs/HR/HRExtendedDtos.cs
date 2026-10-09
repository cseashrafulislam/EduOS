namespace EduOS.Core.DTOs.HR;

public sealed class EmployeeCampusAssignmentDto
{
    public long Id { get; set; }
    public Guid EmployeeReference { get; set; }
    public long CampusId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsCurrent { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class AssignEmployeeCampusRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid EmployeeReference { get; set; }
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class EmployeeBankAccountDto
{
    public long Id { get; set; }
    public Guid EmployeeReference { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string? AccountNumberLast4 { get; set; }
    public string? BranchName { get; set; }
    public string? RoutingNumber { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveEmployeeBankAccountRequestDto
{
    public Guid EmployeeReference { get; set; }
    [Required, MaxLength(150)] public string BankName { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string AccountName { get; set; } = string.Empty;
    /// <summary>Plaintext input is protected immediately. Never log, cache or return it.</summary>
    [Required, MaxLength(200)] public string AccountNumber { get; set; } = string.Empty;
    [MaxLength(100)] public string? BranchName { get; set; }
    [MaxLength(100)] public string? RoutingNumber { get; set; }
    [Required, MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public sealed class EmployeeAssignmentHistoryDto
{
    public long Id { get; set; }
    public Guid EmployeeReference { get; set; }
    public long? CampusId { get; set; }
    public long? OrganizationUnitId { get; set; }
    public long DesignationId { get; set; }
    public string? EmploymentTypeCode { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsCurrent { get; set; }
    public string? Reason { get; set; }
}
