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

public sealed class ReservationFinanceWorklistTests
{
    [Fact]
    public async Task Only_tenant_admin_can_list_finance_exposure()
    {
        await using var db = Database();
        var result = await new FinanceAdjustmentService(db, new User(false), TimeProvider.System)
            .GetReservationFinanceWorklistAsync(new ReservationFinanceWorklistQueryDto());
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Invalid_filters_are_rejected()
    {
        await using var db = Database();
        var service = new FinanceAdjustmentService(db, new User(true), TimeProvider.System);
        (await service.GetReservationFinanceWorklistAsync(new() { Kind = "unknown" })).StatusCode.Should().Be(400);
        (await service.GetReservationFinanceWorklistAsync(new() { PageSize = 101 })).StatusCode.Should().Be(400);
        (await service.GetReservationFinanceWorklistAsync(new() { CampusId = -1 })).StatusCode.Should().Be(400);
        (await service.GetReservationFinanceWorklistAsync(new() {
            StartFrom = new DateOnly(2026, 11, 2), StartTo = new DateOnly(2026, 11, 1)
        })).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Hostel_review_queue_filters_before_pagination_and_keeps_invoices_immutable()
    {
        await using var db = Database();
        var enrollment = await Enrollment(db, 41, 10, 7);
        var free = new StudentHostelAllocation {
            TenantId = 41, StudentId = 10, StudentEnrollmentId = enrollment.Id,
            HostelBedId = 501, StartDate = new DateOnly(2026, 11, 1)
        };
        var charged = new StudentHostelAllocation {
            TenantId = 41, StudentId = 10, StudentEnrollmentId = enrollment.Id,
            HostelBedId = 502, StartDate = new DateOnly(2026, 11, 2), MonthlyRent = 1200m
        };
        db.StudentHostelAllocations.AddRange(free, charged);
        await db.SaveChangesAsync();
        var service = new FinanceAdjustmentService(db, new User(true), TimeProvider.System);
        var result = await service.GetReservationFinanceWorklistAsync(new() { Kind = "hostel", PageSize = 1 });
        result.Success.Should().BeTrue();
        result.Data!.Reservations.TotalCount.Should().Be(1);
        result.Data.Reservations.Items.Should().ContainSingle()
            .Which.ReservationId.Should().Be(charged.Id);
        result.Data.Reservations.Items[0].FinancialMutationApproved.Should().BeFalse();
        db.Journals.Should().BeEmpty();
    }

    [Fact]
    public async Task Issued_invoice_and_successful_payment_expose_zero_fare_transport()
    {
        await using var db = Database();
        var enrollment = await Enrollment(db, 41, 10, 7);
        var ride = new StudentTransport {
            TenantId = 41, StudentId = 10, StudentEnrollmentId = enrollment.Id,
            RouteId = 901, VehicleId = 902, StartDate = new DateOnly(2026, 11, 1)
        };
        db.StudentTransports.Add(ride);
        db.StudentInvoices.Add(new StudentInvoice {
            TenantId = 41, StudentEnrollmentId = enrollment.Id,
            InvoiceNumber = "ISSUED-1", State = InvoiceState.Issued,
            InvoiceDate = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 31)
        });
        db.StudentPayments.Add(new StudentPayment {
            TenantId = 41, StudentId = 10, ReceiptNumber = "RECEIPT-1",
            PaymentDate = new DateOnly(2026, 10, 1), Amount = 200m,
            State = PaymentState.Successful
        });
        await db.SaveChangesAsync();
        var result = await new FinanceAdjustmentService(db, new User(true), TimeProvider.System)
            .GetReservationFinanceWorklistAsync(new() { Kind = "transport" });
        result.Success.Should().BeTrue();
        var row = result.Data!.Reservations.Items.Single();
        row.HasUnpostedIssuedInvoice.Should().BeTrue();
        row.HasPostedPaymentActivity.Should().BeTrue();
        row.RequiresFinanceReview.Should().BeTrue();
        row.FinancialMutationApproved.Should().BeFalse();
    }

    [Fact]
    public async Task Campus_filter_limits_results_to_enrollment_campus()
    {
        await using var db = Database();
        var own = await Enrollment(db, 41, 10, 7);
        var otherCampus = await Enrollment(db, 41, 11, 8);
        db.StudentHostelAllocations.AddRange(
            new StudentHostelAllocation { TenantId = 41, StudentId = 10, StudentEnrollmentId = own.Id,
                HostelBedId = 1, StartDate = new DateOnly(2026, 11, 1), MonthlyRent = 10m },
            new StudentHostelAllocation { TenantId = 41, StudentId = 11, StudentEnrollmentId = otherCampus.Id,
                HostelBedId = 2, StartDate = new DateOnly(2026, 11, 1), MonthlyRent = 20m });
        await db.SaveChangesAsync();
        var result = await new FinanceAdjustmentService(db, new User(true), TimeProvider.System)
            .GetReservationFinanceWorklistAsync(new() { Kind = "hostel", CampusId = 7 });
        result.Success.Should().BeTrue();
        result.Data!.Reservations.TotalCount.Should().Be(1);
        result.Data.Reservations.Items.Single().StudentId.Should().Be(10);
    }

    [Fact]
    public async Task Other_tenant_reservations_are_not_listed()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("reservation-finance-cross-tenant-" + Guid.NewGuid().ToString("N")).Options;
        await using (var foreignDb = Database(42, options))
        {
            var foreign = await Enrollment(foreignDb, 42, 12, 7);
            foreignDb.StudentHostelAllocations.Add(new StudentHostelAllocation {
                TenantId = 42, StudentId = 12, StudentEnrollmentId = foreign.Id,
                HostelBedId = 3, StartDate = new DateOnly(2026, 11, 1), MonthlyRent = 30m
            });
            await foreignDb.SaveChangesAsync();
        }
        await using var db = Database(41, options);
        var result = await new FinanceAdjustmentService(db, new User(true), TimeProvider.System)
            .GetReservationFinanceWorklistAsync(new() { Kind = "hostel", CampusId = 7 });
        result.Success.Should().BeTrue();
        result.Data!.Reservations.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Uncharged_reservations_are_visible_when_review_only_is_disabled()
    {
        await using var db = Database();
        var enrollment = await Enrollment(db, 41, 10, 7);
        db.StudentTransports.Add(new StudentTransport {
            TenantId = 41, StudentId = 10, StudentEnrollmentId = enrollment.Id,
            RouteId = 1, VehicleId = 2, StartDate = new DateOnly(2026, 11, 1)
        });
        await db.SaveChangesAsync();
        var result = await new FinanceAdjustmentService(db, new User(true), TimeProvider.System)
            .GetReservationFinanceWorklistAsync(new() { Kind = "transport", OnlyReviewRequired = false });
        result.Data!.Reservations.TotalCount.Should().Be(1);
        result.Data.Reservations.Items.Single().RequiresFinanceReview.Should().BeFalse();
    }


    [Fact]
    public async Task Zero_fare_reservation_with_legacy_deleted_posting_stays_in_finance_review_queue()
    {
        await using var db = Database();
        var enrollment = await Enrollment(db, 41, 10, 7);
        db.StudentTransports.Add(new StudentTransport
        {
            TenantId = 41, StudentId = 10, StudentEnrollmentId = enrollment.Id,
            RouteId = 1, VehicleId = 2, StartDate = new DateOnly(2026, 11, 1)
        });
        db.StudentPayments.Add(new StudentPayment
        {
            TenantId = 41, StudentId = 10, ReceiptNumber = "HISTORY-PAID",
            PaymentDate = new DateOnly(2026, 10, 1), Amount = 40m,
            State = PaymentState.Refunded, IsDeleted = true
        });
        await db.SaveChangesAsync();
        var result = await new FinanceAdjustmentService(db, new User(true), TimeProvider.System)
            .GetReservationFinanceWorklistAsync(new() { Kind = "transport" });
        result.Success.Should().BeTrue();
        result.Data!.Reservations.TotalCount.Should().Be(1);
        var row = result.Data.Reservations.Items.Single();
        row.HasPostedPaymentActivity.Should().BeTrue();
        row.RequiresFinanceReview.Should().BeTrue();
        row.FinancialMutationApproved.Should().BeFalse();
    }

    private static async Task<StudentEnrollment> Enrollment(EduOSDbContext db,
        long tenant, long student, long campus)
    {
        var enrollment = new StudentEnrollment { TenantId = tenant, StudentId = student,
            CampusId = campus, RollNo = "F-" + student };
        db.StudentEnrollments.Add(enrollment);
        await db.SaveChangesAsync();
        return enrollment;
    }

    private static EduOSDbContext Database(long tenant = 41,
        DbContextOptions<EduOSDbContext>? options = null)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        options ??= new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("reservation-finance-worklist-" + Guid.NewGuid().ToString("N")).Options;
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
