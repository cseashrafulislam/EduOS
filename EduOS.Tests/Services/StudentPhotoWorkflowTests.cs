using EduOS.Service.Services.Students;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentPhotoWorkflowTests
{
    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }

    [Fact]
    public void Photo_signature_policy_rejects_spoofed_extensions_and_mime_types()
    {
        var jpg = new byte[] { 0xff, 0xd8, 0xff, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var png = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 0 };
        var webp = new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 };
        StudentPhotoService.HasValidImageSignature(jpg, "portrait.jpg", "image/jpeg").Should().BeTrue();
        StudentPhotoService.HasValidImageSignature(png, "portrait.png", "image/png").Should().BeTrue();
        StudentPhotoService.HasValidImageSignature(webp, "portrait.webp", "image/webp").Should().BeTrue();
        StudentPhotoService.HasValidImageSignature(jpg, "portrait.png", "image/png").Should().BeFalse();
        StudentPhotoService.HasValidImageSignature(jpg, "portrait.svg", "image/svg+xml").Should().BeFalse();
        StudentPhotoService.HasValidImageSignature(new byte[12], "portrait.jpg", "image/jpeg").Should().BeFalse();
    }

    [Fact]
    public void Photo_attachment_requires_scanned_private_asset_version_and_audit()
    {
        var service = Read("EduOS.Service", "Services", "Students", "StudentPhotoService.cs");
        service.Should().Contain("fileService.UploadAsync(requestId");
        service.Should().Contain("IsolationLevel.Serializable");
        service.Should().Contain("VersionMatches(student.RowVersion, rowVersion)");
        service.Should().Contain("x.TenantId == user.TenantId");
        service.Should().Contain("DocumentFileService.IsTenantDocumentKey");
        service.Should().Contain("x.UploadedByUserId == user.UserId");
        service.Should().Contain("Student.PhotoUpdated");
        service.Should().Contain("SHA256.HashData");
        service.Should().Contain("student.PhotoUrl == path");
    }

    [Fact]
    public void Photo_api_and_ui_are_tenant_admin_only_and_csrf_protected()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "StudentPhotosController.cs");
        var js = Read("EduOS.App", "wwwroot", "js", "students", "photo.js");
        var view = Read("EduOS.App", "Views", "Students", "Index.cshtml");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        api.Should().Contain("[RequireModule(\"STUDENT\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        api.Should().Contain("[RequestSizeLimit(3145728)]");
        js.Should().Contain("RequestVerificationToken");
        js.Should().Contain("crypto.randomUUID()");
        js.Should().NotContain("innerHTML");
        view.Should().Contain("studentPhotoForm").And.Contain("@Html.AntiForgeryToken()");
    }
}
