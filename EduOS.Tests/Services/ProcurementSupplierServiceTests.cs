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
public sealed class ProcurementSupplierServiceTests
{
    private sealed class Actor(long tenant,long id,string role):ICurrentUserService
    {
        public bool IsAuthenticated=>true;
        public long TenantId=>tenant;
        public long UserId=>id;
        public string? FullName=>"Supplier test";
        public string? Email=>null;
        public bool IsSuperAdmin=>false;
        public bool IsTenantAdmin=>role=="TenantAdmin";
        public IReadOnlyList<string> Roles=>[role];
        public bool IsInRole(string value)=>role==value;
        public string? IpAddress=>null;
        public string? UserAgent=>null;
    }
    private static EduOSDbContext Open(DbContextOptions<EduOSDbContext> options,long tenant)
    {
        var http=new DefaultHttpContext();
        http.Items["TenantId"]=tenant;
        http.User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier,"10"),
             new System.Security.Claims.Claim("TenantId",tenant.ToString())],"TestAuthentication"));
        return new EduOSDbContext(options,new HttpContextAccessor{HttpContext=http});
    }
    private static ProcurementSupplierService Service(EduOSDbContext db,long tenant,long userId,string role)
        =>new(db,new Actor(tenant,userId,role),TimeProvider.System,NullLogger<ProcurementSupplierService>.Instance);
    [Fact]
    public async Task Supplier_onboarding_is_idempotent_and_tenant_campus_scoped()
    {
        var options=new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase("supplier-"+Guid.NewGuid().ToString("N")).Options;
        await using var db=Open(options,101);
        var campus=new Campus{TenantId=101,Code="C",Name="Campus"};
        db.Campuses.Add(campus);await db.SaveChangesAsync();
        var maker=Service(db,101,10,"TenantAdmin");
        var input=new SaveProcurementSupplierRequest{ClientRequestId=Guid.NewGuid(),CampusId=campus.Id,SupplierCode="s-001",Name="Stationery supplier"};
        var created=await maker.SaveAsync(null,input);
        Assert.True(created.Success,created.Message);
        Assert.Equal("S-001",created.Data!.SupplierCode);
        Assert.Equal(created.Data.Id,(await maker.SaveAsync(null,input)).Data!.Id);
        Assert.Equal(409,(await maker.SaveAsync(null,new SaveProcurementSupplierRequest{
            ClientRequestId=input.ClientRequestId,CampusId=campus.Id,SupplierCode="S-002",Name=input.Name
        })).StatusCode);
        Assert.Equal(403,(await maker.TransitionAsync(created.Data.Id,"approve",
            new ProcurementSupplierDecisionRequest{RowVersion="invalid"})).StatusCode);
        Assert.Equal(404,(await Service(db,101,12,"StoreKeeper").GetAsync(created.Data.Id)).StatusCode);
        await using var foreign=Open(options,202);
        Assert.Equal(404,(await Service(foreign,202,20,"TenantAdmin").GetAsync(created.Data.Id)).StatusCode);
        Assert.Single(await db.ProcurementSuppliers.ToListAsync());
    }
}
