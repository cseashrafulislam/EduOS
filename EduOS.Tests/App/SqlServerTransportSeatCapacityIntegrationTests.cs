using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Finance;
using EduOS.Service.Services.Platform;
using EduOS.Service.Services.Transport;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Claims;
using Xunit;
using TransportRoute = EduOS.Core.Entities.Transport.Route;

namespace EduOS.Tests.App;

/// <summary>
/// Operations-owned SQL Server tests of the real rider command, not just an index
/// or a pure interval helper. The service's serializable capacity check must
/// survive two independent DbContexts racing for the last physical seat.
/// </summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerTransportSeatCapacityIntegrationTests
{
    private static readonly ConcurrentQueue<string> Diagnostics = new();
    [Fact]
    public async Task Two_concurrent_future_riders_cannot_reserve_the_same_last_seat()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var first = Request(fixture, 0, fixture.Start, fixture.Start.AddDays(6));
        var second = Request(fixture, 1, fixture.Start, fixture.Start.AddDays(6));

        // Separate contexts and connections: EF InMemory cannot establish this guarantee.
        var responses = await Task.WhenAll(AssignAsync(fixture, first), AssignAsync(fixture, second));
        Assert.True(responses.Any(x => x.Success), "No reservation succeeded. " + string.Join("\n", Diagnostics));
        var accepted = Assert.Single(responses, x => x.Success);
        Assert.Single(responses, x => !x.Success && x.StatusCode == 409);

        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var active = await verify.StudentTransports.AsNoTracking().Where(x =>
                x.TenantId == fixture.TenantId && x.VehicleId == fixture.VehicleId &&
                !x.IsDeleted && x.State == TransportAssignmentState.Active).ToListAsync();
            Assert.Single(active);
            Assert.Equal(accepted.Data!.Reference, active[0].PublicId);
        }

        // The winner's durable client request ID replays the same allocation,
        // without consuming another seat or rewriting the original reservation.
        var winningRequest = responses[0].Success ? first : second;
        var replay = await AssignAsync(fixture, winningRequest);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(accepted.Data!.Reference, replay.Data!.Reference);
        await using var final = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        Assert.Equal(1, await final.StudentTransports.CountAsync(x =>
            x.TenantId == fixture.TenantId && x.VehicleId == fixture.VehicleId &&
            !x.IsDeleted && x.State == TransportAssignmentState.Active));
    }

    [Fact]
    public async Task Disjoint_future_periods_can_share_one_seat_without_false_overcapacity()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var first = await AssignAsync(fixture, Request(fixture, 0,
            fixture.Start, fixture.Start.AddDays(4)));
        Assert.True(first.Success, first.Message + "\n" + string.Join("\n", Diagnostics));
        var second = await AssignAsync(fixture, Request(fixture, 1,
            fixture.Start.AddDays(5), fixture.Start.AddDays(9)));
        Assert.True(second.Success, second.Message + "\n" + string.Join("\n", Diagnostics));

        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        Assert.Equal(2, await verify.StudentTransports.CountAsync(x =>
            x.TenantId == fixture.TenantId && x.VehicleId == fixture.VehicleId &&
            !x.IsDeleted && x.State == TransportAssignmentState.Active));
    }

    private static AssignStudentTransportRequestDto Request(
        Fixture fixture, int student, DateOnly from, DateOnly to) => new()
    {
        ClientRequestId = Guid.NewGuid(),
        StudentEnrollmentReference = fixture.Enrollments[student],
        RouteReference = fixture.RouteReference,
        VehicleReference = fixture.VehicleReference,
        StartDate = from,
        EndDate = to,
        MonthlyFare = 0m
    };

    private static async Task<ApiResponse<StudentTransportDto>> AssignAsync(
        Fixture fixture, AssignStudentTransportRequestDto request)
    {
        await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        return await Service(db, fixture.TenantId, fixture.UserId).AssignAsync(request);
    }

    private static TransportService Service(EduOSDbContext db, long tenant, long user) => new(
        new GenericRepository<TransportRoute>(db),
        new GenericRepository<RouteStop>(db),
        new GenericRepository<Vehicle>(db),
        new GenericRepository<RouteVehicleAssignment>(db),
        new GenericRepository<StudentTransport>(db),
        new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Student>(db),
        new GenericRepository<UserCampusAccess>(db),
        new GenericRepository<Campus>(db),
        db,
        new TenantAdmin(tenant, user),
        TimeProvider.System,
        new CaptureLogger(),
        new ReservationFinancePolicy(db),
        new ReservationCommandReceiptStore(db, TimeProvider.System));

    private static async Task<Fixture> SeedAsync(string connection)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        await using var db = new EduOSDbContext(options);
        var tenant = new Tenant
        {
            Code = "CI-CAP-" + suffix, Name = "Transport seat concurrency CI",
            Email = "cap-" + suffix + "@example.test", State = TenantState.Active
        };
        var user = new ApplicationUser
        {
            UserName = "cap-" + suffix, NormalizedUserName = ("cap-" + suffix).ToUpperInvariant(),
            Email = "cap-" + suffix + "@example.test",
            NormalizedEmail = ("cap-" + suffix + "@example.test").ToUpperInvariant(),
            FullName = "Transport capacity tester", EmailConfirmed = true, IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        var personA = new Person { FullName = "Capacity CI student A" };
        var personB = new Person { FullName = "Capacity CI student B" };
        db.AddRange(tenant, user, personA, personB);
        await db.SaveChangesAsync();

        using (db.BeginSystemTenantScope(tenant.Id))
        {
            var campus = new Campus
            {
                TenantId = tenant.Id, Code = "CAP-" + suffix,
                Name = "Capacity test campus", IsActive = true
            };
            var year = new AcademicYear
            {
                TenantId = tenant.Id, Name = "Capacity year", Code = "CY-" + suffix,
                StartDate = start.AddDays(-60), EndDate = start.AddDays(365),
                IsActive = true
            };
            var program = new AcademicProgram
            {
                TenantId = tenant.Id, Name = "Capacity program", Code = "CP-" + suffix
            };
            var route = new TransportRoute
            {
                TenantId = tenant.Id, Name = "Capacity route", Code = "CR-" + suffix,
                DefaultFare = 0m, IsActive = true
            };
            var vehicle = new Vehicle
            {
                TenantId = tenant.Id, VehicleNumber = "CAP-BUS-" + suffix,
                Capacity = 1, IsActive = true
            };
            var students = new[]
            {
                new Student
                {
                    TenantId = tenant.Id, PersonId = personA.Id,
                    StudentCode = "CAP-A-" + suffix, FullName = "Capacity student A",
                    AdmissionDate = start.AddDays(-30), StatusCode = "Active"
                },
                new Student
                {
                    TenantId = tenant.Id, PersonId = personB.Id,
                    StudentCode = "CAP-B-" + suffix, FullName = "Capacity student B",
                    AdmissionDate = start.AddDays(-30), StatusCode = "Active"
                }
            };
            db.AddRange(campus, year, program, route, vehicle);
            db.Students.AddRange(students);
            await db.SaveChangesAsync();
            route.CampusId = campus.Id;
            vehicle.CampusId = campus.Id;
            var level = new AcademicLevel
            {
                TenantId = tenant.Id, AcademicProgramId = program.Id,
                Name = "Capacity level", Code = "CL-" + suffix, LevelNo = 1
            };
            var curriculum = new AcademicCurriculum
            {
                TenantId = tenant.Id, AcademicProgramId = program.Id,
                Name = "Capacity curriculum", Code = "CC-" + suffix,
                EffectiveFrom = start.AddDays(-90), IsCurrent = true, IsActive = true
            };
            db.AddRange(level, curriculum);
            await db.SaveChangesAsync();
            var batch = new AcademicBatch
            {
                TenantId = tenant.Id, CampusId = campus.Id, AcademicYearId = year.Id,
                AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                Name = "Capacity batch", Code = "CB-" + suffix,
                Capacity = 30, IsActive = true
            };
            db.AcademicBatches.Add(batch);
            await db.SaveChangesAsync();
            var enrollments = students.Select((student, index) => new StudentEnrollment
            {
                TenantId = tenant.Id, ClientRequestId = Guid.NewGuid(),
                StudentId = student.Id, CampusId = campus.Id,
                AcademicYearId = year.Id, AcademicProgramId = program.Id,
                AcademicLevelId = level.Id, AcademicBatchId = batch.Id,
                AcademicCurriculumId = curriculum.Id,
                RollNo = (index + 1).ToString(), EnrollmentDate = start.AddDays(-30),
                IsCurrent = true, State = EnrollmentState.Active
            }).ToArray();
            db.StudentEnrollments.AddRange(enrollments);
            db.RouteVehicleAssignments.Add(new RouteVehicleAssignment
            {
                TenantId = tenant.Id, RouteId = route.Id, VehicleId = vehicle.Id,
                EffectiveFrom = start.AddDays(-1), EffectiveTo = start.AddDays(30),
                IsCurrent = true
            });
            await db.SaveChangesAsync();
            return new Fixture(options, tenant.Id, user.Id, vehicle.Id,
                route.PublicId, vehicle.PublicId,
                enrollments.Select(x => x.PublicId).ToArray(), start);
        }
    }

    private static EduOSDbContext ScopedContext(
        DbContextOptions<EduOSDbContext> options, long tenant, long user)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.ToString()),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenant.ToString())
            }, "TestAuthentication"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class CaptureLogger : ILogger<TransportService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception != null) Diagnostics.Enqueue(exception.ToString());
        }
    }

    private sealed record Fixture(DbContextOptions<EduOSDbContext> Options,
        long TenantId, long UserId, long VehicleId,
        Guid RouteReference, Guid VehicleReference,
        Guid[] Enrollments, DateOnly Start);

    private sealed class TenantAdmin(long tenant, long user) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => user;
        public long TenantId => tenant;
        public string? FullName => "Transport capacity tester";
        public string? Email => "capacity@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL integration tests";
    }
}
