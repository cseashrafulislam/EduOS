using EduOS.App.Security;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Runs against migrated, disposable SQL Server 2022 in the SQL migration workflow.
/// Does not depend on provider-specific InMemory behavior or require production secrets.
/// </summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerAuthTenantIntegrationTests
{
    [Fact]
    public async Task Tenant_role_scope_membership_rowversion_and_session_revocation_are_sql_safe()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantAId, tenantBId, userId, membershipId;
        await using (var db = new EduOSDbContext(options))
        {
            var tenantA = new Tenant { Name = "CI Auth A", Code = "CI-A-" + suffix,
                Email = "ci-a-" + suffix + "@example.test", State = TenantState.Active };
            var tenantB = new Tenant { Name = "CI Auth B", Code = "CI-B-" + suffix,
                Email = "ci-b-" + suffix + "@example.test", State = TenantState.Active };
            db.Tenants.AddRange(tenantA, tenantB);
            await db.SaveChangesAsync();
            tenantAId = tenantA.Id;
            tenantBId = tenantB.Id;
            var username = "auth-ci-" + suffix + "@example.test";
            var user = new ApplicationUser
            {
                UserName = username, NormalizedUserName = username.ToUpperInvariant(),
                Email = username, NormalizedEmail = username.ToUpperInvariant(),
                FullName = "SQL Auth Integration", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
            var roleA = new ApplicationRole { TenantId = tenantAId, Name = "Teacher",
                NormalizedName = "TEACHER", IsActive = true };
            var roleB = new ApplicationRole { TenantId = tenantBId, Name = "Accountant",
                NormalizedName = "ACCOUNTANT", IsActive = true };
            using (db.BeginSystemTenantScope(tenantAId))
            {
                db.Roles.Add(roleA);
                await db.SaveChangesAsync();
                db.TenantMemberships.Add(new TenantMembership
                {
                    TenantId = tenantAId, UserId = userId, IsOwner = false,
                    Status = MembershipStatus.Active
                });
                await db.SaveChangesAsync();
            }
            // Trusted system provisioning must never keep tenant A entities
            // tracked while entering tenant B's explicit system scope.
            db.ChangeTracker.Clear();
            using (db.BeginSystemTenantScope(tenantBId))
            {
                db.Roles.Add(roleB);
                await db.SaveChangesAsync();
                db.TenantMemberships.Add(new TenantMembership
                {
                    TenantId = tenantBId, UserId = userId, IsOwner = false,
                    Status = MembershipStatus.Active
                });
                await db.SaveChangesAsync();
            }
            db.UserRoles.AddRange(
                new IdentityUserRole<long> { UserId = userId, RoleId = roleA.Id },
                new IdentityUserRole<long> { UserId = userId, RoleId = roleB.Id });
            await db.SaveChangesAsync();
            membershipId = await db.TenantMemberships.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantAId && x.UserId == userId).Select(x => x.Id).SingleAsync();
        }

        await using (var db = new EduOSDbContext(options))
        {
            var rolesA = await TenantRoleResolver.GetTenantRolesAsync(db, userId, tenantAId, false, 2);
            var rolesB = await TenantRoleResolver.GetTenantRolesAsync(db, userId, tenantBId, false, 2);
            Assert.Contains("Teacher", rolesA);
            Assert.DoesNotContain("Accountant", rolesA);
            Assert.Contains("Accountant", rolesB);
            Assert.DoesNotContain("Teacher", rolesB);
            var sessionId = await AuthSessionRegistry.CreateAsync(db, userId, tenantAId, "127.0.0.1", "CI SQL", true);
            Assert.True(await AuthSessionRegistry.IsValidAsync(db, userId, sessionId, tenantAId));
            Assert.False(await AuthSessionRegistry.IsValidAsync(db, userId, sessionId, tenantBId));
            Assert.True(await AuthSessionRegistry.RevokeAsync(db, userId, sessionId));
            Assert.False(await AuthSessionRegistry.IsValidAsync(db, userId, sessionId, tenantAId));
        }

        await using var first = new EduOSDbContext(options);
        await using var second = new EduOSDbContext(options);
        var member1 = await first.TenantMemberships.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == membershipId);
        var member2 = await second.TenantMemberships.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == membershipId);
        Assert.NotEmpty(member1.RowVersion);
        using (first.BeginSystemTenantScope(tenantAId))
        {
            member1.Status = MembershipStatus.Suspended;
            await first.SaveChangesAsync();
        }
        using (second.BeginSystemTenantScope(tenantAId))
        {
            member2.Status = MembershipStatus.Left;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        }
    }
    [Fact]
    public async Task Campus_archive_enforces_real_sql_assignments_and_last_campus_invariant()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, userId, campusId;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant
            {
                Code = "CI-CAMP-" + suffix, Name = "Campus Integration",
                Email = "campus-" + suffix + "@example.test", State = TenantState.Active
            };
            var username = "campus-admin-" + suffix + "@example.test";
            var user = new ApplicationUser
            {
                UserName = username, NormalizedUserName = username.ToUpperInvariant(),
                Email = username, NormalizedEmail = username.ToUpperInvariant(),
                FullName = "Campus Administrator", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            setup.Tenants.Add(tenant);
            setup.Users.Add(user);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; userId = user.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var main = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN", IsHeadOffice = true };
                var secondary = new Campus { TenantId = tenantId, Name = "Secondary", Code = "SECOND" };
                setup.Campuses.AddRange(main, secondary);
                await setup.SaveChangesAsync();
                campusId = secondary.Id;
                setup.UserCampusAccesses.Add(new UserCampusAccess
                {
                    TenantId = tenantId, UserId = userId, CampusId = campusId, IsActive = true
                });
                await setup.SaveChangesAsync();
            }
        }

        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        await using var db = new EduOSDbContext(options, accessor);
        var service = new InstitutionFoundationService(
            new GenericRepository<Tenant>(db), new GenericRepository<Campus>(db),
            new GenericRepository<TenantSetting>(db), new GenericRepository<TenantMembership>(db),
            new GenericRepository<UserCampusAccess>(db),
            new GenericRepository<AcademicYear>(db), new GenericRepository<AcademicTerm>(db),
            new GenericRepository<AcademicBatch>(db), new GenericRepository<AdmissionIntakeForm>(db),
            new GenericRepository<TenantSubscription>(db), new GenericRepository<SubscriptionPlan>(db),
            db, new CampusAdminTestUser(tenantId), TimeProvider.System,
            NullLogger<InstitutionFoundationService>.Instance);
        var version = await db.Campuses.AsNoTracking().Where(x => x.Id == campusId)
            .Select(x => x.RowVersion).SingleAsync();
        Assert.NotEmpty(version);
        var assigned = await service.ArchiveCampusAsync(campusId, Convert.ToBase64String(version));
        Assert.Equal(409, assigned.StatusCode);
        Assert.Contains("assigned", assigned.Message ?? "", StringComparison.OrdinalIgnoreCase);

        var grant = await db.UserCampusAccesses.SingleAsync(x => x.CampusId == campusId && x.UserId == userId);
        grant.IsActive = false;
        await db.SaveChangesAsync();
        var archived = await service.ArchiveCampusAsync(campusId, Convert.ToBase64String(version));
        Assert.True(archived.Success, archived.Message);
        Assert.True(await db.Campuses.IgnoreQueryFilters().AnyAsync(x => x.Id == campusId && x.IsDeleted));

        var headOffice = await db.Campuses.AsNoTracking().SingleAsync(x =>
            x.TenantId == tenantId && x.IsHeadOffice && !x.IsDeleted);
        var last = await service.ArchiveCampusAsync(headOffice.Id, Convert.ToBase64String(headOffice.RowVersion));
        Assert.Equal(409, last.StatusCode);
        Assert.Contains("at least one", last.Message ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CampusAdminTestUser(long tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenantId;
        public long UserId => 999;
        public string? FullName => "Campus administrator";
        public string? Email => "campus-ci@example.test";
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL Server Integration";
    }

}
