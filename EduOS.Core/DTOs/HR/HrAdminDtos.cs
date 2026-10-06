using EduOS.Core.Common;
using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.HR;
public sealed class HrEmployeeQueryDto
{
 public string? Search{get;set;}public long? DepartmentId{get;set;}public bool? IsTeacher{get;set;}public bool? IsActive{get;set;}public int Page{get;set;}=1;public int PageSize{get;set;}=25;
}
public sealed class HrEmployeeRowDto
{
 public Guid Reference{get;set;}public string EmployeeCode{get;set;}=string.Empty;public string FullName{get;set;}=string.Empty;public string Phone{get;set;}=string.Empty;public string? Email{get;set;}public string Designation{get;set;}=string.Empty;public string? Department{get;set;}public DateTime JoiningDate{get;set;}public bool IsTeacher{get;set;}public bool IsActive{get;set;}public string RowVersion{get;set;}=string.Empty;
}
public sealed class HrLeaveQueryDto
{
 public string? Search{get;set;}public string? Status{get;set;}public DateTime? From{get;set;}public DateTime? To{get;set;}public int Page{get;set;}=1;public int PageSize{get;set;}=25;
}
public sealed class HrLeaveRowDto
{
 public long Id{get;set;}public Guid EmployeeReference{get;set;}public string EmployeeCode{get;set;}=string.Empty;public string EmployeeName{get;set;}=string.Empty;public string LeaveType{get;set;}=string.Empty;public DateTime FromDate{get;set;}public DateTime ToDate{get;set;}public int TotalDays{get;set;}public string Reason{get;set;}=string.Empty;public string Status{get;set;}=string.Empty;public string? Remarks{get;set;}
}
public sealed class ReviewEmployeeLeaveDto
{
 [Range(1,long.MaxValue)]public long Id{get;set;}[Required,RegularExpression("^(Approved|Rejected)$")]public string Status{get;set;}=string.Empty;[StringLength(500)]public string? Remarks{get;set;}
}
