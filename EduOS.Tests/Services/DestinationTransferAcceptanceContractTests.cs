using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class DestinationTransferAcceptanceContractTests
{
    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }

    [Fact]
    public void Destination_decision_is_tenant_scoped_serializable_and_retry_safe()
    {
        var source = Read("EduOS.Service", "Services", "Students",
            "StudentTransferRequestService.Destination.cs");
        var transaction = Read("EduOS.Service", "Services", "Students",
            "StudentTransferRequestService.cs");
        source.Should().Contain("x.DestinationTenantId == tenant");
        source.Should().Contain("x.SourceTenantId != tenant");
        source.Should().Contain("TransferRequestState.SourceApproved");
        source.Should().Contain("Matches(item.RowVersion, expected)");
        source.Should().Contain("item.AcceptedByUserId = _user.UserId");
        source.Should().Contain("item.RejectedByUserId = _user.UserId");
        source.Should().Contain("item.State == target");
        source.Should().Contain("await _uow.SaveChangesAsync(token)");
        transaction.Should().Contain("System.Data.IsolationLevel.Serializable");
    }

    [Fact]
    public void Destination_inbox_does_not_project_source_private_information()
    {
        var dto = Read("EduOS.Core", "DTOs", "Students", "DestinationTransferDtos.cs");
        dto.Should().NotContain("StudentName");
        dto.Should().NotContain("Guardian");
        dto.Should().NotContain("Phone");
        dto.Should().NotContain("Reason");
        var service = Read("EduOS.Service", "Services", "Students",
            "StudentTransferRequestService.Destination.cs");
        service.Should().Contain("records.Select(MapDestination)");
        service.Should().Contain("x.TenantId == item.SourceTenantId");
        service.Should().Contain("x.StudentEnrollmentId == item.SourceEnrollmentId");
    }

    [Fact]
    public void Destination_api_and_workspace_are_authorized_and_antiforgery_protected()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "StudentTransferRequestsController.cs");
        var view = Read("EduOS.App", "Views", "StudentTransferRequests", "Index.cshtml");
        var js = Read("EduOS.App", "wwwroot", "js", "students", "transfer-requests.js");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        api.Should().Contain("[RequireModule(\"STUDENT\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        api.Should().Contain("destination/inbox");
        api.Should().Contain("DecideDestinationAsync");
        view.Should().Contain("destinationInbox");
        view.Should().Contain("@Html.AntiForgeryToken()");
        js.Should().Contain("RequestVerificationToken");
        js.Should().Contain("credentials: 'same-origin'");
        js.Should().Contain("destination/inbox");
    }
}
