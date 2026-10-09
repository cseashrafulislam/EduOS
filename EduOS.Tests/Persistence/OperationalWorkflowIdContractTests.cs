using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.Transport;
using Xunit;

namespace EduOS.Tests.Persistence;

public sealed class OperationalWorkflowIdContractTests
{
    [Fact]
    public void Library_issue_uses_canonical_copy_and_student_foreign_keys()
    {
        Assert.Equal(typeof(long), typeof(BookIssue).GetProperty(nameof(BookIssue.BookCopyId))!.PropertyType);
        Assert.Equal(typeof(long), typeof(BookIssue).GetProperty(nameof(BookIssue.StudentId))!.PropertyType);
        Assert.Equal(typeof(long), typeof(BookIssue).GetProperty(nameof(BookIssue.IssuedByUserId))!.PropertyType);
        Assert.Equal(typeof(long?), typeof(BookIssue).GetProperty(nameof(BookIssue.ReturnedByUserId))!.PropertyType);
        Assert.Equal(typeof(long), typeof(BookCopy).GetProperty(nameof(BookCopy.BookId))!.PropertyType);
    }

    [Fact]
    public void Transport_assignment_foreign_keys_use_long_ids()
    {
        Assert.Equal(typeof(long), typeof(StudentTransport).GetProperty(nameof(StudentTransport.StudentId))!.PropertyType);
        Assert.Equal(typeof(long), typeof(StudentTransport).GetProperty(nameof(StudentTransport.StudentEnrollmentId))!.PropertyType);
        Assert.Equal(typeof(long), typeof(StudentTransport).GetProperty(nameof(StudentTransport.VehicleId))!.PropertyType);
        Assert.Equal(typeof(long), typeof(StudentTransport).GetProperty(nameof(StudentTransport.RouteId))!.PropertyType);
    }

    [Fact]
    public void Operational_workflows_expose_public_references_and_idempotency_keys()
    {
        Assert.Equal(typeof(Guid), typeof(Book).GetProperty(nameof(Book.PublicId))!.PropertyType);
        Assert.Equal(typeof(Guid), typeof(BookIssue).GetProperty(nameof(BookIssue.ClientRequestId))!.PropertyType);
        Assert.Equal(typeof(Guid), typeof(StudentTransport).GetProperty(nameof(StudentTransport.PublicId))!.PropertyType);
        Assert.Equal(typeof(Guid), typeof(StudentTransport).GetProperty(nameof(StudentTransport.ClientRequestId))!.PropertyType);
    }
}
