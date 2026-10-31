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
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("reservation-cross-tenant-" + Guid.NewGuid().ToString("N")).Options;
        long foreignId;
        await using (var foreign = Database(42, options))
        {
            foreignId = (await SeedHostel(foreign, 42, 7)).Id;
        }
        await using var db = Database(41, options);
        var response = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .GetReservationFinancePreflightAsync("hostel", foreignId);
        response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Transport_payment_and_monthly_fare_are_exposed_without_approval()
    {
        await using var db = Database();
        var enrollment = new StudentEnrollment { TenantId = 41, StudentId = 10, CampusId = 7, RollNo = "T-10" };
        db.StudentEnrollments.Add(enrollment);
        await db.SaveChangesAsync();
        var route = new Route { TenantId = 41, CampusId = 7, Name = "Main", Code = "R1" };
        var vehicle = new Vehicle { TenantId = 41, CampusId = 7, VehicleNumber = "T-1", Capacity = 20 };
        db.Routes.Add(route);
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        var ride = new StudentTransport {
            TenantId = 41, StudentId = 10, StudentEnrollmentId = enrollment.Id,
            RouteId = route.Id, VehicleId = vehicle.Id,
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
        response.Data.TransportRouteId.Should().Be(route.Id);
        response.Data.TransportVehicleId.Should().Be(vehicle.Id);
        response.Data.FinancialMutationApproved.Should().BeFalse();
    }

    [Theory]
    [InlineData(8, 7)]
    [InlineData(7, 8)]
    public async Task Transport_route_or_vehicle_campus_mismatch_fails_closed(long routeCampus, long vehicleCampus)
    {
        await using var db = Database();
        var enrollment = new StudentEnrollment { TenantId = 41, StudentId = 10, CampusId = 7, RollNo = "T-CAMPUS" };
        db.StudentEnrollments.Add(enrollment);
        var route = new Route { TenantId = 41, CampusId = routeCampus, Name = "Other campus", Code = "R-C" };
        var vehicle = new Vehicle { TenantId = 41, CampusId = vehicleCampus, VehicleNumber = "T-C", Capacity = 20 };
        db.Routes.Add(route);
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        var ride = new StudentTransport {
            TenantId = 41, StudentId = 10, StudentEnrollmentId = enrollment.Id,
            RouteId = route.Id, VehicleId = vehicle.Id, StartDate = new DateOnly(2026,11,1)
        };
        db.StudentTransports.Add(ride);
        await db.SaveChangesAsync();
        var response = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .GetReservationFinancePreflightAsync("transport", ride.Id);
        response.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Transport_missing_route_fails_closed()
    {
        await using var db = Database();
        var enrollment = new StudentEnrollment { TenantId = 41, StudentId = 10, CampusId = 7, RollNo = "T-ORPHAN" };
        db.StudentEnrollments.Add(enrollment);
        await db.SaveChangesAsync();
        var ride = new StudentTransport {
            TenantId = 41, StudentId = 10, StudentEnrollmentId = enrollment.Id,
            RouteId = 999, VehicleId = 999, StartDate = new DateOnly(2026,11,1)
        };
        db.StudentTransports.Add(ride);
        await db.SaveChangesAsync();
        var response = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .GetReservationFinancePreflightAsync("transport", ride.Id);
        response.StatusCode.Should().Be(409);
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

    [Fact]
    public async Task Proposed_change_requires_admin_and_canonical_tenant_scope()
    {
        await using var db = Database();
        var stay = await SeedHostel(db, 41, 7);
        var request = new ReservationFinanceChangeReviewRequestDto {
            Kind = "hostel", ReservationId = stay.Id, Change = "reschedule-start",
            ProposedStartDate = new DateOnly(2026, 11, 2)
        };
        (await new FinanceAdjustmentService(db, new TestUser(false), TimeProvider.System)
            .ReviewReservationChangeAsync(request)).StatusCode.Should().Be(403);
        (await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .ReviewReservationChangeAsync(new() { Kind = "hostel", ReservationId = stay.Id + 999,
                Change = "reschedule-start", ProposedStartDate = new DateOnly(2026, 11, 2) }))
            .StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Proposed_date_change_with_posted_invoice_is_blocked_without_ledger_mutation()
    {
        await using var db = Database();
        var stay = await SeedHostel(db, 41, 7);
        db.StudentInvoices.Add(new StudentInvoice {
            TenantId = 41, StudentEnrollmentId = stay.StudentEnrollmentId,
            InvoiceNumber = "PROPOSAL-POST", InvoiceDate = new DateOnly(2026, 10, 1),
            DueDate = new DateOnly(2026, 10, 31), JournalId = 77
        });
        await db.SaveChangesAsync();
        var response = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .ReviewReservationChangeAsync(new() { Kind = "hostel", ReservationId = stay.Id,
                Change = "reschedule-start", ProposedStartDate = new DateOnly(2026, 11, 2) });
        response.Success.Should().BeTrue();
        response.Data!.Status.Should().Be("BLOCKED_POSTED_ACTIVITY");
        response.Data.RequiresApprovedFinanceAdjustment.Should().BeTrue();
        response.Data.FinancialMutationApproved.Should().BeFalse();
        (await db.StudentHostelAllocations.SingleAsync()).StartDate.Should().Be(new DateOnly(2026, 11, 1));
        db.Journals.Should().BeEmpty();
    }

    [Fact]
    public async Task Proposed_change_of_chargeable_stay_is_blocked_without_inventing_proration()
    {
        await using var db = Database();
        var stay = await SeedHostel(db, 41, 7);
        stay.MonthlyRent = 1200m;
        stay.EndDate = new DateOnly(2026, 11, 30);
        await db.SaveChangesAsync();
        var response = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .ReviewReservationChangeAsync(new() { Kind = "hostel", ReservationId = stay.Id,
                Change = "shorten-end", ProposedEndDate = new DateOnly(2026, 11, 20) });
        response.Success.Should().BeTrue();
        response.Data!.Status.Should().Be("BLOCKED_CHARGED_TERMS");
        response.Data.MonthlyCharge.Should().Be(1200m);
        response.Data.CalculationMode.Should().Be("DailyProrated");
        response.Data.SuggestedNetAdjustment.Should().Be(-400m);
        response.Data.FinancialMutationApproved.Should().BeFalse();
    }

    [Fact]
    public async Task Campus_fixed_monthly_quote_is_dynamic_but_does_not_approve_charged_reschedule()
    {
        await using var db = Database();
        var stay = await SeedHostel(db, 41, 7);
        stay.MonthlyRent = 1200m;
        stay.EndDate = new DateOnly(2026, 11, 30);
        db.HostelBillingPolicies.Add(new HostelBillingPolicy {
            TenantId = 41, CampusId = 7, CalculationMode = HostelBillingCalculationMode.FixedMonthly
        });
        await db.SaveChangesAsync();
        var response = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .ReviewReservationChangeAsync(new() { Kind = "hostel", ReservationId = stay.Id,
                Change = "reschedule-start", ProposedStartDate = new DateOnly(2026, 11, 10) });
        response.Success.Should().BeTrue();
        response.Data!.CalculationMode.Should().Be("FixedMonthly");
        response.Data.OriginalPeriodEstimate.Should().Be(1200m);
        response.Data.ProposedPeriodEstimate.Should().Be(1200m);
        response.Data.SuggestedNetAdjustment.Should().Be(0m);
        response.Data.Status.Should().Be("BLOCKED_CHARGED_TERMS");
        response.Data.FinancialMutationApproved.Should().BeFalse();
        (await db.StudentHostelAllocations.SingleAsync()).StartDate.Should().Be(new DateOnly(2026, 11, 1));
        db.Journals.Should().BeEmpty();
    }

    [Fact]
    public async Task Campus_can_allow_manual_signed_quote_without_approving_or_posting()
    {
        await using var db = Database();
        var stay = await SeedHostel(db, 41, 7);
        stay.MonthlyRent = 1200m;
        stay.EndDate = new DateOnly(2026, 11, 30);
        db.HostelBillingPolicies.Add(new HostelBillingPolicy {
            TenantId = 41, CampusId = 7,
            CalculationMode = HostelBillingCalculationMode.DailyProrated,
            AllowManualOverride = true
        });
        await db.SaveChangesAsync();
        var result = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .ReviewReservationChangeAsync(new() {
                Kind = "hostel", ReservationId = stay.Id, Change = "shorten-end",
                ProposedEndDate = new DateOnly(2026, 11, 20), ManualNetAdjustment = -125m,
                ManualOverrideReason = "Authorized future-billing adjustment after inspecting contract terms."
            });
        result.Success.Should().BeTrue();
        result.Data!.SuggestedNetAdjustment.Should().Be(-400m);
        result.Data.SelectedNetAdjustment.Should().Be(-125m);
        result.Data.ManualOverrideAllowed.Should().BeTrue();
        result.Data.ManualOverrideApplied.Should().BeTrue();
        result.Data.AdjustmentSelectionMode.Should().Be("ManualOverride");
        result.Data.FinancialMutationApproved.Should().BeFalse();
        db.Journals.Should().BeEmpty();
        (await db.StudentHostelAllocations.SingleAsync()).EndDate.Should().Be(new DateOnly(2026, 11, 30));
    }

    [Fact]
    public async Task Campus_disallows_manual_quote_override_server_side()
    {
        await using var db = Database();
        var stay = await SeedHostel(db, 41, 7);
        stay.MonthlyRent = 1200m;
        stay.EndDate = new DateOnly(2026, 11, 30);
        db.HostelBillingPolicies.Add(new HostelBillingPolicy {
            TenantId = 41, CampusId = 7,
            CalculationMode = HostelBillingCalculationMode.FixedMonthly,
            AllowManualOverride = false
        });
        await db.SaveChangesAsync();
        var result = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .ReviewReservationChangeAsync(new() {
                Kind = "hostel", ReservationId = stay.Id, Change = "reschedule-start",
                ProposedStartDate = new DateOnly(2026, 11, 5), ManualNetAdjustment = 0m,
                ManualOverrideReason = "Manual price change not permitted for this campus."
            });
        result.StatusCode.Should().Be(409);
        db.Journals.Should().BeEmpty();
    }

    [Fact]
    public async Task No_charge_proposal_is_informational_and_never_grants_approval()
    {
        await using var db = Database();
        var stay = await SeedHostel(db, 41, 7);
        var response = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .ReviewReservationChangeAsync(new() { Kind = "hostel", ReservationId = stay.Id,
                Change = "reschedule-start", ProposedStartDate = new DateOnly(2026, 11, 2) });
        response.Success.Should().BeTrue();
        response.Data!.Status.Should().Be("NO_KNOWN_EXPOSURE_NOT_APPROVED");
        response.Data.FinanceExposureBlocksChange.Should().BeFalse();
        response.Data.FinancialMutationApproved.Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_end_dates_and_unsupported_transfer_proposals_are_rejected()
    {
        await using var db = Database();
        var stay = await SeedHostel(db, 41, 7);
        stay.EndDate = new DateOnly(2026, 11, 30);
        await db.SaveChangesAsync();
        var service = new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System);
        (await service.ReviewReservationChangeAsync(new() { Kind = "hostel", ReservationId = stay.Id,
            Change = "extend-end", ProposedEndDate = new DateOnly(2027, 3, 1) }))
            .StatusCode.Should().Be(400);
        (await service.ReviewReservationChangeAsync(new() { Kind = "hostel", ReservationId = stay.Id,
            Change = "shorten-end", ProposedEndDate = new DateOnly(2026, 10, 30) }))
            .StatusCode.Should().Be(400);
        (await service.ReviewReservationChangeAsync(new() { Kind = "hostel", ReservationId = stay.Id,
            Change = "occupied-transfer" })).StatusCode.Should().Be(400);
    }


    [Fact]
    public async Task Legacy_soft_deleted_posted_invoice_is_still_reported_as_blocking()
    {
        await using var db = Database();
        var stay = await SeedHostel(db, 41, 7);
        db.StudentInvoices.Add(new StudentInvoice
        {
            TenantId = 41, StudentEnrollmentId = stay.StudentEnrollmentId,
            InvoiceNumber = "H-LEGACY", InvoiceDate = new DateOnly(2026, 10, 1),
            DueDate = new DateOnly(2026, 10, 31), State = InvoiceState.Cancelled,
            JournalId = 91, IsDeleted = true
        });
        await db.SaveChangesAsync();
        var result = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .GetReservationFinancePreflightAsync("hostel", stay.Id);
        result.Success.Should().BeTrue();
        result.Data!.HasPostedInvoiceActivity.Should().BeTrue();
        result.Data.RequiresApprovedFinanceAdjustment.Should().BeTrue();
        result.Data.FinancialMutationApproved.Should().BeFalse();
    }


    [Fact]
    public async Task Finance_preflight_fails_closed_when_authenticated_tenant_differs_from_db_context()
    {
        await using var db = Database(42);
        var response = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .GetReservationFinancePreflightAsync("hostel", 1);
        response.StatusCode.Should().Be(403);
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

    private static EduOSDbContext Database(long tenant = 41,
        DbContextOptions<EduOSDbContext>? options = null)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        options ??= new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("reservation-preflight-" + Guid.NewGuid().ToString("N")).Options;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
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
