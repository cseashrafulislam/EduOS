using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentPortalDocumentsContractTests
{
    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var file = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(file)) return File.ReadAllText(file);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }

    [Fact]
    public void Portal_document_access_requires_a_live_tenant_scoped_student_or_guardian_link()
    {
        var source = Read("EduOS.Service", "Services", "Students", "StudentDocumentService.cs");
        source.Should().Contain("user.IsInRole(\"Student\")");
        source.Should().Contain("guardian.UserId == user.UserId");
        source.Should().Contain("guardian.IsActive");
        source.Should().Contain("!guardian.IsDeleted && !link.IsDeleted");
        source.Should().Contain("student.TenantId == user.TenantId");
        source.Should().Contain("student.StatusCode == \"Active\"");
        source.Should().Contain("DownloadForStudentAsync(student, documentReference, ct)");
    }

    [Fact]
    public void Portal_document_list_and_download_exclude_sensitive_unverified_or_unsafe_files()
    {
        var source = Read("EduOS.Service", "Services", "Students", "StudentDocumentService.cs");
        source.Should().Contain("!type.IsSensitive && !type.RequiresVerification");
        source.Should().Contain("file.MalwareScanStateCode == \"Clean\"");
        source.Should().Contain("file.Visibility == FileVisibility.Private");
        source.Should().Contain("DocumentFileService.IsTenantDocumentKey");
        source.Should().Contain("SHA256.HashData(stored.Content)");
        source.Should().Contain("db.Documents.Count(other => other.TenantId == user.TenantId");
        source.Should().Contain("MapPortal(x.Document, x.File)");
    }

    [Fact]
    public void Portal_api_and_ui_provide_authorized_download_without_admin_mutations()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "SelfServicePortalController.cs");
        api.Should().Contain("[Authorize(Roles = \"Student,Guardian,Parent\")]");
        api.Should().Contain("[RequireModule(\"STUDENT\")]");
        api.Should().Contain("DownloadPortalAsync");
        api.Should().Contain("no-store, private");
        var view = Read("EduOS.App", "Views", "StudentPortal", "Index.cshtml");
        view.Should().Contain("data-portal-tab=\"documents\"");
        var js = Read("EduOS.App", "wwwroot", "js", "student-portal", "index.js");
        // The portal now handles the API's array response explicitly before rendering.
        js.Should().Contain("const rows = Array.isArray(data) ? data : [];");
        js.Should().Contain("renderDocuments(rows, student)");
        js.Should().Contain("credentials: 'same-origin'");
        js.Should().Contain("textContent = 'Download'");
        js.Should().NotContain("innerHTML");
    }
}
