using EduOS.Core.DTOs.Portals;
using EduOS.Core.Entities;
using Xunit;

namespace EduOS.Tests;

public sealed class EmployeeSelfServiceIdContractTests
{
    [Theory]
    [InlineData(typeof(Employee), nameof(Employee.Id), typeof(long))]
    [InlineData(typeof(Employee), nameof(Employee.TenantId), typeof(long))]
    [InlineData(typeof(Employee), nameof(Employee.UserId), typeof(long?))]
    [InlineData(typeof(EmployeeProfileDto), nameof(EmployeeProfileDto.EmployeeId), typeof(long))]
    [InlineData(typeof(EmployeeLeaveApplicationDto), nameof(EmployeeLeaveApplicationDto.Id), typeof(long))]
    [InlineData(typeof(EmployeePayslipDto), nameof(EmployeePayslipDto.PayrollRunId), typeof(long))]
    public void Employee_self_service_identifiers_remain_long(Type type, string propertyName, Type expectedType)
    {
        var property = type.GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(expectedType, property!.PropertyType);
    }
}
