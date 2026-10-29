using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Transport;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportVehicleCaseTests
{
    [Fact]
    public async Task Case_create_is_idempotent_and_reused_key_with_different_payload_conflicts()
    {
        await using var db = Context(101);
        var vehicle = await SeedVehicle(db, 101);
        var service = Service(db, new User(101, 42, "TenantAdmin"));
        var request = NewCase(vehicle.PublicId);
        var first = await service.CreateAsync(request);
        first.StatusCode.Should().Be(201);
        var replay = await service.CreateAsync(request);
        replay.Success.Should().BeTrue(replay.Message);
        replay.Data!.Reference.Should().Be(first.Data!.Reference);
        db.TransportVehicleCases.Count().Should().Be(1);
        request.Summary = "Different description";
        (await service.CreateAsync(request)).StatusCode.Should().Be(409);
        db.TransportVehicleCases.Count().Should().Be(1);
    }

    [Fact]
    public async Task Cross_tenant_and_unauthorized_campus_cannot_read_create_or_export()
    {
        await using var db = Context(101);
        var vehicle = await SeedVehicle(db, 101);
        var admin = Service(db, new User(101, 42, "TenantAdmin"));
        (await admin.CreateAsync(NewCase(vehicle.PublicId))).Success.Should().BeTrue();
        var otherTenant = Service(db, new User(202, 42, "TenantAdmin"));
        (await otherTenant.GetAsync(null, "All", null, 1, 25)).Data!.Items.Should().BeEmpty();
        (await otherTenant.CreateAsync(NewCase(vehicle.PublicId))).StatusCode.Should().Be(404);
        (await otherTenant.ExportCsvAsync(null, "All", null)).Data.Should().NotContain("BUS-101");
        var manager = Service(db, new User(101, 99, "TransportManager"));
        (await manager.GetAsync(null, "All", null, 1, 25)).Data!.Items.Should().BeEmpty();
        (await manager.CreateAsync(NewCase(vehicle.PublicId))).StatusCode.Should().Be(403);
        var student = Service(db, new User(101, 42, "Student"));
        (await student.GetAsync(null, "All", null, 1, 25)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Case_export_escapes_spreadsheet_formula_and_filters_by_state()
    {
        await using var db = Context(101);
        var vehicle = await SeedVehicle(db, 101);
        var service = Service(db, new User(101, 42, "TenantAdmin"));
        var request = NewCase(vehicle.PublicId);
        request.Summary = "=HYPERLINK(\"https://example.test\")";
        (await service.CreateAsync(request)).Success.Should().BeTrue();
        var csv = await service.ExportCsvAsync(null, "Open", null);
        csv.Success.Should().BeTrue(csv.Message);
        csv.Data.Should().Contain("'=HYPERLINK");
        (await service.GetAsync(null, "Resolved", null, 1, 25)).Data!.Items.Should().BeEmpty();
        (await service.GetAsync(null, "INVALID", null, 1, 25)).StatusCode.Should().Be(400);
        (await service.GetAsync(null, "All", null, 0, 25)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Case_resolution_requires_current_rowversion_and_preserves_original_details()
    {
        await using var db = Context(101);
        var vehicle = await SeedVehicle(db, 101);
        var service = Service(db, new User(101, 42, "TenantAdmin"));
        var created = await service.CreateAsync(NewCase(vehicle.PublicId));
        var item = await db.TransportVehicleCases.SingleAsync();
        item.RowVersion = new byte[] {1,2,3,4,5,6,7,8};
        await db.SaveChangesAsync();
        var bad = await service.ResolveAsync(created.Data!.Reference, new ResolveTransportVehicleCaseRequestDto
        { RowVersion = Convert.ToBase64String(new byte[8]), Resolution = "Inspected" });
        bad.StatusCode.Should().Be(409);
        var resolved = await service.ResolveAsync(created.Data.Reference, new ResolveTransportVehicleCaseRequestDto
        { RowVersion = Convert.ToBase64String(item.RowVersion), Resolution = "Inspected and repaired" });
        resolved.Success.Should().BeTrue(resolved.Message);
        resolved.Data!.State.Should().Be("Resolved");
        resolved.Data.Summary.Should().Be(created.Data.Summary);
        (await service.ResolveAsync(created.Data.Reference, new ResolveTransportVehicleCaseRequestDto
        { RowVersion = Convert.ToBase64String(item.RowVersion), Resolution = "Again" })).StatusCode.Should().Be(409);
    }

    [Fact]
    public void Case_model_has_tenant_scoped_keys_and_vehicle_campus_foreign_keys()
    {
        using var db = Context(101);
        var entity = db.Model.FindEntityType(typeof(TransportVehicleCase));
        entity.Should().NotBeNull();
        entity!.GetIndexes().Should().Contain(x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] {"TenantId","ClientRequestId"}));
        entity.GetForeignKeys().Should().Contain(x => x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] {"TenantId","VehicleId"}));
        entity.GetForeignKeys().Should().Contain(x => x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] {"TenantId","CampusId"}));
    }


    [Fact]
    public async Task Safety_impact_is_tenant_scoped_and_open_cases_hold_reserved_vehicle()
    {
        await using var db = Context(101);
        var vehicle = await SeedVehicle(db, 101);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        db.StudentTransports.Add(new StudentTransport
        {
            TenantId = 101, VehicleId = vehicle.Id, StudentId = 1,
            StudentEnrollmentId = 1, RouteId = 1, ClientRequestId = Guid.NewGuid(),
            StartDate = today, State = EduOS.Core.Enums.Domain.TransportAssignmentState.Active
        });
        db.TransportVehicleCases.Add(new TransportVehicleCase
        {
            TenantId = 101, VehicleId = vehicle.Id, CampusId = vehicle.CampusId!.Value,
            VehicleNumberSnapshot = vehicle.VehicleNumber, ClientRequestId = Guid.NewGuid(),
            OccurredOn = today, CaseType = "Incident", Severity = "High",
            Summary = "Vehicle not cleared", State = "Open"
        });
        await db.SaveChangesAsync();
        var admin = Service(db, new User(101, 42, "TenantAdmin"));
        var impact = await admin.GetSafetyImpactAsync(today, null, null, true, 1, 25);
        impact.Success.Should().BeTrue(impact.Message);
        var row = impact.Data!.Items.Single();
        row.ReservedRiders.Should().Be(1);
        row.OpenCases.Should().Be(1);
        row.HighSeverityCases.Should().Be(1);
        row.SafetyState.Should().Be("SafetyHold");
        var csv = await admin.ExportSafetyImpactCsvAsync(today, null, null, true);
        csv.Data.Should().Contain("BUS-101");
        csv.Data.Should().Contain("SafetyHold");
        (await Service(db, new User(202, 42, "TenantAdmin"))
            .GetSafetyImpactAsync(today, null, null, false, 1, 25))
            .Data!.Items.Should().BeEmpty();
        (await Service(db, new User(101, 99, "TransportManager"))
            .GetSafetyImpactAsync(today, null, null, false, 1, 25))
            .Data!.Items.Should().BeEmpty();
        (await Service(db, new User(101, 42, "Student"))
            .GetSafetyImpactAsync(today, null, null, false, 1, 25))
            .StatusCode.Should().Be(403);
        (await admin.GetSafetyImpactAsync(today.AddDays(91), null, null, false, 1, 25))
            .StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Safety_impact_retains_vehicle_hold_after_campus_case_mismatch_without_exposing_case_details()
    {
        await using var db = Context(101);
        var vehicle = await SeedVehicle(db, 101);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        db.TransportVehicleCases.Add(new TransportVehicleCase
        {
            TenantId = 101, VehicleId = vehicle.Id, CampusId = vehicle.CampusId!.Value + 999,
            VehicleNumberSnapshot = vehicle.VehicleNumber, ClientRequestId = Guid.NewGuid(),
            OccurredOn = today, CaseType = "Maintenance", Severity = "Medium",
            Summary = "Private old campus issue", State = "Open"
        });
        await db.SaveChangesAsync();
        var impact = await Service(db, new User(101, 42, "TenantAdmin"))
            .GetSafetyImpactAsync(today, null, null, true, 1, 25);
        var row = impact.Data!.Items.Single();
        row.CrossCampusCaseHold.Should().BeTrue();
        row.SafetyState.Should().Be("SafetyHold");
        var csv = await Service(db, new User(101, 42, "TenantAdmin"))
            .ExportSafetyImpactCsvAsync(today, null, null, true);
        csv.Data.Should().NotContain("Private old campus issue");
    }

    private static CreateTransportVehicleCaseRequestDto NewCase(Guid vehicle) => new()
    {
        ClientRequestId = Guid.NewGuid(), VehicleReference = vehicle,
        OccurredOn = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime),
        CaseType = "Maintenance", Severity = "Medium", Summary = "Brake inspection required"
    };

    private static TransportVehicleCaseService Service(EduOSDbContext db, ICurrentUserService user) =>
        new(db, user, TimeProvider.System, NullLogger<TransportVehicleCaseService>.Instance);

    private static EduOSDbContext Context(long tenant)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("transport-vehicle-cases-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "42"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenant.ToString())
            ], "TestAuthentication"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static async Task<Vehicle> SeedVehicle(EduOSDbContext db, long tenant)
    {
        var campus = new Campus { TenantId = tenant, Name = "Transport campus", Code = "TC", IsActive = true };
        db.Campuses.Add(campus);
        await db.SaveChangesAsync();
        var vehicle = new Vehicle
        {
            TenantId = tenant, CampusId = campus.Id, VehicleNumber = "BUS-101", Capacity = 30, IsActive = true
        };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        return vehicle;
    }

    private sealed class User(long tenant, long id, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => id;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
