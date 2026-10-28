using EduOS.Core.Interfaces.IServices;
using EduOS.Core.DTOs.Payroll;
using Xunit;

namespace EduOS.Tests;

public sealed class PayrollCampusStatementContractTests
{
    [Fact]
    public void Salary_statement_supports_independent_campus_and_period_scope()
    {
        var action = typeof(IPayrollSalaryStatementService).GetMethod(nameof(IPayrollSalaryStatementService.GetAsync));
        Assert.NotNull(action);
        Assert.Contains(action!.GetParameters(), p =>
            p.Name == "campusId" && p.ParameterType == typeof(long?));
        Assert.Equal(typeof(long?), typeof(PayrollSalaryStatementRowDto)
            .GetProperty(nameof(PayrollSalaryStatementRowDto.CampusId))!.PropertyType);
    }
}
