using EduOS.Core.Interfaces;
using EduOS.Service.Helpers.Storage;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EduOS.Tests.Services;

public class FileUploadSecurityTests
{
    [Fact]
    public async Task Private_receipt_is_not_given_a_public_url_and_traversal_is_rejected()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), $"eduos-private-files-{Guid.NewGuid():N}");
        try
        {
            var service = new FileUploadService(
                Options.Create(new FileStorageSettings
                {
                    BasePath = Path.Combine(storageRoot, "public"),
                    PrivateBasePath = Path.Combine(storageRoot, "private"),
                    MaxFileSizeMB = 5,
                    AllowedExtensions = [".pdf"],
                    AllowedMimeTypes = ["application/pdf"],
                    GenerateThumbnails = false
                }),
                new TestCurrentUser(),
                NullLogger<FileUploadService>.Instance);
            var content = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };
            await using var stream = new MemoryStream(content);
            var file = new FormFile(stream, 0, content.Length, "depositSlip", "receipt.pdf")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/pdf"
            };

            var upload = await service.UploadPrivateAsync(file, "deposit-slips");

            upload.Success.Should().BeTrue();
            upload.FileUrl.Should().NotStartWith("/");
            upload.FileUrl.Should().NotContain("wwwroot");
            (await service.GetPrivateFileAsync(upload.FileUrl))!.Content.Should().Equal(content);
            (await service.GetPrivateFileAsync("../outside.pdf")).Should().BeNull();
            (await service.GetPrivateFileAsync(Path.Combine(storageRoot, "outside.pdf"))).Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(storageRoot)) Directory.Delete(storageRoot, recursive: true);
        }
    }

    [Fact]
    public void Truncated_signature_is_rejected_without_throwing()
    {
        var service = new FileUploadService(
            Options.Create(new FileStorageSettings
            {
                AllowedExtensions = [".png"],
                AllowedMimeTypes = ["image/png"]
            }),
            new TestCurrentUser(),
            NullLogger<FileUploadService>.Instance);
        var content = new byte[] { 0x89 };
        using var stream = new MemoryStream(content);
        var file = new FormFile(stream, 0, content.Length, "depositSlip", "receipt.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

        service.ValidateFile(file).Should().BeFalse();
    }

    private sealed class TestCurrentUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 51;
        public long TenantId => 701;
        public string? FullName => "Tenant Admin";
        public string? Email => "admin@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
