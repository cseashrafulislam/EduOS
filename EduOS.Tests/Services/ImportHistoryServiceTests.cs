using EduOS.Core.Entities.System;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ImportHistoryServiceTests
{
    [Fact]
    public async Task Only_current_tenant_imports_and_rows_are_visible()
    {
        var options = Options();
        var ownReference = Guid.NewGuid();
        var foreignReference = Guid.NewGuid();
        await SeedAsync(options, 101, ownReference);
        await SeedAsync(options, 202, foreignReference);
        await using var db = Context(options, 101);
        var service = Service(db, new TestUser(101, true));
        var list = await service.GetImportsAsync(null, 1, 25);
        Assert.True(list.Success);
        Assert.Single(list.Data!.Items);
        Assert.Equal(ownReference, list.Data.Items[0].Reference);
        var ownRows = await service.GetImportRowsAsync(ownReference, 1, 25);
        Assert.True(ownRows.Success);
        Assert.Single(ownRows.Data!.Items);
        Assert.Equal("ROW-1", ownRows.Data.Items[0].BusinessKey);
        Assert.Equal(404, (await service.GetImportAsync(foreignReference)).StatusCode);
        Assert.Equal(404, (await service.GetImportRowsAsync(foreignReference, 1, 25)).StatusCode);
    }

    [Fact]
    public async Task Denies_unprivileged_users_and_unbounded_queries()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var user = Service(db, new TestUser(101, false));
        Assert.Equal(403, (await user.GetImportsAsync(null, 1, 25)).StatusCode);
        Assert.Equal(403, (await user.GetImportAsync(Guid.NewGuid())).StatusCode);
        var admin = Service(db, new TestUser(101, true));
        Assert.Equal(400, (await admin.GetImportsAsync(null, 0, 25)).StatusCode);
        Assert.Equal(400, (await admin.GetImportsAsync(null, 1, 101)).StatusCode);
    }


    [Fact]
    public async Task Failed_row_export_is_tenant_scoped_and_spreadsheet_safe()
    {
        var options = Options();
        var ownReference = Guid.NewGuid();
        var foreignReference = Guid.NewGuid();
        await SeedAsync(options, 101, ownReference);
        await SeedAsync(options, 202, foreignReference);
        await using var db = Context(options, 101);
        var importId = await db.ImportLogs.Where(x => x.PublicId == ownReference).Select(x => x.Id).SingleAsync();
        db.ImportLogItems.AddRange(
            new ImportLogItem { TenantId = 101, ImportLogId = importId, RowNumber = 2,
                BusinessKey = "=1+1", IsSuccess = false, ErrorMessage = "+42" },
            new ImportLogItem { TenantId = 101, ImportLogId = importId, RowNumber = 3,
                BusinessKey = " A,\"quoted\"", IsSuccess = false, ErrorMessage = "Invalid value" });
        await db.SaveChangesAsync();
        var service = Service(db, new TestUser(101, true));
        var result = await service.ExportFailedRowsCsvAsync(ownReference);
        Assert.True(result.Success);
        Assert.StartsWith("RowNumber,BusinessKey,ErrorMessage\r\n", result.Data);
        Assert.Contains("\"'=1+1\"", result.Data);
        Assert.Contains("\"'+42\"", result.Data);
        Assert.Contains("\" A,\"\"quoted\"\"\"", result.Data);
        Assert.DoesNotContain("ROW-1", result.Data);
        Assert.Equal(404, (await service.ExportFailedRowsCsvAsync(foreignReference)).StatusCode);
    }

    [Fact]
    public async Task Failed_row_export_rejects_unprivileged_and_invalid_requests()
    {
        var options = Options();
        await using var db = Context(options, 101);
        Assert.Equal(403, (await Service(db, new TestUser(101, false))
            .ExportFailedRowsCsvAsync(Guid.NewGuid())).StatusCode);
        Assert.Equal(400, (await Service(db, new TestUser(101, true))
            .ExportFailedRowsCsvAsync(Guid.Empty)).StatusCode);
    }

    [Fact]
    public async Task Failed_row_export_is_bounded_to_one_thousand_rows()
    {
        var options = Options();
        var reference = Guid.NewGuid();
        await SeedAsync(options, 101, reference);
        await using var db = Context(options, 101);
        var importId = await db.ImportLogs.Where(x => x.PublicId == reference).Select(x => x.Id).SingleAsync();
        db.ImportLogItems.AddRange(Enumerable.Range(2, 1001).Select(i => new ImportLogItem
        {
            TenantId = 101, ImportLogId = importId, RowNumber = i, IsSuccess = false, ErrorMessage = "Rejected"
        }));
        await db.SaveChangesAsync();
        Assert.Equal(413, (await Service(db, new TestUser(101, true))
            .ExportFailedRowsCsvAsync(reference)).StatusCode);
    }

    [Fact]
    public void Failed_row_export_api_preserves_authorization_and_get_semantics()
    {
        var controller = typeof(EduOS.App.Controllers.Api.ImportHistoryController);
        var auth = Assert.Single(controller.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        Assert.Equal("TenantAdmin,Principal", auth.Roles);
        var export = controller.GetMethod("ExportFailures");
        Assert.NotNull(export);
        Assert.NotEmpty(export!.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpGetAttribute), true));
    }

    private static async Task SeedAsync(DbContextOptions<EduOSDbContext> options, long tenantId, Guid reference)
    {
        await using var db = Context(options, tenantId);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Institution " + tenantId, Code = "TN-" + tenantId,
            Email = tenantId + "@example.test", State = EduOS.Core.Enums.Domain.TenantState.Active
        });
        await db.SaveChangesAsync();
        var record = new ImportLog
        {
            TenantId = tenantId, PublicId = reference, ImportType = "Student",
            TotalRows = 1, SuccessRows = 1, FailedRows = 0
        };
        db.ImportLogs.Add(record);
        await db.SaveChangesAsync();
        db.ImportLogItems.Add(new ImportLogItem
        {
            TenantId = tenantId, ImportLogId = record.Id, RowNumber = 1,
            BusinessKey = "ROW-1", IsSuccess = true
        });
        await db.SaveChangesAsync();
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("import-history-" + Guid.NewGuid().ToString("N")).Options;

    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var context = new DefaultHttpContext();
        context.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = context });
    }

    private static ImportHistoryService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<ImportLog>(db),
        new GenericRepository<ImportLogItem>(db),
        user, NullLogger<ImportHistoryService>.Instance);

    private sealed class TestUser(long tenant, bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => 99;
        public bool IsTenantAdmin => admin;
        public bool IsSuperAdmin => false;
        public string? FullName => null;
        public string? Email => null;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : [];
        public bool IsInRole(string role) => admin && role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => "Test";
    }
}
