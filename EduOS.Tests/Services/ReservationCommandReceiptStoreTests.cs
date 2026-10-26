using EduOS.Core.Common;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ReservationCommandReceiptStoreTests
{
    [Fact]
    public async Task Durable_replay_returns_original_outcome_and_rejects_changed_request()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("command-receipt-" + Guid.NewGuid().ToString("N")).Options;
        var key = Guid.NewGuid();
        const string scope = "transport.route-vehicle-change";
        var hash = ReservationCommandReceiptStore.Hash("target:30:vehicle:40:rowversion:abc");
        await using (var db = Context(options, 101))
        {
            var store = new ReservationCommandReceiptStore(db, TimeProvider.System);
            var prior = await store.ReplayAsync<string>(101, 7, scope, key, 30, hash);
            Assert.False(prior.Found);
            await store.RecordAsync(101, 7, scope, key, 30, hash,
                ApiResponse<string>.SuccessResponse("vehicle-40", "Committed."));
            await db.SaveChangesAsync();
        }
        await using (var db = Context(options, 101))
        {
            var store = new ReservationCommandReceiptStore(db, TimeProvider.System);
            var replay = await store.ReplayAsync<string>(101, 7, scope, key, 30, hash);
            Assert.True(replay.Found);
            Assert.False(replay.Conflict);
            Assert.Equal("vehicle-40", replay.Response!.Data);
            Assert.Equal("Committed.", replay.Response.Message);
            Assert.True((await store.ReplayAsync<string>(101, 7, scope, key, 30,
                ReservationCommandReceiptStore.Hash("different"))).Conflict);
            Assert.True((await store.ReplayAsync<string>(101, 7, scope, key, 31, hash)).Conflict);
            Assert.True((await store.ReplayAsync<string>(101, 8, scope, key, 30, hash)).Conflict);
        }
        await using (var db = Context(options, 202))
        {
            var otherTenant = await new ReservationCommandReceiptStore(db, TimeProvider.System)
                .ReplayAsync<string>(202, 7, scope, key, 30, hash);
            Assert.False(otherTenant.Found);
        }
    }

    [Fact]
    public async Task Tampered_saved_result_cannot_be_replayed()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("command-receipt-" + Guid.NewGuid().ToString("N")).Options;
        var key = Guid.NewGuid();
        var hash = ReservationCommandReceiptStore.Hash("original");
        await using var db = Context(options, 101);
        var store = new ReservationCommandReceiptStore(db, TimeProvider.System);
        await store.RecordAsync(101, 7, "transport.rider-extend-upcoming", key, 3, hash,
            ApiResponse<string>.SuccessResponse("original"));
        await db.SaveChangesAsync();
        var row = await db.IdempotencyRecords.SingleAsync();
        row.ResultJson = "{\"Success\":true,\"Data\":\"tampered\"}";
        await db.SaveChangesAsync();
        Assert.True((await store.ReplayAsync<string>(101, 7,
            "transport.rider-extend-upcoming", key, 3, hash)).Conflict);
    }

    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
}
