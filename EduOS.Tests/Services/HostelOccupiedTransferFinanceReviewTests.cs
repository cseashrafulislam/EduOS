using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelOccupiedTransferFinanceReviewTests
{
    private static DateOnly Today => DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);

    [Fact]
    public async Task Only_tenant_admin_may_review_occupied_transfer()
    {
        await using var db = Database();
        var result = await new FinanceAdjustmentService(db, new User(false), TimeProvider.System)
            .ReviewHostelOccupiedTransferAsync(new() { ReservationId = 1, DestinationBedId = 2, EffectiveDate = Today });
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Posted_invoice_exposure_blocks_approval_without_changing_reservation_or_journal()
    {
        await using var db = Database();
        var (stay, destination) = await Seed(db);
        db.StudentInvoices.Add(new StudentInvoice {
            TenantId = 41, StudentEnrollmentId = stay.StudentEnrollmentId,
            InvoiceNumber = "TRANSFER-POST", InvoiceDate = Today.AddDays(-1),
            DueDate = Today.AddDays(7), JournalId = 99
        });
        await db.SaveChangesAsync();
        var result = await Review(db, stay.Id, destination, Today.AddDays(1));
        result.Success.Should().BeTrue();
        result.Data!.Status.Should().Be("BLOCKED_POSTED_ACTIVITY");
        result.Data.RequiresApprovedFinanceAdjustment.Should().BeTrue();
        result.Data.FinancialMutationApproved.Should().BeFalse();
        (await db.StudentHostelAllocations.SingleAsync()).HostelBedId.Should().Be(stay.HostelBedId);
        db.Journals.Should().BeEmpty();
    }

    [Fact]
    public async Task Charged_deposit_without_invoice_still_blocks_transfer()
    {
        await using var db = Database();
        var (stay, destination) = await Seed(db);
        stay.SecurityDeposit = 500m;
        await db.SaveChangesAsync();
        var result = await Review(db, stay.Id, destination, Today.AddDays(1));
        result.Success.Should().BeTrue();
        result.Data!.Status.Should().Be("BLOCKED_CHARGED_TERMS");
        result.Data.SecurityDeposit.Should().Be(500m);
        result.Data.FinancialMutationApproved.Should().BeFalse();
    }

    [Fact]
    public async Task No_charge_review_never_authorizes_an_occupied_transfer()
    {
        await using var db = Database();
        var (stay, destination) = await Seed(db);
        var result = await Review(db, stay.Id, destination, Today.AddDays(1));
        result.Success.Should().BeTrue();
        result.Data!.Status.Should().Be("NO_KNOWN_EXPOSURE_NOT_APPROVED");
        result.Data.FinancialMutationApproved.Should().BeFalse();
        db.StudentHostelAllocations.Should().ContainSingle();
    }

    [Fact]
    public async Task Cross_campus_destination_and_same_bed_are_rejected()
    {
        await using var db = Database();
        var (stay, destination) = await Seed(db, destinationCampus: 8);
        (await Review(db, stay.Id, destination, Today.AddDays(1))).StatusCode.Should().Be(409);
        (await Review(db, stay.Id, stay.HostelBedId, Today.AddDays(1))).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Past_date_and_unstarted_stay_are_rejected()
    {
        await using var db = Database();
        var (stay, destination) = await Seed(db);
        (await Review(db, stay.Id, destination, Today.AddDays(-1))).StatusCode.Should().Be(400);
        stay.StartDate = Today.AddDays(2);
        await db.SaveChangesAsync();
        (await Review(db, stay.Id, destination, Today.AddDays(3))).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Overlapping_destination_occupancy_is_rejected()
    {
        await using var db = Database();
        var (stay, destination) = await Seed(db);
        db.StudentHostelAllocations.Add(new StudentHostelAllocation {
            TenantId = 41, StudentId = 11, StudentEnrollmentId = 222,
            HostelBedId = destination, StartDate = Today, EndDate = Today.AddDays(5)
        });
        await db.SaveChangesAsync();
        (await Review(db, stay.Id, destination, Today.AddDays(1))).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Foreign_tenant_reservation_is_not_disclosed()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("hostel-transfer-finance-tenant-" + Guid.NewGuid().ToString("N")).Options;
        long foreignId, destination;
        await using (var foreign = Database(42, options))
        {
            var seeded = await Seed(foreign, tenant: 42);
            foreignId = seeded.Stay.Id;
            destination = seeded.Destination;
        }
        await using var own = Database(41, options);
        (await Review(own, foreignId, destination, Today.AddDays(1))).StatusCode.Should().Be(404);
    }

    private static async Task<EduOS.Core.Common.ApiResponse<HostelOccupiedTransferFinanceReviewDto>> Review(
        EduOSDbContext db, long reservation, long bed, DateOnly date) =>
        await new FinanceAdjustmentService(db, new User(true), TimeProvider.System)
            .ReviewHostelOccupiedTransferAsync(new() {
                ReservationId = reservation, DestinationBedId = bed, EffectiveDate = date
            });

    private static async Task<(StudentHostelAllocation Stay, long Destination)> Seed(
        EduOSDbContext db, long tenant = 41, long destinationCampus = 7)
    {
        var enrollment = new StudentEnrollment {
            TenantId = tenant, StudentId = 10, CampusId = 7, RollNo = "FIN-TRANSFER"
        };
        var sourceHostel = new Hostel { TenantId = tenant, CampusId = 7, Name = "Source", Code = "SRC" };
        var targetHostel = new Hostel { TenantId = tenant, CampusId = destinationCampus, Name = "Target", Code = "DST" };
        db.StudentEnrollments.Add(enrollment);
        db.Hostels.AddRange(sourceHostel, targetHostel);
        await db.SaveChangesAsync();
        var sourceRoom = new HostelRoom {
            TenantId = tenant, HostelId = sourceHostel.Id, RoomNumber = "101", Capacity = 2
        };
        var targetRoom = new HostelRoom {
            TenantId = tenant, HostelId = targetHostel.Id, RoomNumber = "102", Capacity = 2
        };
        db.HostelRooms.AddRange(sourceRoom, targetRoom);
        await db.SaveChangesAsync();
        var sourceBed = new HostelBed { TenantId = tenant, HostelRoomId = sourceRoom.Id, BedNumber = "A" };
        var targetBed = new HostelBed { TenantId = tenant, HostelRoomId = targetRoom.Id, BedNumber = "B" };
        db.HostelBeds.AddRange(sourceBed, targetBed);
        await db.SaveChangesAsync();
        var stay = new StudentHostelAllocation {
            TenantId = tenant, StudentId = 10, StudentEnrollmentId = enrollment.Id,
            HostelBedId = sourceBed.Id, StartDate = Today.AddDays(-5),
            EndDate = Today.AddDays(15), State = HostelAllocationState.Active
        };
        db.StudentHostelAllocations.Add(stay);
        await db.SaveChangesAsync();
        return (stay, targetBed.Id);
    }

    private static EduOSDbContext Database(long tenant = 41,
        DbContextOptions<EduOSDbContext>? options = null)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        options ??= new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("hostel-transfer-finance-" + Guid.NewGuid().ToString("N")).Options;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class User(bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Staff"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
