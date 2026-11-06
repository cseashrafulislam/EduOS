using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ImportHistoryAuthorizationTests
{
    [Fact]
    public async Task History_and_failed_row_export_require_live_membership_role_and_selected_tenant()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("import-history-access-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        CentralPrivilegedTestIdentity.Seed(db, 101, 10);
        var reference = Guid.NewGuid();
        db.ImportLogs.Add(new ImportLog
        {
            Id = 88, TenantId = 101, PublicId = reference, ImportType = "STUDENTS",
            TotalRows = 1, FailedRows = 1
        });
        db.ImportLogItems.Add(new ImportLogItem
        {
            Id = 89, TenantId = 101, ImportLogId = 88, RowNumber = 1,
            BusinessKey = "test", ErrorMessage = "invalid", IsSuccess = false
        });
        await db.SaveChangesAsync();
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.UserId).Returns(10);
        user.SetupGet(x => x.TenantId).Returns(101);
        user.SetupGet(x => x.IsTenantAdmin).Returns(true);
        var service = new ImportHistoryService(new GenericRepository<ImportLog>(db),
            new GenericRepository<ImportLogItem>(db), user.Object, db,
            NullLogger<ImportHistoryService>.Instance);

        Assert.Equal(200, (await service.GetImportsAsync(null, 1, 20)).StatusCode);
        Assert.Equal(200, (await service.GetImportAsync(reference)).StatusCode);
        Assert.Equal(200, (await service.GetImportRowsAsync(reference, 1, 20)).StatusCode);
        Assert.Equal(200, (await service.ExportFailedRowsCsvAsync(reference)).StatusCode);

        http.Items["TenantId"] = 202L;
        Assert.Equal(403, (await service.GetImportsAsync(null, 1, 20)).StatusCode);
        Assert.Equal(403, (await service.ExportFailedRowsCsvAsync(reference)).StatusCode);
        http.Items["TenantId"] = 101L;

        var role = await db.Roles.IgnoreQueryFilters().SingleAsync(x => x.Id == 9001);
        role.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(403, (await service.GetImportAsync(reference)).StatusCode);
        Assert.Equal(403, (await service.GetImportRowsAsync(reference, 1, 20)).StatusCode);

        role.IsActive = true;
        db.TenantMemberships.Single(x => x.UserId == 10).Status = MembershipStatus.Suspended;
        await db.SaveChangesAsync();
        Assert.Equal(403, (await service.GetImportsAsync(null, 1, 20)).StatusCode);
        Assert.Equal(403, (await service.ExportFailedRowsCsvAsync(reference)).StatusCode);
    }
}
