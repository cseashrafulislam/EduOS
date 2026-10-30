using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Hostel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelApplicationWorkflowTests
{
    [Fact]
    public async Task Student_application_waitlist_offer_and_atomic_accept_reuse_canonical_allocation()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.ActivateContext();
        var service = fixture.Service;
        var request = fixture.Request();
        Assert.Equal(101, fixture.Db.CurrentTenantId);
        Assert.Equal(fixture.Enrollment.PublicId, (await fixture.Db.StudentEnrollments.FirstAsync()).PublicId);
        var submitted = await service.SubmitApplicationAsync(request);
        Assert.True(submitted.Success, submitted.Message);
        Assert.Equal(HostelApplicationState.Submitted, submitted.Data!.State);
        var replay = await service.SubmitApplicationAsync(request);
        Assert.True(replay.Success);
        Assert.Equal(submitted.Data.Id, replay.Data!.Id);
        Assert.Single(fixture.Db.HostelApplications);

        fixture.User.AsWarden();
        var queue = await service.GetApplicationQueueAsync(null, null, 1, 25, null);
        Assert.True(queue.Success, queue.Message);
        Assert.Equal(submitted.Data.Id, Assert.Single(queue.Data!.Items).Id);
        fixture.RefreshVersion();
        var version = fixture.Version;
        var waitlist = await service.TransitionApplicationAsync(submitted.Data.Id, "waitlist",
            new HostelApplicationDecisionRequestDto { RowVersion = version });
        Assert.True(waitlist.Success, waitlist.Message);
        Assert.Equal(HostelApplicationState.Waitlisted, waitlist.Data!.State);

        var offer = await service.TransitionApplicationAsync(submitted.Data.Id, "offer",
            new HostelApplicationDecisionRequestDto
            {
                RowVersion = waitlist.Data.RowVersion, OfferedBedId = fixture.Bed.Id, OfferHours = 48
            });
        Assert.True(offer.Success, offer.Message);
        Assert.Equal(HostelApplicationState.Offered, offer.Data!.State);
        Assert.Empty(fixture.Db.StudentHostelAllocations);

        // Viewing and deciding an offer does not authorize the warden to accept it.
        var wardenAcceptance = await service.AcceptApplicationOfferAsync(submitted.Data.Id,
            new HostelApplicationDecisionRequestDto { RowVersion = offer.Data.RowVersion });
        Assert.Equal(403, wardenAcceptance.StatusCode);
        Assert.Empty(fixture.Db.StudentHostelAllocations);

        fixture.User.AsStudent();
        var accepted = await service.AcceptApplicationOfferAsync(submitted.Data.Id,
            new HostelApplicationDecisionRequestDto { RowVersion = offer.Data.RowVersion });
        Assert.True(accepted.Success, accepted.Message);
        Assert.Equal(HostelApplicationState.Accepted, accepted.Data!.State);
        Assert.NotNull(accepted.Data.AllocationId);
        var allocation = Assert.Single(fixture.Db.StudentHostelAllocations);
        Assert.Equal(fixture.Bed.Id, allocation.HostelBedId);
        Assert.Equal(fixture.Enrollment.Id, allocation.StudentEnrollmentId);
        Assert.Equal(4, fixture.Db.HostelApplicationEvents.Count());
        var retry = await service.AcceptApplicationOfferAsync(submitted.Data.Id,
            new HostelApplicationDecisionRequestDto { RowVersion = offer.Data.RowVersion });
        Assert.True(retry.Success);
        Assert.Single(fixture.Db.StudentHostelAllocations);
        Assert.Equal(409, (await service.TransitionApplicationAsync(submitted.Data.Id, "withdraw",
            new HostelApplicationDecisionRequestDto { RowVersion = accepted.Data.RowVersion })).StatusCode);
        var history = await service.GetApplicationEventsAsync(submitted.Data.Id);
        Assert.True(history.Success);
        Assert.Equal(4, history.Data!.Count);
    }

    [Fact]
    public async Task Guardian_pickup_does_not_grant_application_rights_and_ids_remain_tenant_scoped()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.ActivateContext();
        fixture.User.AsGuardian();
        var denied = await fixture.Service.SubmitApplicationAsync(fixture.Request());
        Assert.Equal(403, denied.StatusCode);
        Assert.Empty(fixture.Db.HostelApplications);
        fixture.User.AsStudent();
        var submitted = await fixture.Service.SubmitApplicationAsync(fixture.Request());
        Assert.True(submitted.Success);
        fixture.User.AsGuardian();
        Assert.Equal(403, (await fixture.Service.GetApplicationEventsAsync(submitted.Data!.Id)).StatusCode);
        Assert.Equal(403, (await fixture.Service.AcceptApplicationOfferAsync(submitted.Data.Id,
            new HostelApplicationDecisionRequestDto { RowVersion = "AQ==" })).StatusCode);
        fixture.User.AsWarden();
        Assert.Equal(404, (await fixture.Service.GetApplicationEventsAsync(submitted.Data.Id + 999999)).StatusCode);
        Assert.Equal(400, (await fixture.Service.GetApplicationQueueAsync(null, null, 0, 25, null)).StatusCode);
        Assert.Equal(400, (await fixture.Service.GetApplicationQueueAsync(null, null, 1, 101, null)).StatusCode);
    }

    [Fact]
    public async Task Expired_nonbinding_offer_never_allocates_and_conflicting_request_key_is_rejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.ActivateContext();
        var request = fixture.Request();
        var submitted = await fixture.Service.SubmitApplicationAsync(request);
        Assert.True(submitted.Success, submitted.Message);
        var conflict = await fixture.Service.SubmitApplicationAsync(new SubmitHostelApplicationRequestDto
        {
            ClientRequestId = request.ClientRequestId,
            StudentEnrollmentReference = request.StudentEnrollmentReference,
            HostelReference = request.HostelReference,
            StartDate = request.StartDate,
            EndDate = request.StartDate.AddDays(7)
        });
        Assert.Equal(409, conflict.StatusCode);
        fixture.RefreshVersion();
        fixture.User.AsWarden();
        var offer = await fixture.Service.TransitionApplicationAsync(submitted.Data!.Id, "offer",
            new HostelApplicationDecisionRequestDto
            {
                RowVersion = fixture.Version, OfferedBedId = fixture.Bed.Id, OfferHours = 1
            });
        Assert.True(offer.Success, offer.Message);
        fixture.Db.HostelApplications.Single().OfferExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();
        fixture.User.AsStudent();
        var accept = await fixture.Service.AcceptApplicationOfferAsync(submitted.Data.Id,
            new HostelApplicationDecisionRequestDto { RowVersion = offer.Data!.RowVersion });
        Assert.Equal(409, accept.StatusCode);
        Assert.Empty(fixture.Db.StudentHostelAllocations);
        Assert.Equal(HostelApplicationState.Expired, fixture.Db.HostelApplications.Single().State);
        Assert.Equal(HostelApplicationState.Expired, fixture.Db.HostelApplicationEvents.OrderBy(x => x.Id).Last().ToState);
    }

    [Fact]
    public async Task Verified_guardian_delegate_can_apply_then_revocation_blocks_future_access()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.ActivateContext();
        fixture.User.AsWarden();
        var grantRequest = new GrantHostelApplicationAuthorityRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = fixture.Enrollment.PublicId,
            GuardianReference = fixture.Guardian.PublicId, EvidenceReference = "Campus-verified-2026-001",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        };
        Assert.Equal(403, (await fixture.Service.GrantApplicationAuthorityAsync(grantRequest)).StatusCode);
        fixture.User.AsPrincipal();
        var granted = await fixture.Service.GrantApplicationAuthorityAsync(grantRequest);
        Assert.True(granted.Success, granted.Message);
        Assert.True((await fixture.Service.GrantApplicationAuthorityAsync(grantRequest)).Success);
        fixture.User.AsGuardian();
        var options = await fixture.Service.GetMyApplicationOptionsAsync(1, 25);
        Assert.True(options.Success, options.Message);
        Assert.Single(options.Data!.Items);
        var submitted = await fixture.Service.SubmitApplicationAsync(fixture.Request());
        Assert.True(submitted.Success, submitted.Message);
        var mine = await fixture.Service.GetMyApplicationsAsync(1, 20);
        Assert.Equal(submitted.Data!.Id, Assert.Single(mine.Data!.Items).Id);
        fixture.Db.HostelApplicationAuthorities.Single().RowVersion = [3, 2, 1];
        await fixture.Db.SaveChangesAsync();
        fixture.User.AsPrincipal();
        var revoke = await fixture.Service.RevokeApplicationAuthorityAsync(granted.Data!.Id,
            new RevokeHostelApplicationAuthorityRequestDto
            {
                RowVersion = Convert.ToBase64String([3, 2, 1]), Reason = "Consent withdrawn"
            });
        Assert.True(revoke.Success, revoke.Message);
        fixture.User.AsGuardian();
        Assert.Empty((await fixture.Service.GetMyApplicationsAsync(1, 20)).Data!.Items);
        Assert.Equal(403, (await fixture.Service.GetApplicationEventsAsync(submitted.Data.Id)).StatusCode);
    }

    private sealed class MutableUser : ICurrentUserService
    {
        public long UserId { get; private set; } = 41;
        private string _role = "Student";
        public void AsStudent() { UserId = 41; _role = "Student"; }
        public void AsGuardian() { UserId = 42; _role = "Guardian"; }
        public void AsWarden() { UserId = 43; _role = "HostelWarden"; }
        public void AsPrincipal() { UserId = 43; _role = "Principal"; }
        public bool IsAuthenticated => true;
        public long TenantId => 101;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => [_role];
        public bool IsInRole(string role) => role == _role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required EduOSDbContext Db { get; init; }
        public required HostelService Service { get; init; }
        public required MutableUser User { get; init; }
        public required HttpContextAccessor Accessor { get; init; }
        public required DefaultHttpContext Http { get; init; }
        public void ActivateContext() => Accessor.HttpContext = Http;
        public required StudentEnrollment Enrollment { get; init; }
        public required Hostel Hostel { get; init; }
        public required HostelBed Bed { get; init; }
        public required Guardian Guardian { get; init; }
        public string Version => Convert.ToBase64String(Db.HostelApplications.Single().RowVersion);
        public void RefreshVersion()
        {
            var app = Db.HostelApplications.Single();
            app.RowVersion = [1, 2, 3, 4];
            Db.SaveChanges();
        }
        public SubmitHostelApplicationRequestDto Request() => new()
        {
            ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = Enrollment.PublicId,
            HostelReference = Hostel.PublicId,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            EndDate = DateOnly.FromDateTime(DateTime.Now.AddDays(14))
        };
        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<EduOSDbContext>()
                .UseInMemoryDatabase("hostel-application-" + Guid.NewGuid().ToString("N")).Options;
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "41"), new Claim(ClaimTypes.Role, "Student"),
                    new Claim("TenantId", "101")
                ], "TestAuth"))
            };
            http.Items["TenantId"] = 101L;
            var accessor = new HttpContextAccessor { HttpContext = http };
            var db = new EduOSDbContext(options, accessor);
            var campus = new Campus { TenantId = 101, Name = "Campus", Code = "C" };
            db.Add(campus); await db.SaveChangesAsync();
            db.Add(new UserCampusAccess { TenantId = 101, UserId = 43, CampusId = campus.Id });
            await db.SaveChangesAsync();
            var student = new Student { TenantId = 101, UserId = 41, PersonId = 1,
                FullName = "Student A", StudentCode = "SA", StatusCode = "Active" };
            var hostel = new Hostel { TenantId = 101, CampusId = campus.Id, Code = "H", Name = "Hostel A" };
            var guardian = new Guardian { TenantId = 101, UserId = 42, PersonId = 2,
                FullName = "Verified guardian", IsActive = true };
            db.AddRange(student, hostel, guardian); await db.SaveChangesAsync();
            db.Add(new StudentGuardian { TenantId = 101, StudentId = student.Id, GuardianId = guardian.Id,
                RelationCode = "Parent", IsAuthorizedPickup = true });
            await db.SaveChangesAsync();
            var enrollment = new StudentEnrollment
            {
                TenantId = 101, StudentId = student.Id, CampusId = campus.Id,
                ClientRequestId = Guid.NewGuid(), IsCurrent = true, State = EnrollmentState.Active,
                AcademicYearId = 1, AcademicProgramId = 1, AcademicLevelId = 1,
                AcademicBatchId = 1, AcademicCurriculumId = 1, RollNo = "A"
            };
            var room = new HostelRoom { TenantId = 101, HostelId = hostel.Id, RoomNumber = "101",
                Capacity = 1, RentPerBed = 0m };
            db.AddRange(enrollment, room); await db.SaveChangesAsync();
            var bed = new HostelBed { TenantId = 101, HostelRoomId = room.Id, BedNumber = "A" };
            db.Add(bed); await db.SaveChangesAsync();
            var user = new MutableUser();
            var service = new HostelService(
                new GenericRepository<Hostel>(db), new GenericRepository<HostelRoom>(db),
                new GenericRepository<HostelBed>(db), new GenericRepository<StudentHostelAllocation>(db),
                new GenericRepository<StudentEnrollment>(db), new GenericRepository<Student>(db),
                new GenericRepository<UserCampusAccess>(db), new GenericRepository<Campus>(db),
                db, user, TimeProvider.System, NullLogger<HostelService>.Instance,
                new EduOS.Service.Services.Finance.ReservationFinancePolicy(db),
                new EduOS.Service.Services.Platform.ReservationCommandReceiptStore(db, TimeProvider.System),
                new GenericRepository<HostelApplication>(db), new GenericRepository<HostelApplicationEvent>(db),
                new GenericRepository<HostelApplicationAuthority>(db), new GenericRepository<StudentGuardian>(db),
                new GenericRepository<Guardian>(db));
            return new Fixture { Db = db, Service = service, User = user, Accessor = accessor, Http = http,
                Enrollment = enrollment, Hostel = hostel, Bed = bed, Guardian = guardian };
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
