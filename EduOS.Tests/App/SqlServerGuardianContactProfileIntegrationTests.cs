using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Students;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerGuardianContactProfileIntegrationTests
{
    [Fact]
    public async Task Sql_rowversion_stale_writer_replay_and_cross_tenant_access_are_safe()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, userId;
        Guid studentReference, guardianReference;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant
            {
                Code = "CI-GUARDIAN-" + suffix, Name = "Guardian Contact CI",
                Email = "guardian-" + suffix + "@example.test", State = TenantState.Active
            };
            var username = "guardian-admin-" + suffix + "@example.test";
            var manager = new ApplicationUser
            {
                UserName = username, NormalizedUserName = username.ToUpperInvariant(),
                Email = username, NormalizedEmail = username.ToUpperInvariant(),
                FullName = "Guardian Admin", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            var studentPerson = new Person { FullName = "Guardian CI Student" };
            var guardianPerson = new Person { FullName = "Guardian CI Parent" };
            setup.AddRange(tenant, manager, studentPerson, guardianPerson);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; userId = manager.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var student = new Student
                {
                    TenantId = tenantId, PersonId = studentPerson.Id,
                    StudentCode = "GCI-" + suffix, FullName = "Guardian CI Student",
                    StatusCode = "Active", AdmissionDate = new DateOnly(2026, 1, 1)
                };
                var guardian = new Guardian
                {
                    TenantId = tenantId, PersonId = guardianPerson.Id,
                    FullName = "Guardian CI Parent", IsActive = true
                };
                setup.AddRange(student, guardian);
                await setup.SaveChangesAsync();
                studentReference = student.PublicId; guardianReference = guardian.PublicId;
                setup.StudentGuardians.Add(new StudentGuardian
                {
                    TenantId = tenantId, StudentId = student.Id, GuardianId = guardian.Id,
                    RelationCode = "Mother", IsPrimary = true
                });
                await setup.SaveChangesAsync();
            }
        }

        await using var firstDb = TenantDb(options, tenantId);
        await using var secondDb = TenantDb(options, tenantId);
        var first = Service(firstDb, tenantId, userId);
        var second = Service(secondDb, tenantId, userId);
        var before = await first.GetAsync(studentReference, guardianReference);
        Assert.True(before.Success, before.Message);
        Assert.Equal(8, Convert.FromBase64String(before.Data!.RowVersion).Length);

        var request = new UpdateGuardianContactProfileRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RowVersion = before.Data.RowVersion,
            Phone = "+8801700000000", Email = "guardian@example.test",
            Occupation = "Teacher", Address = "Guardian contact address",
            Reason = "Verified guardian contact correction"
        };
        var saved = await first.UpdateAsync(studentReference, guardianReference, request);
        Assert.True(saved.Success, saved.Message);
        var replay = await second.UpdateAsync(studentReference, guardianReference, request);
        Assert.True(replay.Success, replay.Message);

        var stale = new UpdateGuardianContactProfileRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RowVersion = before.Data.RowVersion,
            Phone = "+8801800000000", Email = "stale@example.test",
            Occupation = "Engineer", Address = "Stale address",
            Reason = "Stale concurrent guardian correction"
        };
        var conflict = await second.UpdateAsync(studentReference, guardianReference, stale);
        Assert.Equal(409, conflict.StatusCode);
        await using var verify = TenantDb(options, tenantId);
        Assert.Equal(1, await verify.IdempotencyRecords.CountAsync(x =>
            x.TenantId == tenantId && x.Scope == "GuardianContactProfile"));
        Assert.Equal(1, await verify.AuditLogs.CountAsync(x =>
            x.TenantId == tenantId && x.Action == "Guardian.ContactUpdated"));
        Assert.Equal("+8801700000000", await verify.Guardians
            .Where(x => x.PublicId == guardianReference).Select(x => x.Phone).SingleAsync());

        await using var foreignDb = TenantDb(options, tenantId + 1000000);
        var foreign = Service(foreignDb, tenantId + 1000000, userId);
        Assert.Equal(404, (await foreign.GetAsync(studentReference, guardianReference)).StatusCode);
        Assert.Equal(404, (await foreign.UpdateAsync(studentReference, guardianReference,
            new UpdateGuardianContactProfileRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = before.Data.RowVersion,
                Phone = "+8801900000000", Reason = "Unauthorized cross tenant correction"
            })).StatusCode);
    }

    private static EduOSDbContext TenantDb(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, accessor);
    }

    private static GuardianContactProfileService Service(EduOSDbContext db, long tenantId, long userId) =>
        new(db, new TenantAdmin(tenantId, userId), NullLogger<GuardianContactProfileService>.Instance);

    private sealed class TenantAdmin(long tenantId, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "Guardian SQL Admin";
        public string? Email => "guardian-ci@example.test";
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Guardian Contact SQL CI";
    }
}
