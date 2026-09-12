namespace EduOS.Core.DTOs.Portals;

public sealed class EmployeePortalProfileDto
{
    public Guid Reference { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public int DesignationId { get; set; }
    public int? DepartmentId { get; set; }
    public DateTime JoiningDate { get; set; }
    public decimal Salary { get; set; }
    public string? PhotoUrl { get; set; }
    public string? Qualification { get; set; }
    public string? Experience { get; set; }
    public bool IsTeacher { get; set; }
}
