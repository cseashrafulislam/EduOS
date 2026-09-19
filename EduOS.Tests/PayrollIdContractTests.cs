using EduOS.Core.Entities.Payroll;
using Xunit;

namespace EduOS.Tests;

public sealed class PayrollIdContractTests
{
    [Theory]
    [InlineData(typeof(SalaryStructure), nameof(SalaryStructure.Id), typeof(long))]
    [InlineData(typeof(SalaryStructure), nameof(SalaryStructure.TenantId), typeof(long))]
    [InlineData(typeof(SalaryStructure), nameof(SalaryStructure.EmployeeId), typeof(long))]
    [InlineData(typeof(Payroll), nameof(Payroll.Id), typeof(long))]
    [InlineData(typeof(Payroll), nameof(Payroll.TenantId), typeof(long))]
    [InlineData(typeof(Payroll), nameof(Payroll.EmployeeId), typeof(long))]
    [InlineData(typeof(Payroll), nameof(Payroll.PaidByUserId), typeof(long?))]
    [InlineData(typeof(Increment), nameof(Increment.Id), typeof(long))]
    [InlineData(typeof(Increment), nameof(Increment.TenantId), typeof(long))]
    [InlineData(typeof(Increment), nameof(Increment.EmployeeId), typeof(long))]
    [InlineData(typeof(LoanAdvance), nameof(LoanAdvance.Id), typeof(long))]
    [InlineData(typeof(LoanAdvance), nameof(LoanAdvance.TenantId), typeof(long))]
    [InlineData(typeof(LoanAdvance), nameof(LoanAdvance.EmployeeId), typeof(long))]
    [InlineData(typeof(Bonus), nameof(Bonus.Id), typeof(long))]
    [InlineData(typeof(Bonus), nameof(Bonus.TenantId), typeof(long))]
    [InlineData(typeof(Bonus), nameof(Bonus.EmployeeId), typeof(long))]
    public void Mapped_payroll_identifiers_remain_long(Type type, string propertyName, Type expectedType)
    {
        var property = type.GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(expectedType, property!.PropertyType);
    }
}
