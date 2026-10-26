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

public sealed class ReservationFinancePreflightTests
{
    [Fact]
    public async Task Non_admin_cannot_inspect_reservation_finances()
    {
        await using var db = Database();
        var response = await new FinanceAdjustmentService(db, new TestUser(false), TimeProvider.System)
            .GetReservationFinancePreflightAsync("hostel", 1);
        response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Invalid_reservation_scope_is_rejected()
    {
        await using var db = Database();
        var service = new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System);
        (await service.GetReservationFinancePreflightAsync("hostel", 0)).StatusCode.Should().Be(400);
        (await service.GetReservationFinancePreflightAsync("other", 1)).StatusCode.Should().Be(400);
        (await service.GetReservationFinancePreflightAsync("hostel", 99)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Posted_invoice_blocks_hostel_financial_change_without_mutating_ledger()
    {
        await using var db = Database();
        var stay = await SeedHostel(db, 41, 7);
        db.StudentInvoices.Add(new StudentInvoice {
            TenantId = 41, StudentEnrollmentId = stay.StudentEnrollmentId,
            InvoiceNumber = "H-POST", InvoiceDate = new DateOnly(2026,10,1),
            DueDate = new DateOnly(2026,10,31), JournalId = 9, State = InvoiceState.Issued
        });
        await db.SaveChangesAsync();
        var response = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .GetReservationFinancePreflightAsync("hostel", stay.Id);
        response.Success.Should().BeTrue();
        response.Data!.HasPostedInvoiceActivity.Should().BeTrue();
        response.Data.RequiresApprovedFinanceAdjustment.Should().BeTrue();
        response.Data.FinancialMutationApproved.Should().BeFalse();
        db.Journals.Should().BeEmpty();
    }

    [Fact]
    public async Task Hostel_campus_mismatch_fails_closed()
    {
        await using var db = Database();
        var stay = await SeedHostel(db, 41, 8);
        var response = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .GetReservationFinancePreflightAsync("hostel", stay.Id);
        response.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Other_tenant_reservation_is_not_disclosed()
    {
        await using var db = Database();
        var stay = await SeedHostel(db, 42, 7);
        var response = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .GetReservationFinancePreflightAsync("hostel", stay.Id);
        response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Transport_payment_and_monthly_fare_are_exposed_without_approval()
    {
        await using var db = Database();
        var enrollment = new StudentEnrollment { TenantId = 41, StudentId = 10, CampusId = 7, RollNo = "T-10" };
        db.StudentEnrollments.Add(enrollment);
        await db.SaveChangesAsync();
        var ride = new StudentTransport {
            TenantId = 41, StudentId = 10, StudentEnrollmentId = enrollment.Id,
            StartDate = new DateOnly(2026,11,1), EndDate = new DateOnly(2026,11,30),
            MonthlyFare = 1250m
        };
        db.StudentTransports.Add(ride);
        db.StudentPayments.Add(new StudentPayment {
            TenantId = 41, StudentId = 10, ReceiptNumber = "TR-PAY",
            PaymentDate = new DateOnly(2026,10,25), Amount = 1250m,
            State = PaymentState.Successful
        });
        await db.SaveChangesAsync();
        var response = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .GetReservationFinancePreflightAsync("transport", ride.Id);
        response.Success.Should().BeTrue();
        response.Data!.HasPostedPaymentActivity.Should().BeTrue();
        response.Data.RequiresBillingPolicy.Should().BeTrue();
        response.Data.MonthlyCharge.Should().Be(1250m);
        response.Data.FinancialMutationApproved.Should().BeFalse();
    }

    [Fact]
    public async Task No_charge_no_posting_is_informational_only()
    {
        await using var db = Database();
        var stay = await SeedHostel(db, 41, 7);
        var response = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .GetReservationFinancePreflightAsync("hostel", stay.Id);
        response.Success.Should().BeTrue();
        response.Data!.RequiresApprovedFinanceAdjustment.Should().BeFalse();
        response.Data.RequiresBillingPolicy.Should().BeFalse();
        response.Data.FinancialMutationApproved.Should().BeFalse();
    }

    private static async Task<StudentHostelAllocation> SeedHostel(EduOSDbContext db, long tenant, long hostelCampus)
    {
        var enrollment = new StudentEnrollment { TenantId = tenant, StudentId = 10, CampusId = 7, RollNo = "H-10" };
        var hostel = new Hostel { TenantId = tenant, CampusId = hostelCampus, Name = "Hostel", Code = "H1" };
        db.StudentEnrollments.Add(enrollment);
        db.Hostels.Add(hostel);
        await db.SaveChangesAsync();
        var room = new HostelRoom { TenantId = tenant, HostelId = hostel.Id, RoomNumber = "101", Capacity = 2 };
        db.HostelRooms.Add(room);
        await db.SaveChangesAsync();
        var bed = new HostelBed { TenantId = tenant, HostelRoomId = room.Id, BedNumber = "A" };
        db.HostelBeds.Add(bed);
        await db.SaveChangesAsync();
        var stay = new StudentHostelAllocation {
            TenantId = tenant, StudentId = 10, StudentEnrollmentId = enrollment.Id,
            HostelBedId = bed.Id, StartDate = new DateOnly(2026,11,1)
        };
        db.StudentHostelAllocations.Add(stay);
        await db.SaveChangesAsync();
        return stay;
    }

    private static EduOSDbContext Database()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("reservation-preflight-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
    }

    private sealed class TestUser(bool admin) : ICurrentUserService
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
