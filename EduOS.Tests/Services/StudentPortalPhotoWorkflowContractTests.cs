using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentPortalPhotoWorkflowContractTests
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
    public void Portal_photo_requires_live_guardian_relationship_and_transactional_recheck()
    {
        var source = Read("EduOS.Service", "Services", "Students", "StudentPhotoService.cs");
        source.Should().Contain("g.UserId == user.UserId").And.Contain("g.IsActive");
        source.Should().Contain("x.StatusCode == \"Active\"").And.Contain("x.TenantId == user.TenantId");
        source.Should().Contain("PortalStudents(true).FirstOrDefaultAsync");
        source.Should().Contain("IsolationLevel.Serializable");
        source.Should().Contain("VersionMatches(student.RowVersion, rowVersion)");
        source.Should().Contain("fileService.UploadPortalAsync");
        source.Should().Contain("Student.PhotoPortalUpdated");
    }

    [Fact]
    public void Portal_photo_download_uses_private_scanned_asset_and_live_authorization()
    {
        var source = Read("EduOS.Service", "Services", "Students", "StudentPhotoService.cs");
        source.Should().Contain("PortalPhotoPath").And.Contain("PhotoPath(studentReference, assetReference)");
        source.Should().Contain("DocumentFileService.IsTenantDocumentKey");
        source.Should().Contain("x.IsVerifiedSafe").And.Contain("x.MalwareScanStateCode");
        source.Should().Contain("SHA256.HashData");
        var api = Read("EduOS.App", "Controllers", "Api", "SelfServicePortalController.cs");
        api.Should().Contain("[Authorize(Roles = \"Student,Guardian,Parent\")]");
        api.Should().Contain("[RequireModule(\"STUDENT\")]");
        api.Should().Contain("_photos.DownloadPortalAsync");
    }

    [Fact]
    public void Portal_photo_form_uses_csrf_and_idempotent_request_id()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "SelfServicePortalController.cs");
        api.Should().Contain("_photos.UploadPortalAsync");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        api.Should().Contain("[RequestSizeLimit(3145728)]");
        var view = Read("EduOS.App", "Views", "StudentPortal", "Index.cshtml");
        view.Should().Contain("portalPhotoForm").And.Contain("student-portal/photo.js");
        var js = Read("EduOS.App", "wwwroot", "js", "student-portal", "photo.js");
        js.Should().Contain("RequestVerificationToken").And.Contain("crypto.randomUUID()");
        js.Should().Contain("portal:student-selected");
        js.Should().NotContain("innerHTML");
    }
}