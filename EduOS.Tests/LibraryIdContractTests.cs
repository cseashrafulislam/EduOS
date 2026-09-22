using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Employees;
using Xunit;

namespace EduOS.Tests;

public sealed class LibraryIdContractTests
{
    [Fact]
    public void Issue_contract_uses_public_employee_reference()
    {
        var employeeReference = typeof(IssueBookDto).GetProperty(nameof(IssueBookDto.EmployeeReference));
        var internalEmployeeId = typeof(IssueBookDto).GetProperty("EmployeeId");

        Assert.NotNull(employeeReference);
        Assert.Equal(typeof(Guid?), employeeReference!.PropertyType);
        Assert.Null(internalEmployeeId);
    }

    [Fact]
    public void Employee_has_public_reference_for_cross_boundary_use()
    {
        var property = typeof(Employee).GetProperty(nameof(Employee.PublicId));

        Assert.NotNull(property);
        Assert.Equal(typeof(Guid), property!.PropertyType);
    }
}
