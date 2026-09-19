using EduOS.Core.DTOs.Portals;
using EduOS.Core.Entities.Employees;
using Xunit;

namespace EduOS.Tests;

public sealed class EmployeeSelfServiceIdContractTests
{
    [Theory]
    [InlineData(typeof(Employee), nameof(Employee.Id), typeof(long))]
    [InlineData(typeof(Employee), nameof(Employee.TenantId), typeof(long))]
    [InlineData(typeof(Employee), nameof(Employee.UserId), typeof(long?))]
    [InlineData(typeof(EmployeePortalProfileDto), nameof(EmployeePortalProfileDto.DesignationId), typeof(long))]
    [InlineData(typeof(EmployeePortalProfileDto), nameof(EmployeePortalProfileDto.DepartmentId), typeof(long?))]
    [InlineData(typeof(EmployeePortalLeaveDto), nameof(EmployeePortalLeaveDto.Id), typeof(long))]
    [InlineData(typeof(EmployeePortalLeaveBalanceDto), nameof(EmployeePortalLeaveBalanceDto.LeaveTypeId), typeof(long))]
    [InlineData(typeof(EmployeePortalLeaveApplyDto), nameof(EmployeePortalLeaveApplyDto.LeaveTypeId), typeof(long))]
    public void Employee_self_service_identifiers_remain_long(Type type, string propertyName, Type expectedType)
    {
        var property = type.GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(expectedType, property!.PropertyType);
    }
}
