using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentTransferRequestWorkflowContractTests
{
    private static string Read(params string[] path) => File.ReadAllText(Find(path));

    [Fact]
    public void Transfer_source_approval_is_tenant_scoped_and_does_not_move_student()
    {
        var source = Read("EduOS.Service", "Services", "Students", "StudentTransferRequestService.cs");
        var submit = Read("EduOS.Service", "Services", "Students", "StudentTransferRequestService.Submit.cs");
        var review = Read("EduOS.Service", "Services", "Students", "StudentTransferRequestService.Review.cs");
        source.Should().Contain("_user.IsTenantAdmin");
        submit.Should().Contain("x.SourceTenantId == tenant");
        submit.Should().Contain("x.ClientRequestId == request.ClientRequestId");
        submit.Should().Contain("EnrollmentState.Active");
        submit.Should().Contain("student.RowVersion");
        submit.Should().Contain("enrollment.RowVersion");
        submit.Should().Contain("TransferRequestState.Submitted");
        review.Should().Contain("TransferRequestState.SourceApproved");
        review.Should().Contain("TransferRequestState.Rejected");
        review.Should().Contain("Matches(item.RowVersion, expected)");
        review.Should().NotContain("enrollment.IsCurrent = false");
        review.Should().NotContain("StudentExitRecord {");
        source.Should().Contain("System.Data.IsolationLevel.Serializable");
    }

    [Fact]
    public void Transfer_api_and_workspace_require_tenant_admin_and_antiforgery()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "StudentTransferRequestsController.cs");
        var mvc = Read("EduOS.App", "Controllers", "StudentTransferRequestsController.cs");
        var view = Read("EduOS.App", "Views", "StudentTransferRequests", "Index.cshtml");
        var js = Read("EduOS.App", "wwwroot", "js", "students", "transfer-requests.js");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        api.Should().Contain("[Route(\"api/student-transfer-requests\")]");
        mvc.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        view.Should().Contain("@Html.AntiForgeryToken()");
        js.Should().Contain("RequestVerificationToken: token");
        js.Should().Contain("studentRowVersion: selected.studentRowVersion");
        js.Should().Contain("enrollmentRowVersion: selected.enrollmentRowVersion");
        js.Should().Contain("rowVersion: item.rowVersion");
    }

    private static string Find(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Missing workflow file: " + string.Join("/", segments));
    }
}
