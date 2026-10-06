using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.HR;

public class OrganizationUnit : BaseTenantEntity
{
    public long? CampusId { get; set; }
    public long? ParentId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class Designation : BaseTenantEntity
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public int Rank { get; set; }
    public bool IsActive { get; set; } = true;
}

public class WorkShift : BaseTenantEntity
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int GraceMinutes { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Employee : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long PersonId { get; set; }
    public long? UserId { get; set; }
    public long? OrganizationUnitId { get; set; }
    public long DesignationId { get; set; }
    [Required, MaxLength(50)] public string EmployeeCode { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(1000)] public string? Address { get; set; }
    public DateOnly JoiningDate { get; set; }
    public DateOnly? ConfirmationDate { get; set; }
    public DateOnly? LeavingDate { get; set; }
    [MaxLength(50)] public string EmploymentTypeCode { get; set; } = "Permanent";
    [MaxLength(50)] public string EmployeeTypeCode { get; set; } = "Staff";
    public DateTime? PersonDataSnapshotAt { get; set; }
    public bool CanTeach { get; set; }
    public EmployeeState State { get; set; } = EmployeeState.Active;
    [MaxLength(500)] public string? PhotoUrl { get; set; }
}

public class EmployeeShiftAssignment : BaseTenantEntity
{
    public long EmployeeId { get; set; }
    public long WorkShiftId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsCurrent { get; set; } = true;
}

public class EmployeeCampusAssignment : BaseTenantEntity
{
    public long EmployeeId { get; set; }
    public long CampusId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsCurrent { get; set; } = true;
}

public class EmployeeBankAccount : BaseTenantEntity
{
    public long EmployeeId { get; set; }
    [Required, MaxLength(150)] public string BankName { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string AccountName { get; set; } = string.Empty;
    [Required, MaxLength(2000)] public string ProtectedAccountNumber { get; set; } = string.Empty;
    [MaxLength(10)] public string? AccountNumberLast4 { get; set; }
    [MaxLength(100)] public string? BranchName { get; set; }
    [MaxLength(100)] public string? RoutingNumber { get; set; }
    [MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
}

public class EmployeeAssignmentHistory : BaseTenantEntity
{
    public long EmployeeId { get; set; }
    public long? CampusId { get; set; }
    public long? OrganizationUnitId { get; set; }
    public long DesignationId { get; set; }
    [MaxLength(50)] public string? EmploymentTypeCode { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsCurrent { get; set; } = true;
    [MaxLength(1000)] public string? Reason { get; set; }
}
