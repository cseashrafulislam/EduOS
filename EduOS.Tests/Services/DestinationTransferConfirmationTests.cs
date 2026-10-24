using EduOS.Service.Services.Students;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class DestinationTransferConfirmationTests
{
    [Fact]
    public void Freshness_requires_nonfuture_confirmation_within_ten_minutes()
    {
        var now = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        DestinationTransferConfirmationPolicy.IsFresh(null, now).Should().BeFalse();
        DestinationTransferConfirmationPolicy.IsFresh(now.AddSeconds(1), now).Should().BeFalse();
        DestinationTransferConfirmationPolicy.IsFresh(now.AddMinutes(-10), now).Should().BeTrue();
        DestinationTransferConfirmationPolicy.IsFresh(now.AddMinutes(-10).AddTicks(-1), now).Should().BeFalse();
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var file = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(file)) return File.ReadAllText(file);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }

    [Fact]
    public void Confirmation_revalidates_destination_ownership_and_active_enrollment()
    {
        var service = Read("EduOS.Service", "Services", "Students", "StudentTransferRequestService.Confirmation.cs");
        service.Should().Contain("x.DestinationTenantId == tenant");
        service.Should().Contain("x.TenantId == tenant && x.Id == item.DestinationEnrollmentId.Value");
        service.Should().Contain("x.StudentId == student.Id && x.IsCurrent");
        service.Should().Contain("x.State == EnrollmentState.Active && !x.IsDeleted");
        service.Should().Contain("DestinationEnrollmentVersionAtConfirmation = version");
        service.Should().NotContain("IgnoreQueryFilters");
    }

    [Fact]
    public void Source_completion_requires_recent_attestation_without_destination_personal_data()
    {
        var completion = Read("EduOS.Service", "Services", "Students", "StudentTransferRequestService.Completion.cs");
        var controller = Read("EduOS.App", "Controllers", "Api", "StudentTransferRequestsController.cs");
        var js = Read("EduOS.App", "wwwroot", "js", "students", "transfer-requests.js");
        completion.Should().Contain("DestinationTransferConfirmationPolicy.IsFresh(");
        controller.Should().Contain("ConfirmDestinationEnrollment(");
        controller.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        js.Should().Contain("/confirm-enrollment");
        completion.Should().NotContain("IgnoreQueryFilters");
    }
}
