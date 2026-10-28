namespace EduOS.Core.DTOs.Payroll;

public sealed class PayrollSalaryStatementRowDto
{
    public Guid RunReference { get; set; }
    public string RunNumber { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public PayrollRunState State { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal DeductionAmount { get; set; }
    public decimal NetAmount { get; set; }
}
