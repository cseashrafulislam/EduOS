using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentTransferCertificateContractTests
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
    public void Certificate_lookup_requires_source_tenant_student_enrollment_and_transfer_exit()
    {
        var source = Read("EduOS.Service", "Services", "Students", "StudentExitService.Certificate.cs");
        source.Should().Contain("if (!CanManage())");
        source.Should().Contain("x.TenantId == tenantId && !x.IsDeleted");
        source.Should().Contain("x.StudentId == student.Id && x.CertificateNumber == certificateNumber");
        source.Should().Contain("x.Id == certificate.StudentEnrollmentId && x.StudentId == student.Id");
        source.Should().Contain("x.ClientRequestId == certificate.ClientRequestId");
        source.Should().Contain("x.ExitType == StudentExitType.Transfer");
        source.Should().Contain("Certificate not found.");
        source.Should().NotContain("IgnoreQueryFilters");
    }

    [Fact]
    public void Certificate_api_and_print_view_require_admin_and_are_not_cached()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "StudentExitController.cs");
        var mvc = Read("EduOS.App", "Controllers", "StudentLifecycleController.cs");
        var view = Read("EduOS.App", "Views", "StudentLifecycle", "Certificate.cshtml");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        api.Should().Contain("[RequireModule(\"STUDENT\")]");
        api.Should().Contain("GetCertificateAsync");
        mvc.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        mvc.Should().Contain("ResponseCache(NoStore = true");
        mvc.Should().Contain("Referrer-Policy");
        view.Should().Contain("@model EduOS.Core.DTOs.Student.StudentTransferCertificateDto");
        view.Should().Contain("Model.CertificateNumber");
        view.Should().Contain("Model.IssueDate");
        view.Should().Contain("noindex,nofollow,noarchive");
    }

    [Fact]
    public void History_offers_print_only_for_issued_certificates()
    {
        var js = Read("EduOS.App", "wwwroot", "js", "students", "lifecycle.js");
        js.Should().Contain("item.certificateNo");
        js.Should().Contain("StudentLifecycle/Certificate");
        js.Should().Contain("noopener noreferrer");
        js.Should().Contain("new URLSearchParams");
    }
}
