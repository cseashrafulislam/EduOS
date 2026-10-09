namespace EduOS.Core.DTOs.Payroll;

public class SalaryComponentDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public SalaryComponentType Type { get; set; }
    public bool IsTaxable { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveSalaryComponentRequestDto
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public SalaryComponentType Type { get; set; }
    public bool IsTaxable { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class SalaryStructureDto
{
    public long Id { get; set; }
    public Guid EmployeeReference { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsCurrent { get; set; }
    public IReadOnlyList<SalaryStructureLineDto> Lines { get; set; } = Array.Empty<SalaryStructureLineDto>();
    public decimal GrossMonthlyAmount { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveSalaryStructureRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid EmployeeReference { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public IReadOnlyList<SaveSalaryStructureLineRequestDto> Lines { get; set; } = Array.Empty<SaveSalaryStructureLineRequestDto>();
}

public class SalaryStructureLineDto
{
    public long Id { get; set; }
    public long SalaryComponentId { get; set; }
    public string SalaryComponentName { get; set; } = string.Empty;
    public SalaryComponentType Type { get; set; }
    public decimal Amount { get; set; }
}

public class SaveSalaryStructureLineRequestDto
{
    public long SalaryComponentId { get; set; }
    [Range(typeof(decimal), "0", "999999999999")] public decimal Amount { get; set; }
}

public class PayrollRunDto
{
    public long Id { get; set; }
    public string RunNumber { get; set; } = string.Empty;
    public long? CampusId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public Guid Reference { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public PayrollRunState State { get; set; }
    public DateTime? CalculatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? PostedAt { get; set; }
    public long? JournalId { get; set; }
    public int EmployeeCount { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DeductionAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class CreatePayrollRunRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(2000, 2200)] public int Year { get; set; }
    [Range(1, 12)] public int Month { get; set; }
}

public class PayrollEmployeeDto
{
    public long Id { get; set; }
    public Guid EmployeeReference { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal DeductionAmount { get; set; }
    public decimal NetAmount { get; set; }
    public IReadOnlyList<PayrollLineDto> Lines { get; set; } = Array.Empty<PayrollLineDto>();
}

public class PayrollLineDto
{
    public long Id { get; set; }
    public long SalaryComponentId { get; set; }
    public string SalaryComponentName { get; set; } = string.Empty;
    public SalaryComponentType ComponentType { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
}

public class ApprovePayrollRunRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class PostPayrollRunRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class BonusDto
{
    public long Id { get; set; }
    public Guid EmployeeReference { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public DateOnly BonusDate { get; set; }
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveBonusRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid EmployeeReference { get; set; }
    public DateOnly BonusDate { get; set; }
    [Range(typeof(decimal), "0.01", "999999999999")] public decimal Amount { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
}

public class LoanAdvanceDto
{
    public long Id { get; set; }
    public Guid EmployeeReference { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public decimal PrincipalAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public decimal InstallmentAmount { get; set; }
    public string? Remarks { get; set; }
    public bool IsClosed { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class CreateLoanAdvanceRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid EmployeeReference { get; set; }
    public DateOnly IssueDate { get; set; }
    [Range(typeof(decimal), "0.01", "999999999999")] public decimal PrincipalAmount { get; set; }
    [Range(typeof(decimal), "0.01", "999999999999")] public decimal InstallmentAmount { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
}

public sealed class PayrollPaymentDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long PayrollEmployeeId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public PaymentMethodType PaymentMethod { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public PaymentState State { get; set; }
    public long? BankAccountId { get; set; }
    public long? EmployeeBankAccountId { get; set; }
    public string? ExternalReference { get; set; }
    public long? JournalId { get; set; }
}

public sealed class RecordPayrollPaymentRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long PayrollEmployeeId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public PaymentMethodType PaymentMethod { get; set; }
    [Range(typeof(decimal), "0.01", "999999999999")] public decimal Amount { get; set; }
    public long? BankAccountId { get; set; }
    public long? EmployeeBankAccountId { get; set; }
    [MaxLength(150)] public string? ExternalReference { get; set; }
    [Required] public string PayrollEmployeeRowVersion { get; set; } = string.Empty;
}
