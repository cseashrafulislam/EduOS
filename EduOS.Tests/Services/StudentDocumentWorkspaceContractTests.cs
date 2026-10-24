using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentDocumentWorkspaceContractTests
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
    public void Student_document_service_enforces_tenant_owner_and_safe_private_files()
    {
        var service = Read("EduOS.Service", "Services", "Students", "StudentDocumentService.cs");
        service.Should().Contain("x.TenantId == user.TenantId && x.PublicId == reference");
        service.Should().Contain("document.EntityType == \"Student\" && document.EntityId == student.Id");
        service.Should().Contain("DocumentFileService.IsTenantDocumentKey");
        service.Should().Contain("!type.IsSensitive && !type.RequiresVerification");
        service.Should().Contain("file.MalwareScanStateCode == \"Clean\"");
        service.Should().Contain("SHA256.HashData(stored.Content)");
        service.Should().Contain("IsolationLevel.Serializable");
        service.Should().Contain("StudentDocument.Attached");
        service.Should().Contain("StudentDocument.Deactivated");
    }
    [Fact]
    public void Student_document_api_and_navigation_are_restricted()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "StudentDocumentsController.cs");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        api.Should().Contain("[RequireModule(\"STUDENT\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        api.Should().Contain("DownloadAsync");
        var layout = Read("EduOS.App", "Views", "Shared", "_Layout.cshtml");
        layout.Should().Contain("href=\"/StudentDocuments/Index\"");
    }
    [Fact]
    public void Student_document_ui_uses_private_upload_and_safe_dom_rendering()
    {
        var js = Read("EduOS.App", "wwwroot", "js", "students", "documents.js");
        js.Should().Contain("'/api/document-admin/files'");
        js.Should().Contain("studentRowVersion: state.student.rowVersion");
        js.Should().Contain("credentials: 'same-origin'");
        js.Should().Contain("RequestVerificationToken");
        js.Should().Contain("textContent");
        js.Should().NotContain("innerHTML");
    }
}
