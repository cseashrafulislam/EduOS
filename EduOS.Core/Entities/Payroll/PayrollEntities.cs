using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Payroll;

public class SalaryComponent : BaseTenantEntity
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public SalaryComponentType Type { get; set; }
    public bool IsTaxable { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SalaryStructure : BaseTenantEntity
{
    public long EmployeeId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsCurrent { get; set; } = true;
}

public class SalaryStructureLine : BaseTenantEntity
{
    public long SalaryStructureId { get; set; }
    public long SalaryComponentId { get; set; }
    [MaxLength(30)] public string CalculationMethodCode { get; set; } = "Fixed";
    public long? BasedOnSalaryComponentId { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? Percentage { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
}

public class PayrollRun : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(50)] public string RunNumber { get; set; } = string.Empty;
    public long? CampusId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public PayrollRunState State { get; set; } = PayrollRunState.Draft;
    public DateTime? CalculatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? PostedAt { get; set; }
    public long? JournalId { get; set; }
}

public class PayrollEmployee : BaseTenantEntity
{
    public long PayrollRunId { get; set; }
    public long EmployeeId { get; set; }
    [MaxLength(50)] public string? EmployeeCodeSnapshot { get; set; }
    [MaxLength(200)] public string? EmployeeNameSnapshot { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal GrossAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DeductionAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal NetAmount { get; set; }
}

public class PayrollLine : BaseTenantEntity
{
    public long PayrollEmployeeId { get; set; }
    public long SalaryComponentId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
}

public class Bonus : BaseTenantEntity
{
    public long EmployeeId { get; set; }
    public DateOnly BonusDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
}

public class LoanAdvance : BaseTenantEntity
{
    public long EmployeeId { get; set; }
    public DateOnly IssueDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PrincipalAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal OutstandingAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal InstallmentAmount { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
    public bool IsClosed { get; set; }
}

public class LoanAdvanceRecovery : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long LoanAdvanceId { get; set; }
    public long? PayrollEmployeeId { get; set; }
    public DateOnly RecoveryDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    public long? JournalId { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
}

public class PayrollPayment : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long PayrollEmployeeId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public PaymentMethodType PaymentMethod { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public long? BankAccountId { get; set; }
    public long? EmployeeBankAccountId { get; set; }
    [MaxLength(150)] public string? ExternalReference { get; set; }
    public PaymentState State { get; set; } = PaymentState.Successful;
    public long? JournalId { get; set; }
}
