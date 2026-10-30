using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Inventory;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ProcurementRequisitionServiceTests
{
    private sealed class Actor(long tenant, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => userId;
        public string? FullName => "Procurement test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
    private static EduOSDbContext Open(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static ProcurementRequisitionService Service(EduOSDbContext db, long tenant, long userId, string role)
        => new(db, new Actor(tenant, userId, role), TimeProvider.System,
            NullLogger<ProcurementRequisitionService>.Instance);
    [Fact]
    public async Task Maker_checker_request_replay_and_tenant_IDOR_are_enforced()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("procurement-" + Guid.NewGuid().ToString("N")).Options;
        await using var db = Open(options, 101);
        var campus = new Campus { TenantId = 101, Code = "C", Name = "Campus" };
        var item = new InventoryItem { TenantId = 101, Code = "PEN", Name = "Pens" };
        db.Campuses.Add(campus); db.InventoryItems.Add(item); await db.SaveChangesAsync();
        var maker = Service(db, 101, 10, "TenantAdmin");
        var checker = Service(db, 101, 11, "TenantAdmin");
        var input = new SaveProcurementRequisitionRequest
        {
            ClientRequestId = Guid.NewGuid(), CampusId = campus.Id, Purpose = "Purchase new stationery",
            Lines = [new ProcurementRequisitionLineInput { InventoryItemId = item.Id,
                Quantity = 10, EstimatedUnitPrice = 4.5m }]
        };
        var first = await maker.SaveAsync(null, input);
        Assert.True(first.Success, first.Message);
        var replay = await maker.SaveAsync(null, input);
        Assert.Equal(first.Data!.Id, replay.Data!.Id);
        var mismatch = await maker.SaveAsync(null, new SaveProcurementRequisitionRequest
        {
            ClientRequestId = input.ClientRequestId, CampusId = campus.Id,
            Purpose = "Different procurement purpose",
            Lines = input.Lines
        });
        Assert.Equal(409, mismatch.StatusCode);
        Assert.Equal(1, await db.ProcurementRequisitions.CountAsync());
        var header = await db.ProcurementRequisitions.SingleAsync();
        // InMemory does not generate SQL Server rowversion; test role checks directly.
        header.StateCode = "Submitted";
        await db.SaveChangesAsync();
        var selfApproval = await maker.TransitionAsync(header.Id, "approve",
            new ProcurementDecisionRequest { RowVersion = "invalid" });
        Assert.Equal(403, selfApproval.StatusCode);
        var unauthorized = await Service(db, 101, 12, "StoreKeeper").TransitionAsync(header.Id, "approve",
            new ProcurementDecisionRequest { RowVersion = "invalid" });
        Assert.Equal(403, unauthorized.StatusCode);
        var afterApproval = await maker.SaveAsync(header.Id, input);
        Assert.Equal(409, afterApproval.StatusCode);
        await using var foreign = Open(options, 202);
        Assert.Equal(404, (await Service(foreign, 202, 20, "TenantAdmin").GetAsync(header.Id)).StatusCode);
    }
    [Fact]
    public async Task Invalid_lines_and_unauthorized_campus_fail_before_persistence()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("procurement-invalid-" + Guid.NewGuid().ToString("N")).Options;
        await using var db = Open(options, 101);
        var campus = new Campus { TenantId = 101, Code = "C", Name = "Campus" };
        var item = new InventoryItem { TenantId = 101, Code = "PEN", Name = "Pens" };
        db.Campuses.Add(campus); db.InventoryItems.Add(item); await db.SaveChangesAsync();
        var input = new SaveProcurementRequisitionRequest
        {
            ClientRequestId = Guid.NewGuid(), CampusId = campus.Id, Purpose = "Purchase new stationery",
            Lines = [new ProcurementRequisitionLineInput { InventoryItemId = item.Id, Quantity = -1 }]
        };
        Assert.Equal(400, (await Service(db, 101, 10, "TenantAdmin").SaveAsync(null, input)).StatusCode);
        input.Lines[0].Quantity = 1;
        Assert.Equal(403, (await Service(db, 101, 10, "StoreKeeper").SaveAsync(null, input)).StatusCode);
        Assert.Empty(await db.ProcurementRequisitions.ToListAsync());
    }
}
