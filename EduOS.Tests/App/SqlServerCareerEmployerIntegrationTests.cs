using EduOS.Core.DTOs.Alumni;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Alumni;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerCareerEmployerIntegrationTests
{
    [Fact]
    public async Task Employer_verification_is_tenant_campus_owned_independent_and_idempotent()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, otherTenantId, campusId, makerId, checkerId;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-CE-" + suffix, Name = "Career SQL CI",
                Email = "career-" + suffix + "@example.test", State = TenantState.Active };
            var other = new Tenant { Code = "CI-CX-" + suffix, Name = "Other career tenant",
                Email = "other-career-" + suffix + "@example.test", State = TenantState.Active };
            var maker = NewUser("career-maker-" + suffix);
            var checker = NewUser("career-checker-" + suffix);
            setup.AddRange(tenant, other, maker, checker);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; otherTenantId = other.Id;
            makerId = maker.Id; checkerId = checker.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Code = "MAIN", Name = "Main Campus" };
                setup.Add(campus);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                setup.UserCampusAccesses.AddRange(
                    new UserCampusAccess { TenantId = tenantId, CampusId = campusId, UserId = makerId, IsActive = true },
                    new UserCampusAccess { TenantId = tenantId, CampusId = campusId, UserId = checkerId, IsActive = true });
                await setup.SaveChangesAsync();
            }
        }
        await using var db = TenantDb(options, tenantId);
        var makerService = Service(db, tenantId, makerId, true);
        var checkerService = Service(db, tenantId, checkerId, true);
        var draftRequest = new CareerEmployerDraftDto
        {
            ClientRequestId = Guid.NewGuid(), CampusId = campusId,
            Name = "Fictional Engineering Ltd", Website = "https://example.test"
        };
        var draft = await makerService.CreateEmployerAsync(draftRequest);
        Assert.True(draft.Success, draft.Message);
        Assert.Equal(1, draft.Data!.Status);
        var replay = await makerService.CreateEmployerAsync(draftRequest);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(draft.Data.Reference, replay.Data!.Reference);
        var duplicate = await makerService.CreateEmployerAsync(new CareerEmployerDraftDto
        {
            ClientRequestId = Guid.NewGuid(), CampusId = campusId, Name = draftRequest.Name
        });
        Assert.False(duplicate.Success);
        Assert.Equal(409, duplicate.StatusCode);
        var invalidUrl = await makerService.CreateEmployerAsync(new CareerEmployerDraftDto
        {
            ClientRequestId = Guid.NewGuid(), CampusId = campusId,
            Name = "Unsafe website", Website = "http://example.test"
        });
        Assert.False(invalidUrl.Success);

        var decision = new CareerEmployerVerifyDto
        {
            ClientRequestId = Guid.NewGuid(), RowVersion = draft.Data.RowVersion
        };
        var self = await makerService.VerifyEmployerAsync(draft.Data.Reference, decision);
        Assert.False(self.Success);
        Assert.Equal(403, self.StatusCode);
        var approved = await checkerService.VerifyEmployerAsync(draft.Data.Reference, decision);
        Assert.True(approved.Success, approved.Message);
        Assert.Equal(2, approved.Data!.Status);
        var decisionReplay = await checkerService.VerifyEmployerAsync(draft.Data.Reference, decision);
        Assert.True(decisionReplay.Success, decisionReplay.Message);
        var stale = await checkerService.VerifyEmployerAsync(draft.Data.Reference,
            new CareerEmployerVerifyDto { ClientRequestId = Guid.NewGuid(), RowVersion = decision.RowVersion });
        Assert.False(stale.Success);
        Assert.Equal(409, stale.StatusCode);

        var verifiedPage = await makerService.EmployersAsync(new CareerEmployerQueryDto { Status = 2 });
        Assert.True(verifiedPage.Success, verifiedPage.Message);
        Assert.Contains(verifiedPage.Data!.Items, x => x.Reference == draft.Data.Reference);
        var studentRole = Service(db, tenantId, makerId, false);
        Assert.Equal(403, (await studentRole.EmployersAsync(new CareerEmployerQueryDto())).StatusCode);
        await using var otherDb = TenantDb(options, otherTenantId);
        var otherService = Service(otherDb, otherTenantId, checkerId, true);
        var crossTenant = await otherService.VerifyEmployerAsync(draft.Data.Reference, decision);
        Assert.False(crossTenant.Success);
        Assert.Equal(404, crossTenant.StatusCode);
        var otherPage = await otherService.EmployersAsync(new CareerEmployerQueryDto());
        Assert.True(otherPage.Success, otherPage.Message);
        Assert.Empty(otherPage.Data!.Items);
    }

    private static ApplicationUser NewUser(string name)
    {
        var email = name + "@example.test";
        return new ApplicationUser
        {
            UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(),
            FullName = name, EmailConfirmed = true, IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
        };
    }
    private static EduOSDbContext TenantDb(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, accessor);
    }
    private static AlumniService Service(EduOSDbContext db, long tenant, long actor, bool admin) =>
        new(db, new Actor(tenant, actor, admin), NullLogger<AlumniService>.Instance,
            new FakeInvitationTransport());

    private sealed class FakeInvitationTransport : IAlumniInvitationTransport
    {
        public Task<AlumniGatewaySendResult> SendAsync(CommunicationGateway gateway,
            string destination, string subject, string text, CancellationToken ct) =>
            Task.FromResult(new AlumniGatewaySendResult(false, "Not used by employer verification."));
    }

    private sealed class Actor(long tenant, long actor, bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => actor;
        public long TenantId => tenant;
        public string? FullName => "Career SQL CI";
        public string? Email => "career-ci@example.test";
        public bool IsTenantAdmin => admin;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Student"];
        public bool IsInRole(string role) => admin ? role == "TenantAdmin" : role == "Student";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Career SQL CI";
    }
}
