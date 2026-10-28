using EduOS.Core.Enums.Domain;

namespace EduOS.Core.DTOs.Payroll;

/// <summary>Historical payroll employee snapshot and verified settlement; amounts in BDT.</summary>
public sealed class PayrollReconciliationRowDto
{
    public Guid RunReference { get; set; }
    public string RunNumber { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public long? CampusId { get; set; }
    public PayrollRunState RunState { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public decimal NetPayable { get; set; }
    public decimal VerifiedPaid { get; set; }
    public decimal PendingVerification { get; set; }
    public decimal Outstanding { get; set; }
    public string AccrualStatus { get; set; } = string.Empty;
    public string SettlementStatus { get; set; } = string.Empty;
}
