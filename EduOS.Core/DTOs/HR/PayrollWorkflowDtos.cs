using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.HR;
public sealed class SaveSalaryStructureDto
{
 public Guid EmployeeReference { get; set; }
 [Range(typeof(decimal),"0","1000000000")] public decimal BasicSalary { get; set; }
 [Range(typeof(decimal),"0","1000000000")] public decimal HouseRent { get; set; }
 [Range(typeof(decimal),"0","1000000000")] public decimal Medical { get; set; }
 [Range(typeof(decimal),"0","1000000000")] public decimal Transport { get; set; }
 [Range(typeof(decimal),"0","1000000000")] public decimal Others { get; set; }
 public DateTime EffectiveFrom { get; set; }
}
public sealed class EmployeeAttendanceItemDto
{
 public Guid EmployeeReference { get; set; }
 [Required,RegularExpression("^(Present|Absent|Late|Leave|Holiday)$")] public string Status { get; set; }="Present";
 public TimeSpan? InTime { get; set; } public TimeSpan? OutTime { get; set; }
 [Range(typeof(decimal),"0","24")] public decimal? OvertimeHours { get; set; }
 [StringLength(500)] public string? Remarks { get; set; }
}
public sealed class SaveEmployeeAttendanceDto
{
 public DateTime Date { get; set; }
 [Required,MinLength(1)] public List<EmployeeAttendanceItemDto> Items { get; set; }=new();
}
public sealed class GeneratePayrollDto
{
 public Guid ClientRequestId { get; set; }
 [Range(1,12)] public int Month { get; set; }
 [Range(2000,2200)] public int Year { get; set; }
}
public sealed class PayPayrollDto
{
 public Guid PayrollReference { get; set; }
 [Required,RegularExpression("^(Cash|Bank|Bkash|Nagad)$")] public string PaymentMethod { get; set; }="Bank";
 [StringLength(500)] public string? Note { get; set; }
 [Required] public string RowVersion { get; set; }=string.Empty;
}
public sealed class PayrollRowDto
{
 public Guid Reference { get; set; } public Guid EmployeeReference { get; set; }
 public string EmployeeCode { get; set; }=string.Empty; public string EmployeeName { get; set; }=string.Empty;
 public string Month { get; set; }=string.Empty; public int Year { get; set; }
 public decimal GrossSalary { get; set; } public int AbsentDays { get; set; }
 public decimal AttendanceDeduction { get; set; } public decimal LoanDeduction { get; set; }
 public decimal Bonus { get; set; } public decimal NetSalary { get; set; }
 public string Status { get; set; }=string.Empty; public DateTime? PaymentDate { get; set; }
 public string RowVersion { get; set; }=string.Empty;
}
public sealed class PayrollBatchDto
{
 public Guid ClientRequestId { get; set; } public int Generated { get; set; } public int Existing { get; set; }
 public List<PayrollRowDto> Rows { get; set; }=new();
}
