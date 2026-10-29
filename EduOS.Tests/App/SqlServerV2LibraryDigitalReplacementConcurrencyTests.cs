using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Files;
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

/// <summary>
/// Two independent SQL contexts race to supersede the same private resource.
/// Only one version transition and audit may commit; the losing scanned asset
/// must remain unattached and cannot be downloaded as a library publication.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task V2_library_sql_competing_replacements_have_one_active_version_and_replay_winner()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var original = await SeedDigitalResourceAsync(fixture);
        var bytes = original.Bytes;
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var assets = new Dictionary<Guid, long>();
        string version;
        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var previous = await setup.Documents.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == original.Reference);
            version = Convert.ToBase64String(previous.RowVersion);
            foreach (var id in ids)
            {
                var asset = new FileAsset
                {
                    TenantId = fixture.TenantId, PublicId = id,
                    StorageProvider = "LocalPrivate",
                    StorageKey = $"tenant-{fixture.TenantId}/documents/{id:N}.pdf",
                    OriginalFileName = "replacement.pdf", ContentType = "application/pdf",
                    SizeBytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                    UploadedByUserId = fixture.UserId, IsVerifiedSafe = true,
                    MalwareScanStateCode = "Clean", Visibility = FileVisibility.Private
                };
                setup.FileAssets.Add(asset);
                await setup.SaveChangesAsync();
                assets[id] = asset.Id;
            }
        }

        async Task<(Guid Id, ApiResponse<LibraryDigitalResourceDto> Result)> ReplaceAsync(Guid id)
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            var uploads = new Mock<IDocumentFileService>();
            uploads.Setup(x => x.UploadAsync(id, It.IsAny<PrivateFileUploadDto>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ApiResponse<FileAssetDto>.SuccessResponse(new FileAssetDto
                { Id = assets[id], Reference = id, IsVerifiedSafe = true }));
            var service = new LibraryDigitalResourceService(
                db, new DigitalReader(fixture.TenantId, fixture.UserId, "TenantAdmin"),
                uploads.Object, new Mock<IFileUploadService>().Object,
                TimeProvider.System, NullLogger<LibraryDigitalResourceService>.Instance);
            var request = new LibraryDigitalReplaceRequestDto
            {
                RequestId = id, RowVersion = version,
                Title = "Verified replacement", ExpiresOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20))
            };
            var result = await service.ReplaceAsync(original.Reference, request,
                new PrivateFileUploadDto
                {
                    FileName = "replacement.pdf", ContentType = "application/pdf",
                    Length = bytes.Length, Content = new MemoryStream(bytes)
                });
            return (id, result);
        }

        var attempts = await Task.WhenAll(ReplaceAsync(ids[0]), ReplaceAsync(ids[1]));
        var winner = Assert.Single(attempts, x => x.Result.Success);
        Assert.Single(attempts, x => x.Result.StatusCode == 409);
        Assert.Equal(2, winner.Result.Data!.VersionNo);

        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var rows = await verify.Documents.AsNoTracking().Where(x =>
                x.TenantId == fixture.TenantId && x.DocumentTypeCode == "LIBRARY_DIGITAL" &&
                (x.PublicId == original.Reference || ids.Contains(x.PublicId))).ToListAsync();
            Assert.Equal(2, rows.Count);
            var previous = Assert.Single(rows, x => x.PublicId == original.Reference);
            Assert.False(previous.IsActive);
            var current = Assert.Single(rows, x => x.IsActive);
            Assert.Equal(winner.Id, current.PublicId);
            Assert.Equal(previous.Id, current.SupersedesDocumentId);
            Assert.Equal(2, current.VersionNo);
            Assert.Equal(1, await verify.AuditLogs.CountAsync(x =>
                x.TenantId == fixture.TenantId &&
                x.Action == "LibraryDigitalResource.Replace" &&
                x.CorrelationId == winner.Id.ToString("N")));
            var losing = ids.Single(x => x != winner.Id);
            Assert.False(await verify.Documents.AnyAsync(x =>
                x.TenantId == fixture.TenantId && x.FileAssetId == assets[losing]));
        }

        var replay = await ReplaceAsync(winner.Id);
        Assert.True(replay.Result.Success, replay.Result.Message);
        await using var final = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        Assert.Equal(1, await final.AuditLogs.CountAsync(x =>
            x.TenantId == fixture.TenantId &&
            x.Action == "LibraryDigitalResource.Replace"));
    }
}
