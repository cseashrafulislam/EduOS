using EduOS.Core.DTOs.Library;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Helpers.Storage;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

// Exercises real V1 membership and V2 documents in independent SQL contexts.
// These tests use disposable fixture data; no production files are accessed.
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task V2_library_sql_revoke_during_private_storage_read_denies_bytes_and_download_audit()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var asset = await SeedDigitalResourceAsync(fixture);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(asset.Key)).Returns(async () =>
        {
            entered.TrySetResult(true);
            await release.Task.WaitAsync(TimeSpan.FromSeconds(20));
            return new FileDownloadResult { Content = asset.Bytes, ContentType = "application/pdf" };
        });
        var download = Task.Run(async () =>
        {
            await using var readerDb = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await DigitalService(readerDb, fixture, "Student", storage)
                .DownloadAsync(asset.Reference);
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var adminDb = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            var version = await adminDb.Documents.AsNoTracking().Where(x =>
                x.TenantId == fixture.TenantId && x.PublicId == asset.Reference)
                .Select(x => x.RowVersion).SingleAsync();
            var admin = DigitalService(adminDb, fixture, "TenantAdmin", storage);
            var revoked = await admin.RevokeAsync(asset.Reference,
                new LibraryDigitalRevokeRequestDto { RowVersion = Convert.ToBase64String(version) });
            Assert.True(revoked.Success, revoked.Message);
        }
        finally { release.TrySetResult(true); }
        var denied = await download.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.False(denied.Success);
        Assert.Equal(404, denied.StatusCode);
        storage.Verify(x => x.GetPrivateFileAsync(asset.Key), Times.Once);
        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        Assert.Equal(0, await verify.AuditLogs.CountAsync(x =>
            x.TenantId == fixture.TenantId &&
            x.Action == "LibraryDigitalResource.Download" &&
            x.CorrelationId == asset.Reference.ToString("N")));
        Assert.Equal(1, await verify.AuditLogs.CountAsync(x =>
            x.TenantId == fixture.TenantId &&
            x.Action == "LibraryDigitalResource.Revoke" &&
            x.CorrelationId == asset.Reference.ToString("N")));
    }

    [Fact]
    public async Task V2_library_sql_update_and_revoke_with_same_rowversion_have_one_winner()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var asset = await SeedDigitalResourceAsync(fixture);
        byte[] version;
        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            version = await setup.Documents.AsNoTracking().Where(x =>
                x.TenantId == fixture.TenantId && x.PublicId == asset.Reference)
                .Select(x => x.RowVersion).SingleAsync();
        }
        var supplied = Convert.ToBase64String(version);
        async Task<EduOS.Core.Common.ApiResponse<LibraryDigitalResourceDto>> ChangeAsync(bool revoke)
        {
            await using var context = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            var service = DigitalService(context, fixture, "TenantAdmin", new Mock<IFileUploadService>());
            if (revoke)
                return await service.RevokeAsync(asset.Reference,
                    new LibraryDigitalRevokeRequestDto { RowVersion = supplied });
            return await service.UpdateMetadataAsync(asset.Reference,
                new LibraryDigitalMetadataRequestDto
                {
                    RowVersion = supplied, Title = "Concurrent metadata correction",
                    ExpiresOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10))
                });
        }
        var results = await Task.WhenAll(ChangeAsync(false), ChangeAsync(true));
        Assert.Single(results.Where(x => x.Success));
        Assert.Single(results.Where(x => x.StatusCode == 409));
        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var row = await verify.Documents.AsNoTracking().SingleAsync(x =>
            x.TenantId == fixture.TenantId && x.PublicId == asset.Reference);
        var audits = await verify.AuditLogs.AsNoTracking().Where(x =>
            x.TenantId == fixture.TenantId &&
            x.EntityName == "LibraryDigitalResource" &&
            x.CorrelationId == asset.Reference.ToString("N") &&
            (x.Action == "LibraryDigitalResource.Update" ||
             x.Action == "LibraryDigitalResource.Revoke"))
            .Select(x => x.Action).ToListAsync();
        Assert.Single(audits);
        Assert.Equal(row.IsActive ? "LibraryDigitalResource.Update" :
            "LibraryDigitalResource.Revoke", audits[0]);
    }
}
