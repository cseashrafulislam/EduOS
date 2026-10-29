using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.Library;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Cryptography;
using Xunit;

namespace EduOS.Tests.App;

// SQL Server is the source of truth for publication uniqueness, not an InMemory
// provider or an optimistic UI request-id check.
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task V2_library_sql_parallel_same_request_publication_is_single_and_replayable()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var requestId = Guid.NewGuid();
        var bytes = "%PDF-1.4\nPublication race fixture"u8.ToArray();
        Guid bookReference;
        long assetId;
        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var book = new Book { TenantId = fixture.TenantId, Title = "Race-safe digital book", IsActive = true };
            var asset = new FileAsset
            {
                TenantId = fixture.TenantId, PublicId = requestId,
                StorageProvider = "LocalPrivate",
                StorageKey = $"tenant-{fixture.TenantId}/documents/{requestId:N}.pdf",
                OriginalFileName = "race.pdf", ContentType = "application/pdf",
                SizeBytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                UploadedByUserId = fixture.UserId, IsVerifiedSafe = true,
                MalwareScanStateCode = "Clean", Visibility = FileVisibility.Private
            };
            setup.AddRange(book, asset, new DocumentTypeDefinition
            {
                TenantId = fixture.TenantId, Code = "LIBRARY_DIGITAL",
                Name = "Digital library", IsActive = true
            });
            await setup.SaveChangesAsync();
            bookReference = book.PublicId;
            assetId = asset.Id;
        }

        var files = new Mock<IDocumentFileService>();
        files.Setup(x => x.UploadAsync(requestId, It.IsAny<PrivateFileUploadDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<FileAssetDto>.SuccessResponse(new FileAssetDto
            { Id = assetId, Reference = requestId, IsVerifiedSafe = true }));
        var request = new LibraryDigitalUploadRequestDto
        {
            RequestId = requestId, BookReference = bookReference,
            Title = "Single publication", ExpiresOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10))
        };
        async Task<ApiResponse<LibraryDigitalResourceDto>> PublishAsync()
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            var service = new LibraryDigitalResourceService(
                db, new DigitalReader(fixture.TenantId, fixture.UserId, "TenantAdmin"),
                files.Object, new Mock<IFileUploadService>().Object,
                TimeProvider.System, NullLogger<LibraryDigitalResourceService>.Instance);
            return await service.UploadAsync(request, new PrivateFileUploadDto
            {
                FileName = "race.pdf", ContentType = "application/pdf",
                Length = bytes.Length, Content = new MemoryStream(bytes)
            });
        }

        var attempts = await Task.WhenAll(PublishAsync(), PublishAsync());
        Assert.Contains(attempts, x => x.Success);
        Assert.All(attempts, x => Assert.True(x.Success || x.StatusCode == 409,
            $"Unexpected concurrent publication status {x.StatusCode}: {x.Message}"));

        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            Assert.Equal(1, await verify.Documents.CountAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == requestId &&
                x.DocumentTypeCode == "LIBRARY_DIGITAL"));
            Assert.Equal(1, await verify.AuditLogs.CountAsync(x =>
                x.TenantId == fixture.TenantId && x.Action == "LibraryDigitalResource.Publish" &&
                x.CorrelationId == requestId.ToString("N")));
        }
        // The losing caller may safely retry the same immutable request after the
        // winning transaction commits; no second document or audit is created.
        var replay = await PublishAsync();
        Assert.True(replay.Success, replay.Message);
        await using var final = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        Assert.Equal(1, await final.Documents.CountAsync(x =>
            x.TenantId == fixture.TenantId && x.PublicId == requestId));
        Assert.Equal(1, await final.AuditLogs.CountAsync(x =>
            x.TenantId == fixture.TenantId && x.Action == "LibraryDigitalResource.Publish" &&
            x.CorrelationId == requestId.ToString("N")));
    }
}
