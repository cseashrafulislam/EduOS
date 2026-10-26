using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Auth;
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
public sealed class SqlServerStudentContactProfileIntegrationTests
{
    [Fact]
    public async Task Sql_rowversion_replay_and_stale_writer_preserve_one_audited_change()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, userId;
        Guid studentReference;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant
            {
                Code = "CI-CONTACT-" + suffix, Name = "Student Contact CI",
                Email = "contact-" + suffix + "@example.test", State = TenantState.Active
            };
            var username = "contact-admin-" + suffix + "@example.test";
            var manager = new ApplicationUser
            {
                UserName = username, NormalizedUserName = username.ToUpperInvariant(),
                Email = username, NormalizedEmail = username.ToUpperInvariant(),
                FullName = "Contact Admin", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            setup.AddRange(tenant, manager);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; userId = manager.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var student = new Student
                {
                    TenantId = tenantId, FullName = "Contact Student",
                    StudentCode = "CONTACT-" + suffix, StatusCode = "Active",
                    AdmissionDate = new DateOnly(2026, 1, 1)
                };
                setup.Students.Add(student);
                await setup.SaveChangesAsync();
                studentReference = student.PublicId;
            }
        }

        await using var firstDb = TenantDb(options, tenantId);
        await using var secondDb = TenantDb(options, tenantId);
        var first = Service(firstDb, tenantId, userId);
        var second = Service(secondDb, tenantId, userId);
        var before = await first.GetAsync(studentReference);
        Assert.True(before.Success, before.Message);
        Assert.Equal(8, Convert.FromBase64String(before.Data!.RowVersion).Length);
        var request = new UpdateStudentContactProfileRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RowVersion = before.Data.RowVersion,
            Phone = "+8801700000000", Email = "contact@example.test",
            Address = "Updated contact address", PreferredLanguage = "bn-BD",
            Reason = "Verified institutional contact correction"
        };
        var saved = await first.UpdateAsync(studentReference, request);
        Assert.True(saved.Success, saved.Message);
        var replay = await second.UpdateAsync(studentReference, request);
        Assert.True(replay.Success, replay.Message);

        var stale = new UpdateStudentContactProfileRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RowVersion = before.Data.RowVersion,
            Phone = "+8801800000000", Email = "different@example.test",
            Address = "Different address", PreferredLanguage = "bn-BD",
            Reason = "A concurrent stale contact correction"
        };
        var conflict = await second.UpdateAsync(studentReference, stale);
        Assert.Equal(409, conflict.StatusCode);
        await using var verify = TenantDb(options, tenantId);
        Assert.Equal(1, await verify.IdempotencyRecords.CountAsync(x =>
            x.TenantId == tenantId && x.Scope == "StudentContactProfile"));
        Assert.Equal(1, await verify.AuditLogs.CountAsync(x =>
            x.TenantId == tenantId && x.Action == "Student.ContactUpdated"));
        Assert.Equal("+8801700000000", await verify.Students
            .Where(x => x.PublicId == studentReference).Select(x => x.Phone).SingleAsync());
    }

    private static EduOSDbContext TenantDb(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, accessor);
    }

    private static StudentContactProfileService Service(EduOSDbContext db, long tenantId, long userId) =>
        new(db, new TenantAdmin(tenantId, userId), NullLogger<StudentContactProfileService>.Instance);

    private sealed class TenantAdmin(long tenantId, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "Contact SQL Admin";
        public string? Email => "contact-ci@example.test";
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Student Contact SQL CI";
    }
}
