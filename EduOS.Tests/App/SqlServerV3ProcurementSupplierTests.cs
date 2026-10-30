using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Inventory;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace EduOS.Tests.App;
[Collection("SqlServerIntegration")]
public sealed class SqlServerV3ProcurementSupplierTests
{
    private sealed class Actor(long tenant,long id):ICurrentUserService
    {
        public bool IsAuthenticated=>true;
        public long TenantId=>tenant;
        public long UserId=>id;
        public string? FullName=>"Supplier SQL";
        public string? Email=>null;
        public bool IsSuperAdmin=>false;
        public bool IsTenantAdmin=>true;
        public IReadOnlyList<string> Roles=>["TenantAdmin"];
        public bool IsInRole(string role)=>role=="TenantAdmin";
        public string? IpAddress=>null;
        public string? UserAgent=>null;
    }
    private static EduOSDbContext Open(DbContextOptions<EduOSDbContext> options,long tenant,long userId=0)
    {
        var http=new DefaultHttpContext();http.Items["TenantId"]=tenant;
        if(userId>0)http.User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier,userId.ToString()),
             new System.Security.Claims.Claim("TenantId",tenant.ToString())],"TestAuthentication"));
        return new EduOSDbContext(options,new HttpContextAccessor{HttpContext=http});
    }
    private static ProcurementSupplierService Service(EduOSDbContext db,long tenant,long id)
        =>new(db,new Actor(tenant,id),TimeProvider.System,NullLogger<ProcurementSupplierService>.Instance);
    [Fact]
    public async Task SQL_supplier_request_replay_and_competing_decisions()
    {
        var connection=Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if(string.IsNullOrWhiteSpace(connection))return;
        var options=new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix=Guid.NewGuid().ToString("N")[..12];
        long tenantId,makerId,checkerId;
        await using(var root=new EduOSDbContext(options))
        {
            var tenant=new Tenant{Code="SUP-"+suffix,Name="Supplier SQL",
                Email="sup-"+suffix+"@example.test",State=TenantState.Active};
            var maker=new ApplicationUser{UserName="sup-m-"+suffix,NormalizedUserName=("sup-m-"+suffix).ToUpperInvariant(),
                Email="sup-m-"+suffix+"@example.test",NormalizedEmail=("sup-m-"+suffix+"@example.test").ToUpperInvariant(),FullName="Maker"};
            var checker=new ApplicationUser{UserName="sup-c-"+suffix,NormalizedUserName=("sup-c-"+suffix).ToUpperInvariant(),
                Email="sup-c-"+suffix+"@example.test",NormalizedEmail=("sup-c-"+suffix+"@example.test").ToUpperInvariant(),FullName="Checker"};
            root.Tenants.Add(tenant);root.ApplicationUsers.AddRange(maker,checker);
            await root.SaveChangesAsync();tenantId=tenant.Id;makerId=maker.Id;checkerId=checker.Id;
        }
        long campusId;
        await using(var seed=Open(options,tenantId))
        {
            var campus=new Campus{TenantId=tenantId,Code="C-"+suffix,Name="Campus"};
            seed.Campuses.Add(campus);await seed.SaveChangesAsync();campusId=campus.Id;
        }
        var input=new SaveProcurementSupplierRequest{ClientRequestId=Guid.NewGuid(),
            CampusId=campusId,SupplierCode="SUP-"+suffix,Name="Office supplies vendor"};
        long id;string version;
        await using(var makerDb=Open(options,tenantId,makerId))
        {
            var maker=Service(makerDb,tenantId,makerId);
            var created=await maker.SaveAsync(null,input);Assert.True(created.Success,created.Message);
            id=created.Data!.Id;
            Assert.Equal(id,(await maker.SaveAsync(null,input)).Data!.Id);
            var submitted=await maker.TransitionAsync(id,"submit",
                new ProcurementSupplierDecisionRequest{RowVersion=created.Data.RowVersion});
            Assert.True(submitted.Success,submitted.Message);version=submitted.Data!.RowVersion;
            Assert.Equal(403,(await maker.TransitionAsync(id,"approve",
                new ProcurementSupplierDecisionRequest{RowVersion=version})).StatusCode);
        }
        await using var checkerDb=Open(options,tenantId,checkerId);
        await using var competingDb=Open(options,tenantId,checkerId);
        var approved=await Service(checkerDb,tenantId,checkerId).TransitionAsync(id,"approve",
            new ProcurementSupplierDecisionRequest{RowVersion=version});
        Assert.True(approved.Success,approved.Message);
        var stale=await Service(competingDb,tenantId,checkerId).TransitionAsync(id,"reject",
            new ProcurementSupplierDecisionRequest{RowVersion=version,Reason="Competing"});
        Assert.Equal(409,stale.StatusCode);
        await using var verify=Open(options,tenantId,makerId);
        Assert.Equal("Approved",(await Service(verify,tenantId,makerId).GetAsync(id)).Data!.StateCode);
        Assert.Equal(1,await verify.ProcurementSuppliers.CountAsync(x=>x.Id==id));
    }
}
