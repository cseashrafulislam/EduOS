namespace EduOS.Core.DTOs.HR;

public class OrganizationUnitDto
{
    public long Id { get; set; }
    public long? ParentId { get; set; }
    public string? ParentName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveOrganizationUnitRequestDto
{
    public long? ParentId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class DesignationDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int Rank { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveDesignationRequestDto
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public int Rank { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class WorkShiftDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int GraceMinutes { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveWorkShiftRequestDto
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    [Range(0, 240)] public int GraceMinutes { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class EmployeeDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long PersonId { get; set; }
    public long? UserId { get; set; }
    public long? OrganizationUnitId { get; set; }
    public string? OrganizationUnitName { get; set; }
    public long DesignationId { get; set; }
    public string DesignationName { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public DateOnly JoiningDate { get; set; }
    public DateOnly? LeavingDate { get; set; }
    public bool CanTeach { get; set; }
    public EmployeeState State { get; set; }
    public string? PhotoUrl { get; set; }
    public EmployeeShiftAssignmentDto? CurrentShift { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class CreateEmployeeRequestDto
{
    public Guid ClientRequestId { get; set; }
    public long? PersonId { get; set; }
    public long? UserId { get; set; }
    public long? OrganizationUnitId { get; set; }
    public long DesignationId { get; set; }
    [Required, MaxLength(50)] public string EmployeeCode { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [MaxLength(1000)] public string? Address { get; set; }
    public DateOnly JoiningDate { get; set; }
    public bool CanTeach { get; set; }
    [MaxLength(500)] public string? PhotoUrl { get; set; }
}

public class UpdateEmployeeRequestDto
{
    public long? OrganizationUnitId { get; set; }
    public long DesignationId { get; set; }
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [MaxLength(1000)] public string? Address { get; set; }
    public bool CanTeach { get; set; }
    [MaxLength(500)] public string? PhotoUrl { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class ChangeEmployeeStateRequestDto
{
    public EmployeeState State { get; set; }
    public DateOnly? LeavingDate { get; set; }
    [MaxLength(1000)] public string? Reason { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class EmployeeShiftAssignmentDto
{
    public long Id { get; set; }
    public Guid EmployeeReference { get; set; }
    public long WorkShiftId { get; set; }
    public string WorkShiftName { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsCurrent { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class AssignEmployeeShiftRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid EmployeeReference { get; set; }
    public long WorkShiftId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}
