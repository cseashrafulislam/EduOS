using System.Security.Claims;
using EduOS.Core.DTOs.Research;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Research;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerResearchAccreditationTests
{
    [Fact]
    public async Task Grants_and_accreditation_enforce_independent_decisions_replay_and_real_sql_versions()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, campusId;
        var identities = new long[5]; // Researcher, grant checker, framework maker, publisher, reviewer.
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-RAC-" + suffix, Name = "Research CI",
                Email = "research-" + suffix + "@example.test", State = TenantState.Active };
            var users = Enumerable.Range(0, 5).Select(i =>
            {
                var mail = "rac-" + i + "-" + suffix + "@example.test";
                return new ApplicationUser
                {
                    UserName = mail, NormalizedUserName = mail.ToUpperInvariant(), Email = mail,
                    NormalizedEmail = mail.ToUpperInvariant(), FullName = "Research CI " + i,
                    EmailConfirmed = true, IsActive = true,
                    SecurityStamp = Guid.NewGuid().ToString(),
                    ConcurrencyStamp = Guid.NewGuid().ToString()
                };
            }).ToArray();
            setup.Tenants.Add(tenant);
            setup.Users.AddRange(users);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id;
            for (var i = 0; i < users.Length; i++) identities[i] = users[i].Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Code = "RAC", Name = "Research Campus" };
                setup.Campuses.Add(campus);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                var codes = new[] { "Teacher", "ResearchCoordinator", "AccreditationCoordinator",
                    "AccreditationCoordinator", "AccreditationReviewer" };
                var roles = new Dictionary<string, ApplicationRole>();
                foreach (var code in codes.Distinct())
                    roles.Add(code, new ApplicationRole
                    {
                        TenantId = tenantId, Name = code, NormalizedName = code.ToUpperInvariant(),
                        IsActive = true
                    });
                setup.Roles.AddRange(roles.Values);
                foreach (var id in identities)
                {
                    setup.TenantMemberships.Add(new TenantMembership
                    { TenantId = tenantId, UserId = id, Status = MembershipStatus.Active });
                    setup.UserCampusAccesses.Add(new UserCampusAccess
                    { TenantId = tenantId, CampusId = campusId, UserId = id, IsActive = true });
                }
                await setup.SaveChangesAsync();
                for (var i = 0; i < identities.Length; i++)
                    setup.UserRoles.Add(new IdentityUserRole<long>
                    { UserId = identities[i], RoleId = roles[codes[i]].Id });
                await setup.SaveChangesAsync();
            }
        }

        var proposalRef = Guid.NewGuid();
        await using (var ownerDb = Db(options, tenantId, identities[0]))
        {
            var proposalCreate = Guid.NewGuid();
            await ownerDb.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.ResearchProposals
                (TenantId, PublicId, CampusId, CreateRequestId, OwnerUserId, Title,
                 Abstract, Protocol, ContentSha256, CurrentRevision, StateCode, CreatedAtUtc, UpdatedAtUtc)
                VALUES ({tenantId}, {proposalRef}, {campusId}, {proposalCreate}, {identities[0]},
                        {"Accepted independent ethics protocol"},
                        {"Research methodology approved by independent ethics reviewers."},
                        {"Approved protocol with valid consent and independent review."},
                        {new string('A', 64)}, {1}, {"EthicsApproved"},
                        {DateTime.UtcNow}, {DateTime.UtcNow})
                """);
        }

        long schemeId;
        await using (var grantChecker = Db(options, tenantId, identities[1]))
        {
            var scheme = await Grant(grantChecker, tenantId, identities[1]).CreateSchemeAsync(new()
            {
                ClientRequestId = Guid.NewGuid(), Code = "LOCAL_" + suffix.ToUpperInvariant(),
                Name = "Research funding " + suffix, CurrencyCode = "BDT", MaxAward = 100000m
            });
            Assert.True(scheme.Success, scheme.Message);
            schemeId = scheme.Data!.Id;
        }
        ResearchGrantDto grant;
        var grantCreateRequest = Guid.NewGuid();
        await using (var ownerDb = Db(options, tenantId, identities[0]))
        {
            var service = Grant(ownerDb, tenantId, identities[0]);
            var request = new ResearchGrantCreateDto
            {
                ClientRequestId = grantCreateRequest, ProposalReference = proposalRef,
                SchemeId = schemeId, Title = "Funded ethics-approved research", RequestedAmount = 50000m
            };
            var created = await service.CreateAsync(request);
            Assert.True(created.Success, created.Message);
            grant = created.Data!;
            var replay = await service.CreateAsync(request);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(grant.Reference, replay.Data!.Reference);
            var cannotAward = await service.CommandAsync(grant.Reference, "award", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = grant.RowVersion, Amount = 40000m,
                Reason = "An independent award decision." });
            Assert.False(cannotAward.Success);
            var submitted = await service.CommandAsync(grant.Reference, "submit", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = grant.RowVersion });
            Assert.True(submitted.Success, submitted.Message);
            grant = submitted.Data!;
        }
        await using (var checkerDb = Db(options, tenantId, identities[1]))
        {
            var service = Grant(checkerDb, tenantId, identities[1]);
            var awarded = await service.CommandAsync(grant.Reference, "award", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = grant.RowVersion, Amount = 40000m,
                Reason = "Independent committee approved this award." });
            Assert.True(awarded.Success, awarded.Message);
            grant = awarded.Data!;
            Assert.Equal("Active", grant.StateCode);
            Assert.Equal(1, grant.BudgetVersion);
        }
        await using (var ownerDb = Db(options, tenantId, identities[0]))
        {
            var service = Grant(ownerDb, tenantId, identities[0]);
            var milestone = await service.CommandAsync(grant.Reference, "milestone-add", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = grant.RowVersion,
                MilestoneTitle = "Publish final outcome", DueDate = DateTime.UtcNow.Date.AddDays(45) });
            Assert.True(milestone.Success, milestone.Message);
            grant = milestone.Data!;
            var list = await service.MilestonesAsync(grant.Reference);
            Assert.Single(list.Data!);
            var complete = await service.CommandAsync(grant.Reference, "milestone-complete", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = grant.RowVersion,
                MilestoneId = list.Data![0].Id, Outcome = "Published and verified final project outcome." });
            Assert.True(complete.Success, complete.Message);
            grant = complete.Data!;
            var closure = await service.CommandAsync(grant.Reference, "request-close", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = grant.RowVersion,
                Reason = "All approved deliverables have been completed." });
            Assert.True(closure.Success, closure.Message);
            grant = closure.Data!;
        }
        await using (var checkerDb = Db(options, tenantId, identities[1]))
        {
            var closed = await Grant(checkerDb, tenantId, identities[1]).CommandAsync(
                grant.Reference, "close", new()
                { ClientRequestId = Guid.NewGuid(), RowVersion = grant.RowVersion,
                    Reason = "Independently verified final milestones." });
            Assert.True(closed.Success, closed.Message);
            Assert.Equal("Closed", closed.Data!.StateCode);
        }

        AccreditationFrameworkDto framework;
        await using (var makerDb = Db(options, tenantId, identities[2]))
        {
            var service = Accreditation(makerDb, tenantId, identities[2]);
            var created = await service.CreateFrameworkAsync(new()
            { ClientRequestId = Guid.NewGuid(), Code = "QA_" + suffix.ToUpperInvariant(),
                Name = "Quality standard version one", VersionNo = 1 });
            Assert.True(created.Success, created.Message);
            framework = created.Data!;
            var standard = await service.AddStandardAsync(new()
            {
                ClientRequestId = Guid.NewGuid(), FrameworkId = framework.Id,
                Code = "STD_1", Title = "Research quality assessment",
                Rubric = "Independently validated research deliverables.",
                RequiredEvidence = "Approved evidence and compliance report.", ValidityDays = 365
            });
            Assert.True(standard.Success, standard.Message);
            var ownPublish = await service.PublishFrameworkAsync(framework.Id, new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = framework.RowVersion });
            Assert.False(ownPublish.Success);
        }
        await using (var publisherDb = Db(options, tenantId, identities[3]))
        {
            var published = await Accreditation(publisherDb, tenantId, identities[3])
                .PublishFrameworkAsync(framework.Id, new()
                { ClientRequestId = Guid.NewGuid(), RowVersion = framework.RowVersion });
            Assert.True(published.Success, published.Message);
            Assert.Equal("Published", published.Data!.StateCode);
        }
        AccreditationAssessmentDto assessment;
        await using (var ownerDb = Db(options, tenantId, identities[0]))
        {
            var service = Accreditation(ownerDb, tenantId, identities[0]);
            var standards = await service.StandardsAsync(framework.Id);
            Assert.Single(standards.Data!);
            var created = await service.CreateAsync(new()
            {
                ClientRequestId = Guid.NewGuid(), CampusId = campusId,
                StandardId = standards.Data![0].Id, PeriodStart = DateTime.UtcNow.Date.AddDays(-30),
                PeriodEnd = DateTime.UtcNow.Date,
                EvidenceSummary = "Verified research and teaching outcomes with independent records."
            });
            Assert.True(created.Success, created.Message);
            assessment = created.Data!;
            var submitted = await service.CommandAsync(assessment.Reference, "submit", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = assessment.RowVersion });
            Assert.True(submitted.Success, submitted.Message);
            assessment = submitted.Data!;
            Assert.Equal(1, assessment.CurrentRevision);
        }
        await using (var coordinatorDb = Db(options, tenantId, identities[2]))
        {
            var service = Accreditation(coordinatorDb, tenantId, identities[2]);
            var assigned = await service.CommandAsync(assessment.Reference, "assign", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = assessment.RowVersion,
                ReviewerUserId = identities[4] });
            Assert.True(assigned.Success, assigned.Message);
            assessment = assigned.Data!;
            Assert.Null(assessment.EvidenceSummary); // Coordinator cannot read private evidence.
        }
        await using (var reviewerDb = Db(options, tenantId, identities[4]))
        {
            var service = Accreditation(reviewerDb, tenantId, identities[4]);
            var detail = await service.DetailAsync(assessment.Reference);
            Assert.NotNull(detail.Data!.EvidenceSummary);
            var approved = await service.CommandAsync(assessment.Reference, "approve", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = assessment.RowVersion,
                Reason = "The independent evidence meets the quality rubric.",
                DeclaresNoConflict = true });
            Assert.True(approved.Success, approved.Message);
            Assert.Equal("Approved", approved.Data!.StateCode);
        }
        await using (var reportDb = Db(options, tenantId, identities[2]))
        {
            var service = Accreditation(reportDb, tenantId, identities[2]);
            var report = await service.ReportAsync(campusId,
                DateTime.UtcNow.Date.AddDays(-60), DateTime.UtcNow.Date.AddDays(1));
            Assert.True(report.Success, report.Message);
            Assert.Single(report.Data!);
            Assert.Equal(1, report.Data![0].Approved);
        }
    }

    private static EduOSDbContext Db(DbContextOptions<EduOSDbContext> options, long tenant, long actor)
    {
        var http = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.ToString())], "Test")) };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static ResearchGrantService Grant(EduOSDbContext db, long tenant, long actor) =>
        new(db, new ResearchWorkflowAccess(db, new Principal(tenant, actor)), TimeProvider.System,
            NullLogger<ResearchGrantService>.Instance);
    private static AccreditationService Accreditation(EduOSDbContext db, long tenant, long actor) =>
        new(db, new ResearchWorkflowAccess(db, new Principal(tenant, actor)), TimeProvider.System,
            NullLogger<AccreditationService>.Instance);

    private sealed class Principal(long tenant, long actor) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => actor;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => [];
        public bool IsInRole(string role) => false;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
