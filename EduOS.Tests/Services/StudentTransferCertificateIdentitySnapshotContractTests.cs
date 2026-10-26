using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentTransferCertificateIdentitySnapshotContractTests
{
    private static string Read(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }

    [Fact]
    public void Certificate_issuance_snapshots_name_and_code_inside_exit_transaction()
    {
        var entity = Read("EduOS.Core", "Entities", "Students", "StudentEntities.cs");
        var service = Read("EduOS.Service", "Services", "Students", "StudentExitService.cs");
        entity.Should().Contain("StudentNameAtIssue");
        entity.Should().Contain("StudentCodeAtIssue");
        service.Should().Contain("StudentNameAtIssue = student.FullName");
        service.Should().Contain("StudentCodeAtIssue = student.StudentCode");
        service.Should().Contain("ExecuteInTransactionAsync");
    }

    [Fact]
    public void Certificate_print_uses_issued_identity_and_marks_missing_snapshot_fallback()
    {
        var service = Read("EduOS.Service", "Services", "Students", "StudentExitService.Certificate.cs");
        var dto = Read("EduOS.Core", "DTOs", "Students", "StudentTransferCertificateDto.cs");
        var view = Read("EduOS.App", "Views", "StudentLifecycle", "Certificate.cshtml");
        service.Should().Contain("certificate.StudentNameAtIssue ?? student.FullName");
        service.Should().Contain("certificate.StudentCodeAtIssue ?? student.StudentCode");
        service.Should().Contain("IdentityFromIssueSnapshot");
        dto.Should().Contain("IdentityFromIssueSnapshot");
        view.Should().Contain("Model.IdentityFromIssueSnapshot");
        view.Should().Contain("Issue-time identity snapshot unavailable:");
        service.Should().NotContain("IgnoreQueryFilters");
    }

    [Fact]
    public void Migration_does_not_fabricate_historical_names_for_legacy_certificates()
    {
        var migration = Read("EduOS.Persistence", "Migrations",
            "20261025035000_TransferCertificateIdentitySnapshot.cs");
        var snapshot = Read("EduOS.Persistence", "Migrations", "EduOSDbContextModelSnapshot.cs");
        migration.Should().Contain("nullable: true");
        migration.Should().Contain("StudentNameAtIssue");
        migration.Should().Contain("StudentCodeAtIssue");
        migration.Should().NotContain("UPDATE TransferCertificates");
        snapshot.Should().Contain("b.Property<string>(\"StudentNameAtIssue\")");
        snapshot.Should().Contain("b.Property<string>(\"StudentCodeAtIssue\")");
    }
}
