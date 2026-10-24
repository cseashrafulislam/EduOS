using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentTransferCompletionContractTests
{
    private static string Read(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }

    [Fact]
    public void Destination_binding_requires_owned_active_student_and_current_enrollment()
    {
        var service = Read("EduOS.Service", "Services", "Students",
            "StudentTransferRequestService.Completion.cs");
        service.Should().Contain("x.DestinationTenantId == tenant");
        service.Should().Contain("x.SourceTenantId != tenant");
        service.Should().Contain("x.TenantId == tenant && x.PublicId == request.StudentReference");
        service.Should().Contain("x.StudentId == student.Id && x.IsCurrent && x.State == EnrollmentState.Active");
        service.Should().Contain("Matches(item.RowVersion, expected)");
        service.Should().Contain("item.DestinationStudentId = student.Id");
        service.Should().Contain("await _uow.SaveChangesAsync(token)");
        service.Should().NotContain("IgnoreQueryFilters");
    }

    [Fact]
    public void Completion_requires_source_exit_and_links_certificate_in_one_transaction()
    {
        var service = Read("EduOS.Service", "Services", "Students",
            "StudentTransferRequestService.Completion.cs");
        var transaction = Read("EduOS.Service", "Services", "Students",
            "StudentTransferRequestService.cs");
        service.Should().Contain("x.SourceTenantId == tenant");
        service.Should().Contain("item.State != TransferRequestState.DestinationAccepted");
        service.Should().Contain("item.DestinationStudentId.HasValue");
        service.Should().Contain("student.StatusCode != \"TC\"");
        service.Should().Contain("enrollment.State != EnrollmentState.Transferred");
        service.Should().Contain("x.ExitType == StudentExitType.Transfer");
        service.Should().Contain("x.ClientRequestId == exit.ClientRequestId");
        service.Should().Contain("certificate.TransferRequestId = item.Id");
        service.Should().Contain("item.State = TransferRequestState.Completed");
        transaction.Should().Contain("System.Data.IsolationLevel.Serializable");
    }

    [Fact]
    public void Api_and_workspace_require_authorization_and_antiforgery()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "StudentTransferRequestsController.cs");
        var view = Read("EduOS.App", "Views", "StudentTransferRequests", "Index.cshtml");
        var js = Read("EduOS.App", "wwwroot", "js", "students", "transfer-requests.js");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        api.Should().Contain("[RequireModule(\"STUDENT\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        api.Should().Contain("LinkDestinationEnrollmentAsync");
        api.Should().Contain("CompleteAsync");
        view.Should().Contain("destinationEnrollmentForm");
        js.Should().Contain("RequestVerificationToken: token");
        js.Should().Contain("/enrollment");
        js.Should().Contain("/complete");
    }

    [Fact]
    public void Destination_projection_does_not_disclose_source_student_or_guardian_data()
    {
        var dto = Read("EduOS.Core", "DTOs", "Students", "DestinationTransferDtos.cs");
        dto.Should().NotContain("Guardian");
        dto.Should().NotContain("StudentName");
        dto.Should().NotContain("Phone");
        dto.Should().NotContain("Reason");
        var completion = Read("EduOS.Service", "Services", "Students",
            "StudentTransferRequestService.Completion.cs");
        completion.Should().Contain("x.TenantId == tenant");
        completion.Should().NotContain("IgnoreQueryFilters");
    }
}
