using EduOS.Core.Entities.Payroll;
using Xunit;

namespace EduOS.Tests;

public sealed class PayrollIdContractTests
{
    [Theory]
    [InlineData(typeof(SalaryComponent), nameof(SalaryComponent.Id), typeof(long))]
    [InlineData(typeof(SalaryStructure), nameof(SalaryStructure.Id), typeof(long))]
    [InlineData(typeof(SalaryStructure), nameof(SalaryStructure.TenantId), typeof(long))]
    [InlineData(typeof(SalaryStructure), nameof(SalaryStructure.EmployeeId), typeof(long))]
    [InlineData(typeof(SalaryStructureLine), nameof(SalaryStructureLine.SalaryStructureId), typeof(long))]
    [InlineData(typeof(SalaryStructureLine), nameof(SalaryStructureLine.SalaryComponentId), typeof(long))]
    [InlineData(typeof(PayrollRun), nameof(PayrollRun.Id), typeof(long))]
    [InlineData(typeof(PayrollRun), nameof(PayrollRun.TenantId), typeof(long))]
    [InlineData(typeof(PayrollEmployee), nameof(PayrollEmployee.PayrollRunId), typeof(long))]
    [InlineData(typeof(PayrollEmployee), nameof(PayrollEmployee.EmployeeId), typeof(long))]
    [InlineData(typeof(PayrollLine), nameof(PayrollLine.PayrollEmployeeId), typeof(long))]
    [InlineData(typeof(PayrollLine), nameof(PayrollLine.SalaryComponentId), typeof(long))]
    [InlineData(typeof(PayrollPayment), nameof(PayrollPayment.PayrollEmployeeId), typeof(long))]
    [InlineData(typeof(LoanAdvance), nameof(LoanAdvance.Id), typeof(long))]
    [InlineData(typeof(LoanAdvance), nameof(LoanAdvance.TenantId), typeof(long))]
    [InlineData(typeof(LoanAdvance), nameof(LoanAdvance.EmployeeId), typeof(long))]
    [InlineData(typeof(LoanAdvanceRecovery), nameof(LoanAdvanceRecovery.LoanAdvanceId), typeof(long))]
    [InlineData(typeof(Bonus), nameof(Bonus.Id), typeof(long))]
    [InlineData(typeof(Bonus), nameof(Bonus.TenantId), typeof(long))]
    [InlineData(typeof(Bonus), nameof(Bonus.EmployeeId), typeof(long))]
    public void Final_payroll_identifiers_remain_long(Type type, string propertyName, Type expectedType)
    {
        var property = type.GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(expectedType, property!.PropertyType);
    }
}
