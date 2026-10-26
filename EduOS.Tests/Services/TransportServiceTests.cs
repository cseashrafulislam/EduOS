using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Transport;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;
using TransportRoute = EduOS.Core.Entities.Transport.Route;

namespace EduOS.Tests.Services;

public class TransportServiceTests
{
    [Fact]
    public async Task Legacy_campus_repair_requires_review_and_applies_only_unlinked_institution_records()
    {
        await using var db = CreateContext(CreateOptions(), 101);
        var campus = new Campus { TenantId = 101, Name = "Verified campus", Code = "VER", IsActive = true };
        var route = new TransportRoute { TenantId = 101, Name = "Old route", Code = "OLD", RowVersion = [1,2,3,4,5,6,7,8] };
        db.AddRange(campus, route);
        await db.SaveChangesAsync();
        var service = CreateService(db, 101);
        var review = await service.GetCampusAssignmentReviewAsync();
        review.Success.Should().BeTrue();
        var row = review.Data!.Rows.Single(x => x.Reference == route.PublicId);
        row.CanApply.Should().BeTrue();
        var request = new ApplyTransportCampusAssignmentDto
        {
            CampusId = campus.Id, RowVersion = row.RowVersion
        };
        var premature = await service.ApplyCampusAssignmentAsync("route", route.PublicId, request);
        premature.StatusCode.Should().Be(400);
        route.CampusId.Should().BeNull();
        request.ConfirmHistoricalCampusReview = true;
        var success = await service.ApplyCampusAssignmentAsync("route", route.PublicId, request);
        success.Success.Should().BeTrue(success.Message);
        route.CampusId.Should().Be(campus.Id);
        var auditTrail = await db.AuditLogs.IgnoreQueryFilters().ToListAsync();
        auditTrail.Any(x => x.TenantId == 101 && x.EntityName == "Route" && x.Action == "Update" &&
            x.EntityId == route.Id).Should().BeTrue(string.Join(";", auditTrail.Select(x =>
                $"{x.TenantId}/{x.EntityName}/{x.EntityId}/{x.Action}")));
        (await service.GetCampusAssignmentReviewAsync()).Data!.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Legacy_campus_repair_rejects_links_stale_versions_and_other_tenants_campuses()
    {
        await using var db = CreateContext(CreateOptions(), 101);
        var campus = new Campus { TenantId = 101, Name = "Local", Code = "L", IsActive = true };
        var route = new TransportRoute { TenantId = 101, Name = "Linked", Code = "LINK", RowVersion = [1,2,3,4,5,6,7,8] };
        var vehicle = new Vehicle { TenantId = 101, VehicleNumber = "LEGACY-01", Capacity = 12,
            RowVersion = [1,2,3,4,5,6,7,8] };
        db.AddRange(campus, route, vehicle);
        await db.SaveChangesAsync();
        db.RouteVehicleAssignments.Add(new RouteVehicleAssignment
        {
            TenantId = 101, RouteId = route.Id, VehicleId = vehicle.Id,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.Today.AddDays(-30)), IsCurrent = false,
            IsDeleted = true
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, 101);
        var review = await service.GetCampusAssignmentReviewAsync();
        review.Success.Should().BeTrue();
        review.Data!.Rows.Should().HaveCount(2);
        review.Data.Rows.Should().OnlyContain(x => !x.CanApply && x.LinkedRecords > 0);
        var linked = await service.ApplyCampusAssignmentAsync("vehicle", vehicle.PublicId,
            new ApplyTransportCampusAssignmentDto { CampusId = campus.Id,
                RowVersion = Convert.ToBase64String(vehicle.RowVersion), ConfirmHistoricalCampusReview = true });
        linked.StatusCode.Should().Be(409);
        vehicle.CampusId.Should().BeNull();
        var stale = await service.ApplyCampusAssignmentAsync("route", route.PublicId,
            new ApplyTransportCampusAssignmentDto { CampusId = campus.Id, RowVersion = Convert.ToBase64String([9,9,9,9,9,9,9,9]),
                ConfirmHistoricalCampusReview = true });
        stale.StatusCode.Should().Be(409);
        var unauthorised = await CreateService(db, 101, false).GetCampusAssignmentReviewAsync();
        unauthorised.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Upcoming_route_vehicle_change_persists_outcome_and_rejects_key_reuse()
    {
        await using var db = CreateContext(CreateOptions(), 101);
        var preexistingRider = await SeedAssignmentAsync(db, 101, true, [1,2,3,4,5,6,7,8]);
        // This fixture uses a placeholder link ID; the rider must not reference
        // the planned link created by this test.
        preexistingRider.RouteVehicleAssignmentId = null;
        await db.SaveChangesAsync();
        var route = await db.Routes.SingleAsync();
        var campus = await db.Campuses.SingleAsync();
        route.CampusId = campus.Id;
        var oldVehicle = new Vehicle { TenantId = 101, CampusId = campus.Id,
            VehicleNumber = "TEST-OLD", Capacity = 30, RowVersion = [1,1,1,1,1,1,1,1] };
        var nextVehicle = new Vehicle { TenantId = 101, CampusId = campus.Id,
            VehicleNumber = "TEST-NEW", Capacity = 30, RowVersion = [2,2,2,2,2,2,2,2] };
        db.Vehicles.AddRange(oldVehicle, nextVehicle);
        await db.SaveChangesAsync();
        var link = new RouteVehicleAssignment { TenantId = 101, RouteId = route.Id,
            VehicleId = oldVehicle.Id, EffectiveFrom = DateOnly.FromDateTime(DateTime.Today.AddDays(10)),
            EffectiveTo = DateOnly.FromDateTime(DateTime.Today.AddDays(20)), IsCurrent = true,
            RowVersion = [3,3,3,3,3,3,3,3] };
        db.RouteVehicleAssignments.Add(link);
        await db.SaveChangesAsync();
        var key = Guid.NewGuid();
        var request = new ChangeUpcomingRouteVehicleRequestDto { ClientRequestId = key,
            NewVehicleId = nextVehicle.Id, AssignmentRowVersion = Convert.ToBase64String(link.RowVersion),
            VehicleRowVersion = Convert.ToBase64String(nextVehicle.RowVersion) };
        var service = CreateService(db, 101);

        var first = await service.ChangeUpcomingRouteVehicleAsync(link.Id, request);
        first.Success.Should().BeTrue(first.Message);
        var updatedAt = link.UpdatedAt;
        var second = await service.ChangeUpcomingRouteVehicleAsync(link.Id, request);
        second.Success.Should().BeTrue(second.Message);
        second.Message.Should().Be(first.Message);
        second.Data!.VehicleId.Should().Be(nextVehicle.Id);
        link.UpdatedAt.Should().Be(updatedAt);
        (await db.IdempotencyRecords.CountAsync(x => x.Scope == "transport.route-vehicle-change"))
            .Should().Be(1);
        var wrong = await service.ChangeUpcomingRouteVehicleAsync(link.Id,
            new ChangeUpcomingRouteVehicleRequestDto { ClientRequestId = key,
                NewVehicleId = oldVehicle.Id, AssignmentRowVersion = request.AssignmentRowVersion,
                VehicleRowVersion = request.VehicleRowVersion });
        wrong.StatusCode.Should().Be(409);
        link.VehicleId.Should().Be(nextVehicle.Id);
    }

    [Fact]
    public async Task Upcoming_charged_transport_extension_requires_finance_billing_contract()
    {
        await using var db = CreateContext(CreateOptions(), 101);
        var rider = await SeedAssignmentAsync(db, 101, true, [1,2,3,4,5,6,7,8]);
        rider.StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(10));
        rider.EndDate = rider.StartDate.AddDays(10);
        await db.SaveChangesAsync();
        var previousEnd = rider.EndDate;

        var response = await CreateService(db, 101).ExtendUpcomingRiderAsync(rider.PublicId,
            new ExtendUpcomingTransportAssignmentRequestDto
            {
                ClientRequestId = Guid.NewGuid(),
                EndDate = previousEnd!.Value.AddDays(10),
                RowVersion = Convert.ToBase64String(rider.RowVersion)
            });

        response.Success.Should().BeFalse();
        response.StatusCode.Should().Be(409);
        rider.EndDate.Should().Be(previousEnd);
        (await db.IdempotencyRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Posted_enrollment_invoice_blocks_transport_closure_without_mutating_reservation()
    {
        await using var db = CreateContext(CreateOptions(), 101);
        var rider = await SeedAssignmentAsync(db, 101, true, [1,2,3,4,5,6,7,8]);
        db.StudentInvoices.Add(new StudentInvoice
        {
            TenantId = 101, StudentEnrollmentId = rider.StudentEnrollmentId,
            ClientRequestId = Guid.NewGuid(), InvoiceNumber = "TR-POSTED",
            InvoiceDate = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 30),
            State = InvoiceState.Issued, JournalId = 543
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db, 101).CloseAsync(rider.PublicId,
            new CloseStudentTransportRequestDto
            {
                EndDate = DateOnly.FromDateTime(DateTime.Today),
                RowVersion = Convert.ToBase64String(rider.RowVersion)
            });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        var stored = await db.StudentTransports.SingleAsync(x => x.Id == rider.Id);
        stored.EndDate.Should().BeNull();
        stored.State.Should().Be(TransportAssignmentState.Active);
    }

    [Fact]
    public async Task Close_with_matching_row_version_closes_assignment()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var assignment = await SeedAssignmentAsync(context, 101, true, [1,2,3,4,5,6,7,8]);
        var service = CreateService(context, 101);
        var response = await service.CloseAsync(assignment.PublicId, new CloseStudentTransportRequestDto
        {
            EndDate = DateOnly.FromDateTime(DateTime.Today), RowVersion = Convert.ToBase64String(assignment.RowVersion)
        });
        response.Success.Should().BeTrue();
        response.Data!.State.Should().Be(TransportAssignmentState.Closed);
        var saved = await context.Set<StudentTransport>().IgnoreQueryFilters().SingleAsync(x => x.Id == assignment.Id);
        saved.State.Should().Be(TransportAssignmentState.Closed);
        saved.EndDate.Should().Be(DateOnly.FromDateTime(DateTime.Today));
    }

    [Fact]
    public async Task Close_with_stale_row_version_is_rejected_without_mutation()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var assignment = await SeedAssignmentAsync(context, 101, true, [1,2,3,4,5,6,7,8]);
        var service = CreateService(context, 101);
        var response = await service.CloseAsync(assignment.PublicId, new CloseStudentTransportRequestDto
        {
            EndDate = DateOnly.FromDateTime(DateTime.Today),
            RowVersion = Convert.ToBase64String(new byte[] { 8,7,6,5,4,3,2,1 })
        });
        response.Success.Should().BeFalse();
        response.StatusCode.Should().Be(409);
        var saved = await context.Set<StudentTransport>().IgnoreQueryFilters().SingleAsync(x => x.Id == assignment.Id);
        saved.State.Should().Be(TransportAssignmentState.Active);
        saved.EndDate.Should().BeNull();
    }

    [Fact]
    public async Task Closing_an_already_closed_assignment_remains_idempotent()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var assignment = await SeedAssignmentAsync(context, 101, false, [1,2,3,4,5,6,7,8]);
        var service = CreateService(context, 101);
        var response = await service.CloseAsync(assignment.PublicId, new CloseStudentTransportRequestDto
        {
            EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
            RowVersion = Convert.ToBase64String(new byte[] { 9,9,9,9,9,9,9,9 })
        });
        response.Success.Should().BeTrue();
        response.Data!.State.Should().Be(TransportAssignmentState.Closed);
    }

