using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentPortalDocumentUploadContractTests
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
    public void Portal_upload_rechecks_student_ownership_and_document_type_atomically()
    {
        var source = Read("EduOS.Service", "Services", "Students", "StudentDocumentService.PortalUpload.cs");
        source.Should().Contain("PortalStudentAsync(studentReference, ct)");
        source.Should().Contain("IsolationLevel.Serializable");
        source.Should().Contain("MatchesVersion(current.RowVersion, expectedVersion)");
        source.Should().Contain("!type.IsSensitive && !type.RequiresVerification");
        source.Should().Contain("asset.UploadedByUserId == user.UserId");
        source.Should().Contain("StudentDocument.PortalUploaded");
        source.Should().Contain("document.FileAssetId == file.Id");
    }

    [Fact]
    public void Portal_file_stage_reuses_malware_scanned_private_storage_and_guardian_authorization()
    {
        var source = Read("EduOS.Service", "Services", "Documents", "DocumentFileService.cs");
        source.Should().Contain("UploadPortalAsync(Guid studentReference");
        source.Should().Contain("guardian.UserId == user.UserId && guardian.IsActive");
        source.Should().Contain("student.StatusCode == \"Active\"");
        source.Should().Contain("UploadCoreAsync(clientRequestId, upload, ct)");
        source.Should().Contain("ScanPrivateForMalwareAsync");
        source.Should().Contain("MatchesVerifiedUpload");
    }

    [Fact]
    public void Portal_upload_api_and_js_require_csrf_and_idempotent_request_id()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "SelfServicePortalController.cs");
        api.Should().Contain("[Authorize(Roles = \"Student,Guardian,Parent\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        api.Should().Contain("[RequireModule(\"STUDENT\")]");
        api.Should().Contain("_documents.UploadPortalAsync");
        var js = Read("EduOS.App", "wwwroot", "js", "student-portal", "documents-upload.js");
        js.Should().Contain("RequestVerificationToken");
        js.Should().Contain("crypto.randomUUID()");
        js.Should().Contain("credentials: 'same-origin'");
        js.Should().NotContain("innerHTML");
    }
}
