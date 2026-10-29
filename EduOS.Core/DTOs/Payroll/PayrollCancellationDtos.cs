using System.ComponentModel.DataAnnotations;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.DTOs.Payroll;

public sealed class CancelPayrollRunRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    [Required, StringLength(1000, MinimumLength = 10)] public string Reason { get; set; } = string.Empty;
}

public sealed class CancelPayrollRunResultDto
{
    public Guid Reference { get; set; }
    public string RunNumber { get; set; } = string.Empty;
    public PayrollRunState State { get; set; }
    public bool AlreadyCancelled { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
