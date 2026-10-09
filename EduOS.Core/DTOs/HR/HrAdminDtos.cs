namespace EduOS.Core.DTOs.HR;

public sealed class HrEmployeeQueryDto
{
    [MaxLength(100)] public string? Search { get; set; }
    [Range(1, long.MaxValue)] public long? OrganizationUnitId { get; set; }
    public bool? CanTeach { get; set; }
    public EmployeeState? State { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 25;
}

public sealed class HrEmployeeRowDto
{
    public Guid Reference { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string DesignationName { get; set; } = string.Empty;
    public long? OrganizationUnitId { get; set; }
    public string? OrganizationUnitName { get; set; }
    public DateOnly JoiningDate { get; set; }
    public bool CanTeach { get; set; }
    public EmployeeState State { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class HrLeaveQueryDto
{
    [MaxLength(100)] public string? Search { get; set; }
    public LeaveState? State { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 25;
}

public sealed class HrLeaveRowDto
{
    public long Id { get; set; }
    public Guid EmployeeReference { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string LeaveTypeName { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public decimal TotalDays { get; set; }
    public string Reason { get; set; } = string.Empty;
    public LeaveState State { get; set; }
    public string? ReviewNote { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ReviewEmployeeLeaveDto
{
    [Range(1, long.MaxValue)] public long Id { get; set; }
    public LeaveState State { get; set; }
    [MaxLength(1000)] public string? ReviewNote { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
