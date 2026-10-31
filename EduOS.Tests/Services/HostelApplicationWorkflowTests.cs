using EduOS.Core.DTOs.Hostel;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Finance;
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
using EduOS.Service.Services.Finance;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    [Fact]
    public async Task Occupied_no_charge_transfer_splits_canonical_history_and_replays_exactly_once()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.ActivateContext(); fixture.User.AsWarden();
        var (source, destination, request) = await PrepareOccupiedTransferAsync(fixture);
        var options = await fixture.Service.GetOccupiedTransferBedOptionsAsync(source.Id, 1, 25);
        Assert.True(options.Success, options.Message);
        Assert.Equal(destination.Id, Assert.Single(options.Data!.Items).Id);
        var moved = await fixture.Service.TransferOccupiedNoChargeAsync(source.Id, request);
        Assert.True(moved.Success, moved.Message);
        Assert.Equal(source.Id, moved.Data!.SourceAllocationId);
        var predecessor = await fixture.Db.StudentHostelAllocations.SingleAsync(x => x.Id == source.Id);
        var successor = await fixture.Db.StudentHostelAllocations.SingleAsync(x => x.PreviousAllocationId == source.Id);
        Assert.Equal(destination.Id, successor.HostelBedId);
        Assert.Equal(fixture.Bed.Id, predecessor.HostelBedId);
        Assert.Equal(HostelAllocationState.Closed, predecessor.State);
        Assert.Equal(request.EffectiveFrom.AddDays(-1), predecessor.EndDate);
        Assert.Equal(request.EffectiveFrom, successor.StartDate);
        Assert.Equal(0m, successor.MonthlyRent);
        Assert.Equal(0m, successor.SecurityDeposit);
        Assert.Equal(moved.Data.SuccessorAllocationId, successor.Id);
        Assert.Equal(source.StudentEnrollmentId, successor.StudentEnrollmentId);
        Assert.Equal(source.EndDate, request.EffectiveFrom.AddDays(-1));
        var retry = await fixture.Service.TransferOccupiedNoChargeAsync(source.Id, request);
        Assert.True(retry.Success, retry.Message);
        Assert.Equal(moved.Data.SuccessorAllocationId, retry.Data!.SuccessorAllocationId);
        Assert.Equal(2, await fixture.Db.StudentHostelAllocations.CountAsync());
        Assert.Single(fixture.Db.IdempotencyRecords.Where(x => x.Scope == "hostel.occupied-transfer"));
        Assert.Single(fixture.Db.AuditLogs.Where(x => x.Action == "HostelOccupiedTransfer"));
        request.Reason = "Changed transfer reason";
        Assert.Equal(409, (await fixture.Service.TransferOccupiedNoChargeAsync(source.Id, request)).StatusCode);
    }

    [Fact]
    public async Task Occupied_transfer_blocks_stale_version_charged_terms_and_capacity_collision()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.ActivateContext(); fixture.User.AsWarden();
        var (source, destination, request) = await PrepareOccupiedTransferAsync(fixture);
        request.SourceRowVersion = Convert.ToBase64String([9, 9, 9]);
        Assert.Equal(409, (await fixture.Service.TransferOccupiedNoChargeAsync(source.Id, request)).StatusCode);
        request.SourceRowVersion = Convert.ToBase64String(source.RowVersion);
        request.DestinationBedRowVersion = Convert.ToBase64String([9, 9, 9]);
        Assert.Equal(409, (await fixture.Service.TransferOccupiedNoChargeAsync(source.Id, request)).StatusCode);
        request.DestinationBedRowVersion = Convert.ToBase64String(destination.RowVersion);
        source.MonthlyRent = 1200m;
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(409, (await fixture.Service.TransferOccupiedNoChargeAsync(source.Id, request)).StatusCode);
        source.MonthlyRent = 0m;
        await fixture.Db.SaveChangesAsync();
        var other = new StudentHostelAllocation
        {
            TenantId = 101, StudentId = 999, StudentEnrollmentId = 999,
            HostelBedId = destination.Id, ClientRequestId = Guid.NewGuid(),
            StartDate = request.EffectiveFrom.AddDays(2), EndDate = request.EffectiveFrom.AddDays(3),
            State = HostelAllocationState.Active
        };
        fixture.Db.Add(other); await fixture.Db.SaveChangesAsync();
        Assert.Equal(409, (await fixture.Service.TransferOccupiedNoChargeAsync(source.Id, request)).StatusCode);
        Assert.Null(source.PreviousAllocationId);
        Assert.Equal(HostelAllocationState.Active, source.State);
        Assert.DoesNotContain(fixture.Db.StudentHostelAllocations, x => x.PreviousAllocationId == source.Id);
    }

    [Fact]
    public void Occupied_transfer_period_guards_today_and_nonzero_predecessor()
    {
        var today = new DateOnly(2026, 10, 31);
        Assert.True(HostelService.CanTransferOccupiedToday(today.AddDays(-1), null, today, today));
        Assert.True(HostelService.CanTransferOccupiedToday(today.AddDays(-10), today, today, today));
        Assert.False(HostelService.CanTransferOccupiedToday(today, null, today, today));
        Assert.False(HostelService.CanTransferOccupiedToday(today.AddDays(-1), null, today.AddDays(1), today));
        Assert.False(HostelService.CanTransferOccupiedToday(today.AddDays(-3), today.AddDays(-1), today, today));
    }

    [Fact]
    public async Task Occupied_transfer_rejects_unauthorized_actor_and_future_or_same_day_effective_dates()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.ActivateContext();
        var (source, _, request) = await PrepareOccupiedTransferAsync(fixture);
        fixture.User.AsStudent();
        Assert.Equal(403, (await fixture.Service.TransferOccupiedNoChargeAsync(source.Id, request)).StatusCode);
        fixture.User.AsWarden();
        request.EffectiveFrom = request.EffectiveFrom.AddDays(1);
        Assert.Equal(409, (await fixture.Service.TransferOccupiedNoChargeAsync(source.Id, request)).StatusCode);
        request.EffectiveFrom = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        request.Reason = " ";
        Assert.Equal(400, (await fixture.Service.TransferOccupiedNoChargeAsync(source.Id, request)).StatusCode);
        Assert.Single(fixture.Db.StudentHostelAllocations);
        Assert.Empty(fixture.Db.IdempotencyRecords.Where(x => x.Scope == "hostel.occupied-transfer"));
    }

    private static async Task<(StudentHostelAllocation Source, HostelBed Destination, TransferOccupiedHostelRequestDto Request)>
        PrepareOccupiedTransferAsync(Fixture fixture)
    {
        var room = await fixture.Db.HostelRooms.SingleAsync();
        room.Capacity = 2;
        var destination = new HostelBed
        {
            TenantId = 101, HostelRoomId = room.Id, BedNumber = "B", RowVersion = [5, 6, 7, 8]
        };
        fixture.Db.Add(destination);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var source = new StudentHostelAllocation
        {
            TenantId = 101, ClientRequestId = Guid.NewGuid(), StudentId = fixture.Enrollment.StudentId,
            StudentEnrollmentId = fixture.Enrollment.Id, HostelBedId = fixture.Bed.Id,
            StartDate = today.AddDays(-3), EndDate = today.AddDays(6),
            MonthlyRent = 0m, SecurityDeposit = 0m, State = HostelAllocationState.Active,
            RowVersion = [1, 2, 3, 4]
        };
        fixture.Db.Add(source);
        await fixture.Db.SaveChangesAsync();
        source.RowVersion = [1, 2, 3, 4]; destination.RowVersion = [5, 6, 7, 8];
        return (source, destination, new TransferOccupiedHostelRequestDto
        {
            ClientRequestId = Guid.NewGuid(), DestinationBedId = destination.Id,
            EffectiveFrom = today, SourceRowVersion = Convert.ToBase64String(source.RowVersion),
            DestinationBedRowVersion = Convert.ToBase64String(destination.RowVersion),
            Reason = "Move resident for safety"
        });
    }

    [Fact]
    public async Task Charged_occupied_transfer_requires_exact_approved_clearance_and_consumes_it_once()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.ActivateContext(); fixture.User.AsWarden();
        var (source, destination, move) = await PrepareOccupiedTransferAsync(fixture);
        source.MonthlyRent = 1200m;
        source.SecurityDeposit = 200m;
        await fixture.Db.SaveChangesAsync();
        var admin = new FinanceAdmin();
        var finance = new FinanceAdjustmentService(fixture.Db, admin, TimeProvider.System);
        var request = new RequestHostelClearanceDto
        {
            ClientRequestId = Guid.NewGuid(), SourceAllocationId = source.Id,
            Operation = HostelClearanceOperation.Transfer,
            DestinationBedId = destination.Id, DestinationBedRowVersion = move.DestinationBedRowVersion,
            SourceRowVersion = move.SourceRowVersion, EffectiveDate = move.EffectiveFrom,
            Reason = "Move to a more accessible lower bunk"
        };
        var requested = await finance.RequestHostelClearanceAsync(request);
        Assert.True(requested.Success, requested.Message);
        Assert.Equal(0m, requested.Data!.SuggestedRentCredit);
        Assert.Equal(0m, requested.Data.SuggestedDepositRefund);
        Assert.Equal(409, (await fixture.Service.TransferOccupiedNoChargeAsync(source.Id, move)).StatusCode);
        var row = await fixture.Db.HostelFinanceClearances.SingleAsync();
        row.RowVersion = [3, 4, 5, 6];
        await fixture.Db.SaveChangesAsync();
        var review = new ReviewHostelClearanceDto
        {
            RowVersion = Convert.ToBase64String(row.RowVersion), Approve = true,
            NoAdjustmentRequired = true,
            ReconciliationNote = "Finance verified no adjustment was required; deposit carries forward."
        };
        Assert.Equal(403, (await finance.ReviewHostelClearanceAsync(row.Id, review)).StatusCode);
        admin.UserId = 97;
        var approved = await finance.ReviewHostelClearanceAsync(row.Id, review);
        Assert.True(approved.Success, approved.Message);
        move.FinanceClearanceId = row.Id;
        var done = await fixture.Service.TransferOccupiedNoChargeAsync(source.Id, move);
        Assert.True(done.Success, done.Message);
        Assert.Equal(HostelClearanceState.Consumed, row.State);
        Assert.Equal(done.Data!.SuccessorAllocationId, row.SuccessorAllocationId);
        Assert.Equal(1200m, (await fixture.Db.StudentHostelAllocations.SingleAsync(x => x.PreviousAllocationId == source.Id)).MonthlyRent);
        var retry = await fixture.Service.TransferOccupiedNoChargeAsync(source.Id, move);
        Assert.True(retry.Success, retry.Message);
        Assert.Equal(done.Data.SuccessorAllocationId, retry.Data!.SuccessorAllocationId);
        Assert.Equal(2, await fixture.Db.StudentHostelAllocations.CountAsync());
    }

    [Fact]
    public async Task Campus_policy_supports_manual_override_and_requires_reason()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.ActivateContext(); fixture.User.AsWarden();
        var (source, _, move) = await PrepareOccupiedTransferAsync(fixture);
        source.MonthlyRent = 3100m; source.SecurityDeposit = 300m;
        await fixture.Db.SaveChangesAsync();
        var admin = new FinanceAdmin();
        var finance = new FinanceAdjustmentService(fixture.Db, admin, TimeProvider.System);
        var setting = await finance.SaveHostelBillingPolicyAsync(new HostelBillingPolicyDto
        { CampusId = fixture.Hostel.CampusId, CalculationMode = HostelBillingCalculationMode.DailyProrated,
            AllowManualOverride = true });
        Assert.True(setting.Success, setting.Message);
        var changed = new RequestHostelClearanceDto
        {
            ClientRequestId = Guid.NewGuid(), SourceAllocationId = source.Id,
            Operation = HostelClearanceOperation.Checkout, EffectiveDate = move.EffectiveFrom,
            SourceRowVersion = move.SourceRowVersion, Reason = "Early checkout requested",
            ManualRentCredit = 120m, ManualDepositRefund = 100m
        };
        Assert.Equal(400, (await finance.RequestHostelClearanceAsync(changed)).StatusCode);
        changed.OverrideReason = "Finance explicitly authorized a partial verified deposit liability.";
        var created = await finance.RequestHostelClearanceAsync(changed);
        Assert.True(created.Success, created.Message);
        Assert.Equal(120m, created.Data!.ApprovedRentCredit);
        Assert.Equal(100m, created.Data.ApprovedDepositRefund);
        Assert.Equal(300m, created.Data.DepositAtRequest);
        var repeat = await finance.RequestHostelClearanceAsync(changed);
        Assert.True(repeat.Success);
        Assert.Equal(created.Data.Id, repeat.Data!.Id);
        changed.ManualRentCredit = 130m;
        Assert.Equal(409, (await finance.RequestHostelClearanceAsync(changed)).StatusCode);
    }

    private sealed class FinanceAdmin : ICurrentUserService
    {
        public long UserId { get; set; } = 96;
        public bool IsAuthenticated => true;
        public long TenantId => 101;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
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
                .UseInMemoryDatabase("hostel-application-" + Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
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
                new GenericRepository<Guardian>(db), new GenericRepository<EduOS.Core.Entities.System.AuditLog>(db),
                new GenericRepository<EduOS.Core.Entities.Finance.HostelFinanceClearance>(db));
            return new Fixture { Db = db, Service = service, User = user, Accessor = accessor, Http = http,
                Enrollment = enrollment, Hostel = hostel, Bed = bed, Guardian = guardian };
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
