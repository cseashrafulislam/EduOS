using EduOS.Core.DTOs.HR;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Tests.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>Real SQL Server verifies the tenant-unique receipt under independent competing transactions.</summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerHrCampusAssignmentReceiptIntegrationTests
{
    // A framework HttpContextAccessor uses shared AsyncLocal state. Each
    // concurrent test DbContext must own a separate request context for audit.
    private sealed class IsolatedHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private static IHttpContextAccessor RequestForTenant(long tenantId)
    {
        var context = new DefaultHttpContext();
        context.Items["TenantId"] = tenantId;
        return new IsolatedHttpContextAccessor(context);
    }

    [Fact]
    public async Task Same_request_race_persists_once_then_replays_after_reassignment()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, employeeId, campusAId, campusBId;
        Guid employeeReference;

        await using (var seed = new EduOSDbContext(options))
        {
            var tenant = new Tenant
            {
                Code = "HR-RC-" + suffix, Name = "HR Receipt SQL Acceptance",
                Email = "hr-" + suffix + "@example.test", State = TenantState.Active
            };
            seed.Tenants.Add(tenant);
            await seed.SaveChangesAsync();
            tenantId = tenant.Id;
            seed.ChangeTracker.Clear();

            using (seed.BeginSystemTenantScope(tenantId))
            {
                var person = new Person { FullName = "Receipt SQL Employee" };
                var designation = new Designation { TenantId = tenantId, Name = "Staff", Code = "HR-SQL-" + suffix };
                var campusA = new Campus { TenantId = tenantId, Name = "North", Code = "N-" + suffix };
                var campusB = new Campus { TenantId = tenantId, Name = "South", Code = "S-" + suffix };
                seed.AddRange(person, designation, campusA, campusB);
                await seed.SaveChangesAsync();
                campusAId = campusA.Id;
                campusBId = campusB.Id;
                var employee = new Employee
                {
                    TenantId = tenantId, PersonId = person.Id, DesignationId = designation.Id,
                    EmployeeCode = "HR-RC-" + suffix, FullName = person.FullName,
                    JoiningDate = new DateOnly(2026, 1, 1)
                };
                seed.Employees.Add(employee);
                await seed.SaveChangesAsync();
                employeeId = employee.Id;
                employeeReference = employee.PublicId;
            }
        }


        var requestId = Guid.NewGuid();
        AssignEmployeeCampusRequestDto Original() => new()
        {
            ClientRequestId = requestId, EmployeeReference = employeeReference,
            CampusId = campusAId, EffectiveFrom = new DateOnly(2026, 11, 1), IsPrimary = true
        };
        await using var dbA = new EduOSDbContext(options, RequestForTenant(tenantId));
        await using var dbB = new EduOSDbContext(options, RequestForTenant(tenantId));
        var attempts = await Task.WhenAll(
            HrCampusAssignmentWorkflowTests.FullService(dbA, tenantId).AssignCampusAsync(Original()),
            HrCampusAssignmentWorkflowTests.FullService(dbB, tenantId).AssignCampusAsync(Original()));
        Assert.Contains(attempts, x => x.Success);
        Assert.All(attempts, x => Assert.True(x.Success || x.StatusCode == 409, x.Message));

        await using var verify = new EduOSDbContext(options, RequestForTenant(tenantId));
        var service = HrCampusAssignmentWorkflowTests.FullService(verify, tenantId);
        Assert.Equal(1, await verify.EmployeeCampusAssignmentReceipts.CountAsync(x =>
            x.TenantId == tenantId && x.ClientRequestId == requestId));
        Assert.Equal(1, await verify.EmployeeCampusAssignments.CountAsync(x =>
            x.TenantId == tenantId && x.EmployeeId == employeeId));
        var first = await service.AssignCampusAsync(Original());
        Assert.True(first.Success, first.Message);
        Assert.Equal(1, await verify.EmployeeCampusAssignmentReceipts.CountAsync(x => x.TenantId == tenantId));

        var second = await service.AssignCampusAsync(new AssignEmployeeCampusRequestDto
        {
            ClientRequestId = Guid.NewGuid(), EmployeeReference = employeeReference,
            CampusId = campusBId, EffectiveFrom = new DateOnly(2026, 11, 10), IsPrimary = true
        });
        Assert.True(second.Success, second.Message);
        var replay = await service.AssignCampusAsync(Original());
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(first.Data!.Id, replay.Data!.Id);
        Assert.Null(replay.Data.EffectiveTo);
        Assert.Equal(2, await verify.EmployeeCampusAssignmentReceipts.CountAsync(x => x.TenantId == tenantId));

        var changed = Original();
        changed.IsPrimary = false;
        Assert.Equal(409, (await service.AssignCampusAsync(changed)).StatusCode);
        Assert.Equal(2, await verify.EmployeeCampusAssignments.CountAsync(x => x.TenantId == tenantId));
    }
}