    [Theory]
    [InlineData(TransportAssignmentState.Closed, 1, false)]
    [InlineData(TransportAssignmentState.Cancelled, 0, false)]
    [InlineData(TransportAssignmentState.Closed, 0, true)]
    public async Task Close_replay_requires_matching_date_and_closed_state(
        TransportAssignmentState state, int offsetDays, bool expectedSuccess)
    {
        await using var db = CreateContext(CreateOptions(), 101);
        var assignment = await SeedAssignmentAsync(db, 101, false, [1,2,3,4,5,6,7,8]);
        assignment.State = state;
        if (state == TransportAssignmentState.Cancelled) assignment.EndDate = null;
        await db.SaveChangesAsync();
        var originalEndDate = assignment.EndDate;
        var result = await CreateService(db, 101).CloseAsync(assignment.PublicId, new CloseStudentTransportRequestDto
        {
            EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1 + offsetDays)),
            RowVersion = Convert.ToBase64String([9,9,9,9,9,9,9,9])
        });
        result.Success.Should().Be(expectedSuccess);
        if (!expectedSuccess) result.StatusCode.Should().Be(409);
        var saved = await db.Set<StudentTransport>().SingleAsync(x => x.Id == assignment.Id);
        saved.State.Should().Be(state);
        saved.EndDate.Should().Be(originalEndDate);
    }

    [Fact]
    public async Task Close_cannot_mutate_assignment_from_another_tenant()
    {
        var options = CreateOptions();
        Guid reference;
        await using (var context202 = CreateContext(options, 202))
        {
            var foreign = await SeedAssignmentAsync(context202, 202, true, [1,2,3,4,5,6,7,8]);
            reference = foreign.PublicId;
        }
        await using var context101 = CreateContext(options, 101);
        var response = await CreateService(context101, 101).CloseAsync(reference, new CloseStudentTransportRequestDto
        {
            EndDate = DateOnly.FromDateTime(DateTime.Today),
            RowVersion = Convert.ToBase64String(new byte[] { 1,2,3,4,5,6,7,8 })
        });
        response.Success.Should().BeFalse();
        response.StatusCode.Should().Be(404);
        var saved = await context101.Set<StudentTransport>().IgnoreQueryFilters().SingleAsync(x => x.PublicId == reference);
        saved.TenantId.Should().Be(202);
        saved.State.Should().Be(TransportAssignmentState.Active);
        saved.EndDate.Should().BeNull();
    }

    [Fact]
    public async Task My_assignment_requires_current_active_enrollment_and_student_status()
    {
        await using var db = CreateContext(CreateOptions(), 101);
        var assignment = await SeedAssignmentAsync(db, 101, true, [1,2,3,4,5,6,7,8]);
        var student = await db.Set<Student>().SingleAsync();
        student.UserId = 7;
        var enrollment = new StudentEnrollment
        {
            TenantId = 101, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(),
            StudentId = student.Id, CampusId = 1, AcademicYearId = 1,
            AcademicProgramId = 1, AcademicLevelId = 1, AcademicBatchId = 1,
            AcademicCurriculumId = 1, RollNo = "7",
            EnrollmentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            State = EnrollmentState.Active, IsCurrent = true
        };
        db.Add(enrollment);
        await db.SaveChangesAsync();
        assignment.StudentEnrollmentId = enrollment.Id;
        await db.SaveChangesAsync();
        var service = CreateService(db, 101);
        (await service.GetMyAssignmentAsync()).Data.Should().NotBeNull();
        enrollment.IsCurrent = false;
        await db.SaveChangesAsync();
        (await service.GetMyAssignmentAsync()).Data.Should().BeNull();
        enrollment.IsCurrent = true;
        student.StatusCode = "Transferred";
        await db.SaveChangesAsync();
        (await service.GetMyAssignmentAsync()).Data.Should().BeNull();
    }

    [Fact]
    public async Task Student_cannot_enumerate_transport_routes_or_vehicle_capacity()
    {
        await using var db = CreateContext(CreateOptions(), 101);
        var service = CreateService(db, 101, false);
        var routes = await service.GetRoutesAsync();
        var vehicles = await service.GetVehiclesAsync();
        routes.Success.Should().BeFalse();
        routes.StatusCode.Should().Be(403);
        vehicles.Success.Should().BeFalse();
        vehicles.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Tenant_admin_can_read_transport_catalogue()
    {
        await using var db = CreateContext(CreateOptions(), 101);
        var service = CreateService(db, 101);
        (await service.GetRoutesAsync()).Success.Should().BeTrue();
        (await service.GetVehiclesAsync()).Success.Should().BeTrue();
    }


    [Fact]
    public async Task Manager_catalogue_excludes_other_revoked_and_inactive_campuses()
    {
        await using var db = CreateContext(CreateOptions(), 101);
        var north = new Campus { TenantId = 101, Name = "North", Code = "N", IsActive = true };
        var south = new Campus { TenantId = 101, Name = "South", Code = "S", IsActive = true };
        var inactive = new Campus { TenantId = 101, Name = "Inactive", Code = "I", IsActive = false };
        db.AddRange(north, south, inactive);
        await db.SaveChangesAsync();
        var grant = new UserCampusAccess { TenantId = 101, UserId = 7, CampusId = north.Id, IsActive = true };
        db.AddRange(grant, new UserCampusAccess { TenantId = 101, UserId = 7, CampusId = inactive.Id, IsActive = true });
        db.AddRange(
            new TransportRoute { TenantId = 101, CampusId = north.Id, Name = "North route", Code = "NR" },
            new TransportRoute { TenantId = 101, CampusId = south.Id, Name = "South route", Code = "SR" },
            new TransportRoute { TenantId = 101, CampusId = inactive.Id, Name = "Inactive route", Code = "IR" },
            new Vehicle { TenantId = 101, CampusId = north.Id, VehicleNumber = "N-1", Capacity = 20 },
            new Vehicle { TenantId = 101, CampusId = south.Id, VehicleNumber = "S-1", Capacity = 20 },
            new Vehicle { TenantId = 101, CampusId = inactive.Id, VehicleNumber = "I-1", Capacity = 20 });
        await db.SaveChangesAsync();
        var manager = CreateService(db, 101, true, "TransportManager");
        (await manager.GetRoutesAsync()).Data!.Select(x => x.Name).Should().Equal("North route");
        (await manager.GetVehiclesAsync()).Data!.Select(x => x.VehicleNumber).Should().Equal("N-1");
        grant.IsActive = false;
        await db.SaveChangesAsync();
        (await manager.GetRoutesAsync()).Data.Should().BeEmpty();
        (await manager.GetVehiclesAsync()).Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Manager_cannot_close_assignment_for_ungranted_campus()
    {
        await using var db = CreateContext(CreateOptions(), 101);
        var assignment = await SeedAssignmentAsync(db, 101, true, [1,2,3,4,5,6,7,8]);
        var north = new Campus { TenantId = 101, Name = "North", Code = "N" };
        var south = new Campus { TenantId = 101, Name = "South", Code = "S" };
        db.AddRange(north, south);
        await db.SaveChangesAsync();
        db.Add(new UserCampusAccess { TenantId = 101, UserId = 7, CampusId = north.Id, IsActive = true });
        var enrollment = new StudentEnrollment
        {
            TenantId = 101, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(),
            StudentId = assignment.StudentId, CampusId = south.Id, AcademicYearId = 1,
            AcademicProgramId = 1, AcademicLevelId = 1, AcademicBatchId = 1,
            AcademicCurriculumId = 1, RollNo = "7",
            EnrollmentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            State = EnrollmentState.Active, IsCurrent = true
        };
        db.Add(enrollment);
        await db.SaveChangesAsync();
        assignment.StudentEnrollmentId = enrollment.Id;
        await db.SaveChangesAsync();
        var response = await CreateService(db, 101, true, "TransportManager").CloseAsync(
            assignment.PublicId, new CloseStudentTransportRequestDto
            {
                EndDate = DateOnly.FromDateTime(DateTime.Today),
                RowVersion = Convert.ToBase64String(assignment.RowVersion)
            });
        response.Success.Should().BeFalse();
        response.StatusCode.Should().Be(403);
        (await db.Set<StudentTransport>().SingleAsync()).State.Should().Be(TransportAssignmentState.Active);
    }


    [Fact]
    public async Task Tenant_admin_catalogue_and_eligible_students_ignore_inactive_campus()
    {
        await using var db = CreateContext(CreateOptions(), 101);
        var active = new Campus { TenantId = 101, Name = "Active", Code = "ACTIVE", IsActive = true };
        var inactive = new Campus { TenantId = 101, Name = "Inactive", Code = "INACTIVE", IsActive = false };
        db.AddRange(active, inactive);
        await db.SaveChangesAsync();
        db.AddRange(
            new TransportRoute { TenantId = 101, CampusId = active.Id, Name = "Active route", Code = "AR" },
            new TransportRoute { TenantId = 101, CampusId = inactive.Id, Name = "Inactive route", Code = "IR" },
            new TransportRoute { TenantId = 101, Name = "Shared route", Code = "SH" },
            new Vehicle { TenantId = 101, CampusId = active.Id, VehicleNumber = "ACTIVE-BUS", Capacity = 20 },
            new Vehicle { TenantId = 101, CampusId = inactive.Id, VehicleNumber = "INACTIVE-BUS", Capacity = 20 });
        var first = new Student { TenantId = 101, PersonId = 1, StudentCode = "ACTIVE-STU", FullName = "Active learner", StatusCode = "Active" };
        var second = new Student { TenantId = 101, PersonId = 2, StudentCode = "INACTIVE-STU", FullName = "Inactive learner", StatusCode = "Active" };
        db.AddRange(first, second);
        await db.SaveChangesAsync();
        db.AddRange(
            new StudentEnrollment { TenantId = 101, StudentId = first.Id, CampusId = active.Id, PublicId = Guid.NewGuid(),
                ClientRequestId = Guid.NewGuid(), IsCurrent = true, State = EnrollmentState.Active,
                AcademicYearId = 1, AcademicProgramId = 1, AcademicLevelId = 1, AcademicBatchId = 1,
                AcademicCurriculumId = 1, RollNo = "1" },
            new StudentEnrollment { TenantId = 101, StudentId = second.Id, CampusId = inactive.Id, PublicId = Guid.NewGuid(),
                ClientRequestId = Guid.NewGuid(), IsCurrent = true, State = EnrollmentState.Active,
                AcademicYearId = 1, AcademicProgramId = 1, AcademicLevelId = 1, AcademicBatchId = 1,
                AcademicCurriculumId = 1, RollNo = "2" });
        await db.SaveChangesAsync();
        var service = CreateService(db, 101);
        (await service.GetRoutesAsync()).Data!.Select(x => x.Name).Should().BeEquivalentTo(["Active route", "Shared route"]);
        (await service.GetVehiclesAsync()).Data!.Select(x => x.VehicleNumber).Should().Equal("ACTIVE-BUS");
        var eligible = await service.GetEligibleStudentsAsync(1, 25, null);
        eligible.Success.Should().BeTrue();
        eligible.Data!.Items.Select(x => x.StudentCode).Should().Equal("ACTIVE-STU");
        eligible.Data.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Tenant_admin_cannot_assign_transport_to_inactive_campus()
    {
        await using var db = CreateContext(CreateOptions(), 101);
        var campus = new Campus { TenantId = 101, Name = "Inactive", Code = "INACTIVE", IsActive = false };
        var student = new Student { TenantId = 101, PersonId = 1, StudentCode = "STU-1", FullName = "Learner", StatusCode = "Active" };
        db.AddRange(campus, student);
        await db.SaveChangesAsync();
        var enrollment = new StudentEnrollment
        {
            TenantId = 101, StudentId = student.Id, CampusId = campus.Id, PublicId = Guid.NewGuid(),
            ClientRequestId = Guid.NewGuid(), IsCurrent = true, State = EnrollmentState.Active,
            AcademicYearId = 1, AcademicProgramId = 1, AcademicLevelId = 1, AcademicBatchId = 1,
            AcademicCurriculumId = 1, RollNo = "1"
        };
        var route = new TransportRoute { TenantId = 101, CampusId = campus.Id, Name = "Route", Code = "R" };
        var vehicle = new Vehicle { TenantId = 101, CampusId = campus.Id, VehicleNumber = "BUS", Capacity = 5 };
        db.AddRange(enrollment, route, vehicle);
        await db.SaveChangesAsync();
        db.Add(new RouteVehicleAssignment { TenantId = 101, RouteId = route.Id, VehicleId = vehicle.Id,
            IsCurrent = true, EffectiveFrom = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)) });
        await db.SaveChangesAsync();
        var result = await CreateService(db, 101).AssignAsync(new AssignStudentTransportRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = enrollment.PublicId,
            RouteReference = route.PublicId, VehicleReference = vehicle.PublicId
        });
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        (await db.Set<StudentTransport>().CountAsync()).Should().Be(0);
    }

    private static TransportService CreateService(EduOSDbContext context, long tenant, bool canManage = true, string? role = null) => new(
        new GenericRepository<TransportRoute>(context),
        new GenericRepository<RouteStop>(context),
        new GenericRepository<Vehicle>(context),
        new GenericRepository<RouteVehicleAssignment>(context),
        new GenericRepository<StudentTransport>(context),
        new GenericRepository<StudentEnrollment>(context),
        new GenericRepository<Student>(context),
        new GenericRepository<UserCampusAccess>(context),
        new GenericRepository<Campus>(context),
        new TestUnitOfWork(context),
        new TestCurrentUser(tenant, canManage, role), TimeProvider.System,
        NullLogger<TransportService>.Instance,
        new GenericRepository<TransportDriver>(context),
        new GenericRepository<VehicleDriverAssignment>(context),
        new ReservationFinancePolicy(context),
        new EduOS.Service.Services.Platform.ReservationCommandReceiptStore(context, TimeProvider.System));

    private static async Task<StudentTransport> SeedAssignmentAsync(EduOSDbContext context, long tenant, bool active, byte[] rowVersion)
    {
        var route = new TransportRoute
        {
            TenantId = tenant, PublicId = Guid.NewGuid(), Name = "Route A", Code = "A",
            DefaultFare = 1500m, IsActive = true
        };
        var vehicle = new Vehicle
        {
            TenantId = tenant, PublicId = Guid.NewGuid(), VehicleNumber = "BUS-01",
            VehicleType = "Bus", Capacity = 30, IsActive = true
        };
        var student = new Student
        {
            TenantId = tenant, PublicId = Guid.NewGuid(), PersonId = 1,
            StudentCode = "STU-" + Guid.NewGuid().ToString("N"), FullName = "Transport Student",
            StatusCode = "Active", AdmissionDate = DateOnly.FromDateTime(DateTime.Today)
        };
        var campus = new Campus { TenantId = tenant, Name = "Transport Campus", Code = "TRANSPORT", IsActive = true };
        context.AddRange(route, vehicle, student, campus);
        await context.SaveChangesAsync();
        var enrollment = new StudentEnrollment
        {
            TenantId = tenant, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(),
            StudentId = student.Id, CampusId = campus.Id, AcademicYearId = 1,
            AcademicProgramId = 1, AcademicLevelId = 1, AcademicBatchId = 1,
            AcademicCurriculumId = 1, RollNo = "7", IsCurrent = true,
            State = EnrollmentState.Active, EnrollmentDate = DateOnly.FromDateTime(DateTime.Today)
        };
        context.Add(enrollment);
        await context.SaveChangesAsync();
        var assignment = new StudentTransport
        {
            TenantId = tenant, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(),
            StudentId = student.Id, StudentEnrollmentId = enrollment.Id, VehicleId = vehicle.Id,
            RouteId = route.Id, RouteVehicleAssignmentId = 1,
            StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-10)),
            EndDate = active ? null : DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
            MonthlyFare = route.DefaultFare,
            State = active ? TransportAssignmentState.Active : TransportAssignmentState.Closed,
            RowVersion = rowVersion
        };
        context.Set<StudentTransport>().Add(assignment);
        await context.SaveChangesAsync();
        return assignment;
    }

    private static DbContextOptions<EduOSDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("transport-service-" + Guid.NewGuid().ToString("N")).Options;

    private static EduOSDbContext CreateContext(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenant.ToString())
            ], "TestAuthentication"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class TestUnitOfWork(EduOSDbContext context) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
        // Test-only EF InMemory provider has no relational isolation level.
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation, System.Data.IsolationLevel isolationLevel,
            CancellationToken cancellationToken = default) => operation(cancellationToken);

        public void Dispose() { }
    }

    private sealed class TestCurrentUser(long tenant, bool canManage, string? role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => "Transport User";
        public string? Email => "transport@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => canManage && (role is null or "TenantAdmin");
        public IReadOnlyList<string> Roles => canManage ? [role ?? "TenantAdmin"] : ["Student"];
        public bool IsInRole(string requestedRole) => canManage && requestedRole == (role ?? "TenantAdmin");
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
