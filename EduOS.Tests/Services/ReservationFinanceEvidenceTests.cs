using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ReservationFinanceEvidenceTests
{
    [Fact]
    public async Task Only_admin_can_read_evidence_and_invalid_sources_are_rejected()
    {
        await using var db = Database();
        var request = new ReservationFinanceEvidenceQueryDto { Kind = "hostel", ReservationId = 1 };
        (await Service(db, false).GetReservationFinanceEvidenceAsync(request)).StatusCode.Should().Be(403);
        (await Service(db).GetReservationFinanceEvidenceAsync(new()
            { Kind = "hostel", ReservationId = 1, Source = "all" })).StatusCode.Should().Be(400);
        (await Service(db).GetReservationFinanceEvidenceAsync(new()
            { Kind = "hostel", ReservationId = 1, PageSize = 101 })).StatusCode.Should().Be(400);
        (await Service(db).GetReservationFinanceEvidenceAsync(request)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Invoice_evidence_is_enrollment_scoped_paged_and_never_approves_mutation()
    {
        await using var db = Database();
        var stay = await SeedHostel(db);
        db.StudentInvoices.AddRange(
            new StudentInvoice { TenantId = 41, StudentEnrollmentId = stay.StudentEnrollmentId,
                InvoiceNumber = "OLD", InvoiceDate = new DateOnly(2026, 9, 1),
                DueDate = new DateOnly(2026, 9, 30), State = InvoiceState.Cancelled },
            new StudentInvoice { TenantId = 41, StudentEnrollmentId = stay.StudentEnrollmentId,
                InvoiceNumber = "ISSUED", InvoiceDate = new DateOnly(2026, 10, 1),
                DueDate = new DateOnly(2026, 10, 31), State = InvoiceState.Issued,
                TotalAmount = 900m, DueAmount = 900m },
            new StudentInvoice { TenantId = 41, StudentEnrollmentId = stay.StudentEnrollmentId,
                InvoiceNumber = "POSTED", InvoiceDate = new DateOnly(2026, 11, 1),
                DueDate = new DateOnly(2026, 11, 30), State = InvoiceState.Paid,
                TotalAmount = 1200m, PaidAmount = 1200m, JournalId = 90 },
            new StudentInvoice { TenantId = 41, StudentEnrollmentId = 999,
                InvoiceNumber = "UNRELATED", InvoiceDate = new DateOnly(2026, 12, 1),
                DueDate = new DateOnly(2026, 12, 31) });
        await db.SaveChangesAsync();
        var first = await Service(db).GetReservationFinanceEvidenceAsync(new()
            { Kind = "hostel", ReservationId = stay.Id, Source = "invoice", PageSize = 1 });
        first.Success.Should().BeTrue();
        first.Data!.Evidence.TotalCount.Should().Be(3);
        first.Data.Evidence.Items.Should().ContainSingle().Which.Reference.Should().Be("POSTED");
        first.Data.Evidence.Items[0].HasPostedActivity.Should().BeTrue();
        first.Data.FinancialMutationApproved.Should().BeFalse();
        var second = await Service(db).GetReservationFinanceEvidenceAsync(new()
            { Kind = "hostel", ReservationId = stay.Id, Source = "invoice", Page = 2, PageSize = 1 });
        second.Data!.Evidence.Items.Single().IsUnpostedIssuedInvoice.Should().BeTrue();
        second.Data.Evidence.Items.Single().Amount.Should().Be(900m);
        db.Journals.Should().BeEmpty();
    }

    [Fact]
    public async Task Payment_evidence_is_student_wide_and_retains_failed_history()
    {
        await using var db = Database();
        var stay = await SeedHostel(db);
        db.StudentPayments.AddRange(
            new StudentPayment { TenantId = 41, StudentId = stay.StudentId,
                ReceiptNumber = "FAILED", PaymentDate = new DateOnly(2026, 10, 1),
                State = PaymentState.Failed, Amount = 50m },
            new StudentPayment { TenantId = 41, StudentId = stay.StudentId,
                ReceiptNumber = "SUCCESS", PaymentDate = new DateOnly(2026, 10, 2),
                State = PaymentState.Successful, Amount = 150m, JournalId = 7 },
            new StudentPayment { TenantId = 41, StudentId = 999,
                ReceiptNumber = "OTHER", PaymentDate = new DateOnly(2026, 10, 3),
                State = PaymentState.Successful, Amount = 100m });
        await db.SaveChangesAsync();
        var result = await Service(db).GetReservationFinanceEvidenceAsync(new()
            { Kind = "hostel", ReservationId = stay.Id, Source = "payment" });
        result.Success.Should().BeTrue();
        result.Data!.Evidence.TotalCount.Should().Be(2);
        result.Data.Evidence.Items[0].Reference.Should().Be("SUCCESS");
        result.Data.Evidence.Items[0].HasPostedActivity.Should().BeTrue();
        result.Data.Evidence.Items[0].JournalId.Should().Be(7);
        result.Data.Evidence.Items[1].HasPostedActivity.Should().BeFalse();
        result.Data.Evidence.Items[0].PaidAmount.Should().BeNull();
    }

    [Fact]
    public async Task Invalid_campus_linkage_fails_closed_before_financial_reads()
    {
        await using var db = Database();
        var stay = await SeedHostel(db, campus: 8);
        var result = await Service(db).GetReservationFinanceEvidenceAsync(new()
            { Kind = "hostel", ReservationId = stay.Id });
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Cross_tenant_reservation_is_not_disclosed()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("evidence-cross-tenant-" + Guid.NewGuid().ToString("N")).Options;
        long foreignId;
        await using (var foreign = Database(42, options))
            foreignId = (await SeedHostel(foreign, tenant: 42)).Id;
        await using var own = Database(41, options);
        (await Service(own).GetReservationFinanceEvidenceAsync(new()
            { Kind = "hostel", ReservationId = foreignId })).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Transport_evidence_reuses_route_vehicle_and_campus_validation()
    {
        await using var db = Database();
        var enrollment = new StudentEnrollment { TenantId = 41, StudentId = 10, CampusId = 7, RollNo = "T-10" };
        var route = new Route { TenantId = 41, CampusId = 8, Name = "Other campus", Code = "R-8" };
        var vehicle = new Vehicle { TenantId = 41, CampusId = 7, VehicleNumber = "V-7", Capacity = 10 };
        db.StudentEnrollments.Add(enrollment); db.Routes.Add(route); db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        var ride = new StudentTransport { TenantId = 41, StudentId = 10,
            StudentEnrollmentId = enrollment.Id, RouteId = route.Id, VehicleId = vehicle.Id,
            StartDate = new DateOnly(2026, 11, 1) };
        db.StudentTransports.Add(ride); await db.SaveChangesAsync();
        (await Service(db).GetReservationFinanceEvidenceAsync(new()
            { Kind = "transport", ReservationId = ride.Id })).StatusCode.Should().Be(409);
    }

    private static FinanceAdjustmentService Service(EduOSDbContext db, bool admin = true) =>
        new(db, new User(admin), TimeProvider.System);

    private static async Task<StudentHostelAllocation> SeedHostel(
        EduOSDbContext db, long tenant = 41, long campus = 7)
    {
        var enrollment = new StudentEnrollment { TenantId = tenant, StudentId = 10,
            CampusId = 7, RollNo = "H-10" };
        var hostel = new Hostel { TenantId = tenant, CampusId = campus,
            Name = "Hostel", Code = "H-1" };
        db.StudentEnrollments.Add(enrollment); db.Hostels.Add(hostel);
        await db.SaveChangesAsync();
        var room = new HostelRoom { TenantId = tenant, HostelId = hostel.Id,
            RoomNumber = "101", Capacity = 2 };
        db.HostelRooms.Add(room); await db.SaveChangesAsync();
        var bed = new HostelBed { TenantId = tenant, HostelRoomId = room.Id, BedNumber = "A" };
        db.HostelBeds.Add(bed); await db.SaveChangesAsync();
        var stay = new StudentHostelAllocation { TenantId = tenant, StudentId = 10,
            StudentEnrollmentId = enrollment.Id, HostelBedId = bed.Id,
            StartDate = new DateOnly(2026, 11, 1) };
        db.StudentHostelAllocations.Add(stay); await db.SaveChangesAsync();
        return stay;
    }

    private static EduOSDbContext Database(long tenant = 41,
        DbContextOptions<EduOSDbContext>? options = null)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        options ??= new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("reservation-evidence-" + Guid.NewGuid().ToString("N")).Options;
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
