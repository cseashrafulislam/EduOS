using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Students;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerCounselingSafeguardingIntegrationTests
{
    [Fact]
    public async Task Independent_campus_policy_hold_redaction_and_guardian_consent_are_enforced_in_sql()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var protector = new EphemeralDataProtectionProvider();
        long tenantId, campusId, adminId, reviewerId, officerId, counselorId, guardianId, teacherId;
        Guid closedRef, activeRef;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Name = "Safeguarding SQL " + suffix, Code = "SAFE-" + suffix,
                Email = "safe-" + suffix + "@example.test", State = TenantState.Active };
            var other = new Tenant { Name = "Other safeguarding tenant", Code = "SAFO-" + suffix,
                Email = "safe-other-" + suffix + "@example.test", State = TenantState.Active };
            var actors = Enumerable.Range(0, 6).Select(i =>
            {
                var mail = $"safe-{i}-{suffix}@example.test";
                return new ApplicationUser { UserName = mail, NormalizedUserName = mail.ToUpperInvariant(),
                    Email = mail, NormalizedEmail = mail.ToUpperInvariant(), IsActive = true,
                    SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString() };
            }).ToArray();
            var person = new Person { FullName = "Confidential Learner" };
            var adult = new Person { FullName = "Guardian QA" };
            setup.AddRange(tenant, other, person, adult); setup.Users.AddRange(actors);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id;
            (adminId, reviewerId, officerId, counselorId, guardianId, teacherId) =
                (actors[0].Id, actors[1].Id, actors[2].Id, actors[3].Id, actors[4].Id, actors[5].Id);
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Name = "Protected campus",
                    Code = "S-" + suffix };
                var student = new Student { TenantId = tenantId, PersonId = person.Id,
                    FullName = "Confidential Learner", StudentCode = "ST-" + suffix,
                    AdmissionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90)), StatusCode = "Active" };
                var guardian = new Guardian { TenantId = tenantId, PersonId = adult.Id, UserId = guardianId,
                    FullName = "Guardian QA", IsActive = true };
                var roles = new[] { "TenantAdmin", "SafeguardingOfficer", "Counselor", "Guardian", "Teacher" }
                    .Select(name => new ApplicationRole { TenantId = tenantId, Name = name,
                        NormalizedName = name.ToUpperInvariant(), IsActive = true }).ToArray();
                setup.AddRange(campus, student, guardian); setup.Roles.AddRange(roles);
                await setup.SaveChangesAsync(); campusId = campus.Id;
                setup.StudentGuardians.Add(new StudentGuardian { TenantId = tenantId,
                    StudentId = student.Id, GuardianId = guardian.Id, RelationCode = "Parent", IsPrimary = true });
                var actorRoles = new[] { 0, 0, 1, 2, 3, 4 };
                for (var i = 0; i < actors.Length; i++)
                {
                    setup.TenantMemberships.Add(new TenantMembership { TenantId = tenantId,
                        UserId = actors[i].Id, Status = MembershipStatus.Active });
                    setup.UserRoles.Add(new IdentityUserRole<long> {
                        UserId = actors[i].Id, RoleId = roles[actorRoles[i]].Id });
                    if (i is 2 or 3)
                        setup.UserCampusAccesses.Add(new UserCampusAccess { TenantId = tenantId,
                            UserId = actors[i].Id, CampusId = campusId, IsActive = true });
                }
                var encrypt = protector.CreateProtector("EduOS.StudentSupport.PrivateCase.v1", tenantId.ToString());
                var old = new CounselingCase { TenantId = tenantId, StudentId = student.Id,
                    CampusId = campusId, ClientRequestId = Guid.NewGuid(), State = "Closed",
                    OpenedByUserId = guardianId, OpenedAtUtc = DateTime.UtcNow.AddDays(-100),
                    ClosedByUserId = counselorId, ClosedAtUtc = DateTime.UtcNow.AddDays(-70),
                    AssignedCounselorUserId = counselorId,
                    ProtectedSummary = encrypt.Protect("PRIVATE confidential learner support narrative"),
                    GuardianContactConsented = true };
                var active = new CounselingCase { TenantId = tenantId, StudentId = student.Id,
                    CampusId = campusId, ClientRequestId = Guid.NewGuid(), State = "Assigned",
                    OpenedByUserId = guardianId, AssignedCounselorUserId = counselorId,
                    OpenedAtUtc = DateTime.UtcNow.AddDays(-2),
                    ProtectedSummary = encrypt.Protect("PRIVATE current learning support narrative"),
                    GuardianContactConsented = true, GuardianConsentAtUtc = DateTime.UtcNow.AddDays(-2) };
                setup.CounselingCases.AddRange(old, active);
                await setup.SaveChangesAsync(); closedRef = old.PublicId; activeRef = active.PublicId;
                setup.CounselingCaseEvents.Add(new CounselingCaseEvent { TenantId = tenantId,
                    CounselingCaseId = old.Id, ClientRequestId = Guid.NewGuid(), Action = "Note",
                    ActorUserId = counselorId, ProtectedNote = encrypt.Protect("PRIVATE counselor session notes") });
                await setup.SaveChangesAsync();
            }
        }

        await using var db = new EduOSDbContext(options, Accessor(tenantId));
        var maker = new CounselingPrivacyService(db, new Actor(tenantId, adminId, "TenantAdmin"), protector);
        var checker = new CounselingPrivacyService(db, new Actor(tenantId, reviewerId, "TenantAdmin"), protector);
        var worker = new CounselingPrivacyService(db, new Actor(tenantId, counselorId, "Counselor"), protector);
        var outsider = new CounselingPrivacyService(db, new Actor(tenantId, teacherId, "Teacher"), protector);
        Assert.Equal(403, (await outsider.PolicyAsync(campusId)).StatusCode);
        Assert.Equal(403, (await outsider.PendingRedactionsAsync()).StatusCode);
        var initial = new ProposeCounselingPolicyRequest { RequestId = Guid.NewGuid(), CampusId = campusId,
            RetentionDays = 30, LegalHold = true,
            EmergencyGuidanceEn = "Call trained safeguarding staff immediately",
            EmergencyGuidanceBn = "জরুরি বিপদে তাৎক্ষণিক সুরক্ষা কর্মকর্তার সঙ্গে যোগাযোগ করুন",
            Reason = "Institutionally verified child protection practice" };
        var pending = await maker.ProposeAsync(initial);
        Assert.True(pending.Success, pending.Message);
        var sameAdmin = await maker.ReviewAsync(pending.Data!.Reference,
            new ReviewCounselingPolicyRequest { RequestId = Guid.NewGuid(), RowVersion = pending.Data.RowVersion,
                Approve = true, Reason = "Independent institutional policy acceptance" });
        Assert.Equal(409, sameAdmin.StatusCode);
        var approved = await checker.ReviewAsync(pending.Data.Reference,
            new ReviewCounselingPolicyRequest { RequestId = Guid.NewGuid(), RowVersion = pending.Data.RowVersion,
                Approve = true, Reason = "Independent institutional policy acceptance" });
        Assert.True(approved.Success, approved.Message);
        Assert.True((await maker.PolicyAsync(campusId)).Data!.LegalHold);
        var caseBefore = await db.CounselingCases.AsNoTracking().SingleAsync(x => x.PublicId == closedRef);
        Assert.Equal(409, (await worker.RequestRedactionAsync(closedRef,
            new CounselingPrivacyCommandRequest { RequestId = Guid.NewGuid(),
                RowVersion = Convert.ToBase64String(caseBefore.RowVersion),
                Reason = "Expired record under institutional retention review" })).StatusCode);

        var version = (await maker.PolicyAsync(campusId)).Data!.RowVersion;
        var change = new ProposeCounselingPolicyRequest { RequestId = Guid.NewGuid(),
            CampusId = campusId, ExpectedPolicyRowVersion = version, RetentionDays = 30, LegalHold = false,
            EmergencyGuidanceEn = initial.EmergencyGuidanceEn,
            EmergencyGuidanceBn = initial.EmergencyGuidanceBn,
            Reason = "Independent legal review released original hold" };
        var proposed = await maker.ProposeAsync(change);
        Assert.True(proposed.Success, proposed.Message);
        Assert.True((await checker.ReviewAsync(proposed.Data!.Reference,
            new ReviewCounselingPolicyRequest { RequestId = Guid.NewGuid(),
                RowVersion = proposed.Data.RowVersion, Approve = true,
                Reason = "Legal desk confirmed release of all active holds" })).Success);

        var privacy = new CounselingWorkflowService(db, new Actor(tenantId, counselorId, "Counselor"), protector);
        var confidential = await privacy.OpenPrivateAsync(closedRef, Guid.NewGuid());
        Assert.True(confidential.Success, confidential.Message);
        Assert.Contains("PRIVATE", confidential.Data!.PrivateSummary);
        var teacher = new CounselingWorkflowService(db, new Actor(tenantId, teacherId, "Teacher"), protector);
        Assert.Equal(403, (await teacher.OpenPrivateAsync(closedRef, Guid.NewGuid())).StatusCode);
        var otherTenant = new CounselingPrivacyService(db,
            new Actor(tenantId + 1, adminId, "TenantAdmin"), protector);
        Assert.Equal(403, (await otherTenant.PolicyAsync(campusId)).StatusCode);

        db.ChangeTracker.Clear();
        caseBefore = await db.CounselingCases.AsNoTracking().SingleAsync(x => x.PublicId == closedRef);
        var request = await worker.RequestRedactionAsync(closedRef,
            new CounselingPrivacyCommandRequest { RequestId = Guid.NewGuid(),
                RowVersion = Convert.ToBase64String(caseBefore.RowVersion),
                Reason = "Retention has expired and legal hold cleared" });
        Assert.True(request.Success, request.Message);
        Assert.Single((await checker.PendingRedactionsAsync()).Data!);
        var attempt = new CounselingPrivacyCommandRequest { RequestId = Guid.NewGuid(),
            RowVersion = request.Data!.RowVersion, Approve = true,
            Reason = "Checked retention duration and release of legal hold" };
        Assert.Equal(403, (await worker.ReviewRedactionAsync(closedRef, attempt)).StatusCode);
        var erased = await checker.ReviewRedactionAsync(closedRef, attempt);
        Assert.True(erased.Success, erased.Message);
        Assert.True(erased.Data!.IsRedacted);
        Assert.True((await checker.ReviewRedactionAsync(closedRef, attempt)).Success);
        db.ChangeTracker.Clear();
        var stored = await db.CounselingCases.AsNoTracking().SingleAsync(x => x.PublicId == closedRef);
        Assert.Equal("", stored.ProtectedSummary);
        var notes = await db.CounselingCaseEvents.AsNoTracking().Where(x => x.CounselingCaseId == stored.Id)
            .ToArrayAsync();
        Assert.All(notes, x => Assert.Equal("", x.ProtectedNote));
        Assert.Contains(notes, x => x.Action == "Note");
        Assert.Contains(notes, x => x.Action == "RedactApproved");
        Assert.Equal(410, (await privacy.OpenPrivateAsync(closedRef, Guid.NewGuid())).StatusCode);

        var activeCase = await db.CounselingCases.AsNoTracking().SingleAsync(x => x.PublicId == activeRef);
        var deliveryId = Guid.NewGuid();
        var sent = await privacy.NotifyGuardianAsync(activeRef, new NotifyCounselingGuardianRequest
        { RequestId = deliveryId, RowVersion = Convert.ToBase64String(activeCase.RowVersion) });
        Assert.True(sent.Success, sent.Message);
        Assert.True((await privacy.NotifyGuardianAsync(activeRef, new NotifyCounselingGuardianRequest
        { RequestId = deliveryId, RowVersion = Convert.ToBase64String(activeCase.RowVersion) })).Success);
        var generic = await db.Notifications.AsNoTracking().Where(x => x.SourceType == "CounselingCaseEvent")
            .ToArrayAsync();
        var notification = Assert.Single(generic);
        Assert.Equal(guardianId, notification.UserId);
        Assert.DoesNotContain("PRIVATE", notification.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(activeRef.ToString(), notification.Body, StringComparison.OrdinalIgnoreCase);
        var guardianFlow = new CounselingWorkflowService(db, new Actor(tenantId, guardianId, "Guardian"), protector);
        var withdrawn = await guardianFlow.UpdateGuardianConsentAsync(activeRef,
            new UpdateGuardianContactConsentRequest { RequestId = Guid.NewGuid(),
                RowVersion = sent.Data!.RowVersion, Consent = false });
        Assert.True(withdrawn.Success, withdrawn.Message);
        Assert.Equal(409, (await privacy.NotifyGuardianAsync(activeRef,
            new NotifyCounselingGuardianRequest { RequestId = Guid.NewGuid(),
                RowVersion = withdrawn.Data!.RowVersion })).StatusCode);
        Assert.Equal(1, await db.Notifications.AsNoTracking().CountAsync(x =>
            x.SourceType == "CounselingCaseEvent"));
    }

    private static IHttpContextAccessor Accessor(long tenantId)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return accessor;
    }
    private sealed class Actor(long tenantId, long actorId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => actorId;
        public long TenantId => tenantId;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string name) => name == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
