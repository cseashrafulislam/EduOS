using EduOS.Core.DTOs.Hostel;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Executable V2/V1 contract on the disposable SQL Server used by the migration smoke:
/// the canonical V1 student/enrollment/campus and finance state controls BOTH V2
/// Hostel and Transport. The tests share the production services and SQL fixtures
/// of SqlServerTransportSeatCapacityIntegrationTests rather than mock a second model.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task V2_reservations_reject_foreign_tenant_academic_enrollment_and_resources_on_sql_server()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var first = await SeedAsync(connection);
        var second = await SeedAsync(connection);
        var ownBeds = await SeedHostelBedsAsync(first, 1, 1);
        var foreignBeds = await SeedHostelBedsAsync(second, 1, 1);
        var through = first.Start.AddDays(2);

        var foreignEnrollmentHostel = HostelRequest(first, 0, ownBeds[0], first.Start, through);
        foreignEnrollmentHostel.StudentEnrollmentReference = second.Enrollments[0];
        var foreignEnrollmentTransport = Request(first, 0, first.Start, through);
        foreignEnrollmentTransport.StudentEnrollmentReference = second.Enrollments[0];
        Assert.Equal(404, (await HostelBookAsync(first, foreignEnrollmentHostel)).StatusCode);
        Assert.Equal(404, (await AssignAsync(first, foreignEnrollmentTransport)).StatusCode);

        var foreignBed = HostelRequest(first, 0, foreignBeds[0], first.Start, through);
        var foreignRoute = Request(first, 0, first.Start, through);
        foreignRoute.RouteReference = second.RouteReference;
        var foreignVehicle = Request(first, 0, first.Start, through);
        foreignVehicle.VehicleReference = second.VehicleReference;
        Assert.Equal(404, (await HostelBookAsync(first, foreignBed)).StatusCode);
        Assert.Equal(404, (await AssignAsync(first, foreignRoute)).StatusCode);
        Assert.Equal(404, (await AssignAsync(first, foreignVehicle)).StatusCode);

        // IHttpContextAccessor is AsyncLocal. Do not keep two different
        // tenant HttpContexts alive in one async flow: constructing the second
        // test context would replace the ambient tenant of the first query.
        await using (var own = ScopedContext(first.Options, first.TenantId, first.UserId))
        {
            Assert.Equal(0, await own.StudentHostelAllocations.CountAsync());
            Assert.Equal(0, await own.StudentTransports.CountAsync());
            Assert.Equal(2, (await HostelServiceFor(own, first)
                .GetEligibleStudentsAsync(1, 25, null, startDate: first.Start, endDate: through))
                .Data!.TotalCount);
        }
        await using (var other = ScopedContext(second.Options, second.TenantId, second.UserId))
        {
            Assert.Equal(0, await other.StudentHostelAllocations.CountAsync());
            Assert.Equal(0, await other.StudentTransports.CountAsync());
            Assert.Equal(2, (await Service(other, second.TenantId, second.UserId)
                .GetEligibleStudentsAsync(1, 25, null, second.Start, second.Start.AddDays(2)))
                .Data!.TotalCount);
        }
    }

    [Fact]
    public async Task V1_student_exit_revokes_new_V2_reservations_but_does_not_block_other_learner()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var fixture = await SeedAsync(connection);
        var beds = await SeedHostelBedsAsync(fixture, 1, 1);
        var through = fixture.Start.AddDays(2);
        long exitedEnrollmentId;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var enrollment = await db.StudentEnrollments.SingleAsync(x =>
                x.PublicId == fixture.Enrollments[0]);
            exitedEnrollmentId = enrollment.Id;
            enrollment.IsCurrent = false;
            enrollment.State = EnrollmentState.Withdrawn;
            var student = await db.Students.SingleAsync(x => x.Id == enrollment.StudentId);
            student.StatusCode = "Transferred";
            await db.SaveChangesAsync();
        }

        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var hostelOptions = await HostelServiceFor(db, fixture)
                .GetEligibleStudentsAsync(1, 25, null, startDate: fixture.Start, endDate: through);
            var transportOptions = await Service(db, fixture.TenantId, fixture.UserId)
                .GetEligibleStudentsAsync(1, 25, null, fixture.Start, through);
            Assert.True(hostelOptions.Success, hostelOptions.Message);
            Assert.True(transportOptions.Success, transportOptions.Message);
            Assert.Equal(fixture.Enrollments[1], Assert.Single(hostelOptions.Data!.Items).EnrollmentReference);
            Assert.Equal(fixture.Enrollments[1], Assert.Single(transportOptions.Data!.Items).EnrollmentReference);
        }

        Assert.Equal(404, (await HostelBookAsync(fixture,
            HostelRequest(fixture, 0, beds[0], fixture.Start, through))).StatusCode);
        Assert.Equal(404, (await AssignAsync(fixture,
            Request(fixture, 0, fixture.Start, through))).StatusCode);

        var hostel = await HostelBookAsync(fixture,
            HostelRequest(fixture, 1, beds[0], fixture.Start, through));
        var transport = await AssignAsync(fixture,
            Request(fixture, 1, fixture.Start, through));
        Assert.True(hostel.Success, hostel.Message);
        Assert.True(transport.Success, transport.Message);

        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var activeEnrollmentId = await verify.StudentEnrollments
            .Where(x => x.PublicId == fixture.Enrollments[1]).Select(x => x.Id).SingleAsync();
        Assert.NotEqual(exitedEnrollmentId, activeEnrollmentId);
        Assert.Equal(activeEnrollmentId, (await verify.StudentHostelAllocations
            .SingleAsync()).StudentEnrollmentId);
        Assert.Equal(activeEnrollmentId, (await verify.StudentTransports
            .SingleAsync()).StudentEnrollmentId);
    }

    [Fact]
    public async Task V1_campus_deactivation_blocks_both_V2_pickers_and_mutations_then_recovers()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var fixture = await SeedAsync(connection);
        var beds = await SeedHostelBedsAsync(fixture, 1, 1);
        var through = fixture.Start.AddDays(2);
        long campusId;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            campusId = await db.StudentEnrollments.Where(x =>
                x.PublicId == fixture.Enrollments[0]).Select(x => x.CampusId).SingleAsync();
            var campus = await db.Campuses.SingleAsync(x => x.Id == campusId);
            campus.IsActive = false;
            await db.SaveChangesAsync();
        }

        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            Assert.Equal(0, (await HostelServiceFor(db, fixture)
                .GetEligibleStudentsAsync(1, 25, null, startDate: fixture.Start, endDate: through))
                .Data!.TotalCount);
            Assert.Equal(0, (await Service(db, fixture.TenantId, fixture.UserId)
                .GetEligibleStudentsAsync(1, 25, null, fixture.Start, through))
                .Data!.TotalCount);
        }
        Assert.Equal(409, (await HostelBookAsync(fixture,
            HostelRequest(fixture, 0, beds[0], fixture.Start, through))).StatusCode);
        Assert.Equal(409, (await AssignAsync(fixture,
            Request(fixture, 0, fixture.Start, through))).StatusCode);

        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var campus = await db.Campuses.SingleAsync(x => x.Id == campusId);
            campus.IsActive = true;
            await db.SaveChangesAsync();
        }
        var hostel = await HostelBookAsync(fixture,
            HostelRequest(fixture, 0, beds[0], fixture.Start, through));
        var transport = await AssignAsync(fixture,
            Request(fixture, 0, fixture.Start, through));
        Assert.True(hostel.Success, hostel.Message);
        Assert.True(transport.Success, transport.Message);
    }

    [Fact]
    public async Task V1_issued_and_historically_paid_invoice_prevents_both_V2_closures()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var fixture = await SeedAsync(connection);
        var beds = await SeedHostelBedsAsync(fixture, 1, 1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var through = today.AddDays(1);
        long enrollmentId;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var link = await db.RouteVehicleAssignments.SingleAsync();
            link.EffectiveFrom = today.AddDays(-1);
            enrollmentId = await db.StudentEnrollments.Where(x =>
                x.PublicId == fixture.Enrollments[0]).Select(x => x.Id).SingleAsync();
            await db.SaveChangesAsync();
        }

        var hostel = await HostelBookAsync(fixture,
            HostelRequest(fixture, 0, beds[0], today, through));
        var transport = await AssignAsync(fixture,
            Request(fixture, 0, today, through));
        Assert.True(hostel.Success, hostel.Message);
        Assert.True(transport.Success, transport.Message);

        long invoiceId;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var invoice = new StudentInvoice
            {
                TenantId = fixture.TenantId, StudentEnrollmentId = enrollmentId,
                InvoiceNumber = "V2-FIN-" + Guid.NewGuid().ToString("N")[..12],
                InvoiceDate = today, DueDate = through, State = InvoiceState.Issued,
                TotalAmount = 100m, DueAmount = 100m
            };
            db.StudentInvoices.Add(invoice);
            await db.SaveChangesAsync();
            invoiceId = invoice.Id;
        }

        async Task AssertFinanceBlocksClosuresAsync()
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            var hostelResult = await HostelServiceFor(db, fixture).CloseAsync(
                hostel.Data!.Id, new CloseStudentHostelAllocationRequestDto
                { EndDate = today, RowVersion = hostel.Data.RowVersion });
            var transportResult = await Service(db, fixture.TenantId, fixture.UserId).CloseAsync(
                transport.Data!.Reference, new CloseStudentTransportRequestDto
                { EndDate = today, RowVersion = transport.Data.RowVersion });
            Assert.Equal(409, hostelResult.StatusCode);
            Assert.Equal(409, transportResult.StatusCode);
        }

        await AssertFinanceBlocksClosuresAsync();
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var invoice = await db.StudentInvoices.SingleAsync(x => x.Id == invoiceId);
            invoice.IsDeleted = true;
            invoice.State = InvoiceState.Paid;
            invoice.PaidAmount = 100m;
            invoice.DueAmount = 0m;
            await db.SaveChangesAsync();
        }
        await AssertFinanceBlocksClosuresAsync();

        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var savedHostel = await verify.StudentHostelAllocations.SingleAsync();
        var savedTransport = await verify.StudentTransports.SingleAsync();
        Assert.Equal(HostelAllocationState.Active, savedHostel.State);
        Assert.Equal(TransportAssignmentState.Active, savedTransport.State);
        Assert.Equal(through, savedHostel.EndDate);
        Assert.Equal(through, savedTransport.EndDate);
        Assert.Equal(1, await verify.StudentInvoices.IgnoreQueryFilters()
            .CountAsync(x => x.TenantId == fixture.TenantId && x.Id == invoiceId &&
                x.IsDeleted && x.State == InvoiceState.Paid && x.PaidAmount == 100m));
    }
}
