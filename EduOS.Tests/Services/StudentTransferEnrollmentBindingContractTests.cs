using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentTransferEnrollmentBindingContractTests
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
    public void Transfer_persists_exact_destination_enrollment_with_tenant_scoped_fk()
    {
        var entity = Read("EduOS.Core", "Entities", "Students", "StudentEntities.cs");
        var db = Read("EduOS.Persistence", "Context", "EduOSDbContext.cs");
        var migration = Read("EduOS.Persistence", "Migrations",
            "20261025025500_DestinationEnrollmentTransferBinding.cs");
        entity.Should().Contain("public long? DestinationEnrollmentId { get; set; }");
        db.Should().Contain("nameof(TransferRequest.DestinationTenantId), nameof(TransferRequest.DestinationEnrollmentId)");
        db.Should().Contain("HasPrincipalKey(nameof(BaseTenantEntity.TenantId), nameof(BaseEntity.Id))");
        db.Should().Contain("HasFilter(\"[DestinationEnrollmentId] IS NOT NULL AND [IsDeleted] = 0\")");
        migration.Should().Contain("principalColumns: new[] { \"TenantId\", \"Id\" }");
        migration.Should().Contain("unique: true");
        migration.Should().Contain("DropForeignKey(");
        migration.Should().Contain("DropColumn(");
    }

    [Fact]
    public void Destination_binding_is_idempotent_only_for_the_same_active_enrollment()
    {
        var service = Read("EduOS.Service", "Services", "Students",
            "StudentTransferRequestService.Completion.cs");
        service.Should().Contain("x.StudentId == student.Id && x.IsCurrent && x.State == EnrollmentState.Active");
        service.Should().Contain("item.DestinationEnrollmentId.HasValue && item.DestinationEnrollmentId != enrollment.Id");
        service.Should().Contain("item.DestinationStudentId == student.Id && item.DestinationEnrollmentId == enrollment.Id");
        service.Should().Contain("item.DestinationEnrollmentId = enrollment.Id;");
        service.Should().Contain("x.DestinationEnrollmentId == enrollment.Id");
        service.Should().Contain("Matches(item.RowVersion, expected)");
        service.Should().NotContain("IgnoreQueryFilters");
    }

    [Fact]
    public void Source_completion_requires_durable_binding_without_disclosing_destination_identity()
    {
        var service = Read("EduOS.Service", "Services", "Students",
            "StudentTransferRequestService.Completion.cs");
        var projection = Read("EduOS.Service", "Services", "Students",
            "StudentTransferRequestService.cs");
        var inbox = Read("EduOS.Service", "Services", "Students",
            "StudentTransferRequestService.Destination.cs");
        var api = Read("EduOS.App", "Controllers", "Api", "StudentTransferRequestsController.cs");
        var ui = Read("EduOS.App", "wwwroot", "js", "students", "transfer-requests.js");
        service.Should().Contain("!item.DestinationEnrollmentId.HasValue");
        projection.Should().Contain("DestinationEnrollmentLinked = request.DestinationStudentId.HasValue && request.DestinationEnrollmentId.HasValue");
        inbox.Should().Contain("DestinationEnrollmentLinked = item.DestinationStudentId.HasValue && item.DestinationEnrollmentId.HasValue");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        ui.Should().Contain("/enrollment");
        ui.Should().Contain("/complete");
        projection.Should().NotContain("DestinationEnrollmentId =");
    }
}
