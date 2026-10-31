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

public sealed class ProcurementSupplierAcknowledgementServiceTests
{
    private sealed class Actor(long tenant, long id, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => id;
        public string? FullName => "Supplier response test";
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
        var http = new DefaultHttpContext(); http.Items["TenantId"] = tenant;
        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "10"),
             new System.Security.Claims.Claim("TenantId", tenant.ToString())], "TestAuthentication"));
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static ProcurementSupplierAcknowledgementService Service(EduOSDbContext db, long tenant,
        long id, string role) => new(db, new Actor(tenant, id, role), TimeProvider.System,
            NullLogger<ProcurementSupplierAcknowledgementService>.Instance);

    [Fact]
    public async Task Confirmation_requires_approved_PO_and_independent_checker()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("ack-" + Guid.NewGuid().ToString("N")).Options;
        await using var db = Open(options, 101);
        var campus = new Campus { TenantId = 101, Code = "C", Name = "Campus" };
        db.Campuses.Add(campus); await db.SaveChangesAsync();
        var req = new ProcurementRequisition { TenantId = 101, CampusId = campus.Id,
            ClientRequestId = Guid.NewGuid(), RequestNumber = "PR-ACK", Purpose = "Office stationery",
            StateCode = "Approved" };
        var supplier = new ProcurementSupplier { TenantId = 101, CampusId = campus.Id,
            ClientRequestId = Guid.NewGuid(), SupplierCode = "S-ACK", Name = "Vendor",
            StateCode = "Approved" };
        db.ProcurementRequisitions.Add(req); db.ProcurementSuppliers.Add(supplier);
        await db.SaveChangesAsync();
        var po = new ProcurementPurchaseOrder { TenantId = 101, CampusId = campus.Id,
            RequisitionId = req.Id, SupplierId = supplier.Id, ClientRequestId = Guid.NewGuid(),
            OrderNumber = "PO-ACK", StateCode = "Approved" };
        db.ProcurementPurchaseOrders.Add(po); await db.SaveChangesAsync();
        var input = new SaveProcurementSupplierAcknowledgementRequest {
            ClientRequestId = Guid.NewGuid(), PurchaseOrderId = po.Id, SupplierReference = "Vendor letter",
            ResponseDate = DateTime.UtcNow.Date, PromisedDeliveryDate = DateTime.UtcNow.Date.AddDays(7)
        };
        var maker = Service(db, 101, 10, "TenantAdmin");
        var created = await maker.SaveAsync(null, input);
        Assert.True(created.Success, created.Message);
        Assert.Equal(created.Data!.Id, (await maker.SaveAsync(null, input)).Data!.Id);
        Assert.Equal(409, (await maker.SaveAsync(null, new SaveProcurementSupplierAcknowledgementRequest {
            ClientRequestId = Guid.NewGuid(), PurchaseOrderId = po.Id,
            SupplierReference = "Different evidence", ResponseDate = input.ResponseDate,
            PromisedDeliveryDate = input.PromisedDeliveryDate
        })).StatusCode);
        Assert.Equal(403, (await maker.TransitionAsync(created.Data.Id, "approve",
            new ProcurementSupplierAcknowledgementDecisionRequest { RowVersion = created.Data.RowVersion })).StatusCode);
        Assert.Equal(403, (await Service(db, 101, 12, "Accountant").SaveAsync(null, input)).StatusCode);
        Assert.Equal(404, (await Service(db, 101, 12, "StoreKeeper").GetAsync(created.Data.Id)).StatusCode);
        Assert.Single(await db.ProcurementSupplierAcknowledgements.ToListAsync());
        Assert.Empty(await db.InventoryMovements.ToListAsync());
        await using var foreign = Open(options, 202);
        Assert.Equal(404, (await Service(foreign, 202, 20, "TenantAdmin").GetAsync(created.Data.Id)).StatusCode);
    }
}
