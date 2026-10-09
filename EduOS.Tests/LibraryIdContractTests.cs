using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.HR;
using Xunit;

namespace EduOS.Tests;

public sealed class LibraryIdContractTests
{
    [Fact]
    public void Issue_contract_uses_public_student_reference()
    {
        var studentReference = typeof(IssueBookDto).GetProperty(nameof(IssueBookDto.StudentReference));
        var internalStudentId = typeof(IssueBookDto).GetProperty("StudentId");

        Assert.NotNull(studentReference);
        Assert.Equal(typeof(Guid), studentReference!.PropertyType);
        Assert.Null(internalStudentId);
    }

    [Fact]
    public void Employee_has_public_reference_for_cross_boundary_use()
    {
        var property = typeof(Employee).GetProperty(nameof(Employee.PublicId));

        Assert.NotNull(property);
        Assert.Equal(typeof(Guid), property!.PropertyType);
    }
}
