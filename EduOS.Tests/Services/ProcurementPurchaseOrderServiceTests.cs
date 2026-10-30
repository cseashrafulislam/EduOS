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

public sealed class ProcurementPurchaseOrderServiceTests
{
    private sealed class Actor(long tenant, long id, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => id;
        public string? FullName => "PO test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
    private static EduOSDbContext Open(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext(); http.Items["TenantId"] = tenant;
        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "10"),
             new System.Security.Claims.Claim("TenantId", tenant.ToString())], "TestAuthentication"));
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static ProcurementPurchaseOrderService Service(EduOSDbContext db, long tenant, long id, string role)
        => new(db, new Actor(tenant, id, role), TimeProvider.System,
            NullLogger<ProcurementPurchaseOrderService>.Instance);

    [Fact]
    public async Task Approved_source_creates_one_snapshot_order_and_replay_is_safe()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("po-" + Guid.NewGuid().ToString("N")).Options;
        await using var db = Open(options, 101);
        var campus = new Campus { TenantId = 101, Code = "A", Name = "Campus" };
        var item = new InventoryItem { TenantId = 101, Code = "PEN", Name = "Pens",
            IsActive = true, IsStockTracked = true };
        db.Campuses.Add(campus); db.InventoryItems.Add(item); await db.SaveChangesAsync();
        var req = new ProcurementRequisition { TenantId = 101, CampusId = campus.Id,
            ClientRequestId = Guid.NewGuid(), RequestNumber = "PR-TEST", Purpose = "Buy pens for office",
            StateCode = "Approved", CreatedBy = 10 };
        var supplier = new ProcurementSupplier { TenantId = 101, CampusId = campus.Id,
            ClientRequestId = Guid.NewGuid(), SupplierCode = "S-1", Name = "Office vendor",
            StateCode = "Approved", CreatedBy = 12 };
        db.ProcurementRequisitions.Add(req); db.ProcurementSuppliers.Add(supplier);
        await db.SaveChangesAsync();
        db.ProcurementRequisitionLines.Add(new ProcurementRequisitionLine { TenantId = 101,
            RequisitionId = req.Id, LineNo = 1, InventoryItemId = item.Id,
            Quantity = 12m, EstimatedUnitPrice = 5m });
        await db.SaveChangesAsync();
        var input = new CreateProcurementPurchaseOrderRequest {
            ClientRequestId = Guid.NewGuid(), RequisitionId = req.Id, SupplierId = supplier.Id };
        var maker = Service(db, 101, 10, "TenantAdmin");
        var first = await maker.CreateAsync(input);
        Assert.True(first.Success, first.Message);
        Assert.Equal(60m, first.Data!.TotalAmount);
        Assert.Equal("Draft", first.Data.StateCode);
        Assert.Equal(first.Data.Id, (await maker.CreateAsync(input)).Data!.Id);
        Assert.Equal(409, (await maker.CreateAsync(new CreateProcurementPurchaseOrderRequest {
            ClientRequestId = Guid.NewGuid(), RequisitionId = req.Id, SupplierId = supplier.Id })).StatusCode);
        Assert.Equal(403, (await maker.TransitionAsync(first.Data.Id, "approve",
            new ProcurementPurchaseOrderDecisionRequest { RowVersion = "invalid" })).StatusCode);
        Assert.Equal(404, (await Service(db, 101, 11, "StoreKeeper").GetAsync(first.Data.Id)).StatusCode);
        Assert.Single(await db.ProcurementPurchaseOrderLines.ToListAsync());
        Assert.Empty(await db.InventoryMovements.ToListAsync());
        Assert.Empty((await maker.CommitmentsAsync(1, 25, null, null, null, false, null)).Data!.Items);
        Assert.Equal(403, (await Service(db, 101, 11, "Guest")
            .CommitmentsAsync(1, 25, null, null, null, false, null)).StatusCode);
        Assert.Equal(403, (await Service(db, 101, 11, "Accountant").SaveDraftAsync(first.Data.Id,
            new SaveProcurementPurchaseOrderDraftRequest {
                RowVersion = "invalid", Reason = "Unauthorized draft amendment"
            })).StatusCode);
        Assert.Equal(404, (await Service(db, 101, 11, "TenantAdmin").SaveDraftAsync(999999,
            new SaveProcurementPurchaseOrderDraftRequest {
                RowVersion = "invalid", Reason = "Unrelated draft amendment",
                Lines = [new ProcurementPurchaseOrderDraftLineInput {
                    RequisitionLineId = 1, Quantity = 1m, UnitPrice = 1m }]
            })).StatusCode);
        await using var foreign = Open(options, 202);
        Assert.Equal(404, (await Service(foreign, 202, 20, "TenantAdmin").GetAsync(first.Data.Id)).StatusCode);
    }
}
