using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Academic;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class SubjectOfferingSetupTests
{
    [Fact]
    public async Task Batch_offering_can_be_created_listed_and_retried_without_duplicates()
    {
        var options = Options();
        await using var db = Context(options, 301);
        var ids = await SeedAsync(db, 301);
        var sut = Service(db, 301);
        var request = Request(ids);
        var first = await sut.CreateSubjectOfferingAsync(request);
        var replay = await sut.CreateSubjectOfferingAsync(request);
        first.StatusCode.Should().Be(201);
        replay.Success.Should().BeTrue();
        replay.Data!.Id.Should().Be(first.Data!.Id);
        (await db.SubjectOfferings.CountAsync()).Should().Be(1);
        var list = await sut.GetSubjectOfferingsAsync(ids.Batch, "MATH", 1, 20);
        list.Data!.Items.Should().ContainSingle(x => x.Id == first.Data.Id);
        var duplicate = Request(ids);
        duplicate.Code = "DIFFERENT";
        (await sut.CreateSubjectOfferingAsync(duplicate)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Cross_tenant_and_mismatched_academic_year_are_rejected()
    {
        var options = Options();
        await using (var db = Context(options, 301))
        {
            var ids = await SeedAsync(db, 301);
            var wrong = Request(ids);
            wrong.AcademicYearId++;
            (await Service(db, 301).CreateSubjectOfferingAsync(wrong)).StatusCode.Should().Be(409);
        }
        await using var other = Context(options, 302);
        var foreign = await Service(other, 302).GetSubjectOfferingsAsync(1, null);
        foreign.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Registered_offerings_cannot_be_deactivated_or_shrunk_below_enrollment()
    {
        var options = Options();
        await using var db = Context(options, 301);
        var ids = await SeedAsync(db, 301);
        var sut = Service(db, 301);
        var created = (await sut.CreateSubjectOfferingAsync(Request(ids))).Data!;
        var row = await db.SubjectOfferings.SingleAsync();
        row.RowVersion = [1,2,3,4,5,6,7,8];
        await db.SaveChangesAsync();
        db.Add(new StudentSubjectRegistration
        {
            TenantId = 301, SubjectOfferingId = row.Id, StudentEnrollmentId = 1,
            ClientRequestId = Guid.NewGuid(), State = EduOS.Core.Enums.Domain.SubjectRegistrationState.Approved
        });
        await db.SaveChangesAsync();
        var version = Convert.ToBase64String(row.RowVersion);
        (await sut.UpdateSubjectOfferingAsync(created.Reference, new UpdateSubjectOfferingRequestDto
        { Capacity = 40, IsActive = false, RowVersion = version })).StatusCode.Should().Be(409);
        (await sut.UpdateSubjectOfferingAsync(created.Reference, new UpdateSubjectOfferingRequestDto
        { Capacity = 1, IsActive = true, RowVersion = version })).Success.Should().BeTrue();
        // A further registration would make capacity 1 insufficient.
        db.Add(new StudentSubjectRegistration
        {
            TenantId = 301, SubjectOfferingId = row.Id, StudentEnrollmentId = 2,
            ClientRequestId = Guid.NewGuid(), State = EduOS.Core.Enums.Domain.SubjectRegistrationState.Approved
        });
        await db.SaveChangesAsync();
        (await sut.UpdateSubjectOfferingAsync(created.Reference, new UpdateSubjectOfferingRequestDto
        { Capacity = 1, IsActive = false, RowVersion = version })).StatusCode.Should().Be(409);
    }


    [Fact]
    public async Task Offering_register_reports_pending_allocated_and_released_seats()
    {
        var options = Options();
        await using var db = Context(options, 301);
        var ids = await SeedAsync(db, 301);
        var sut = Service(db, 301);
        var created = (await sut.CreateSubjectOfferingAsync(Request(ids))).Data!;
        db.AddRange(
            new StudentSubjectRegistration { TenantId = 301, SubjectOfferingId = created.Id,
                StudentEnrollmentId = 1, ClientRequestId = Guid.NewGuid(),
                State = EduOS.Core.Enums.Domain.SubjectRegistrationState.Pending },
            new StudentSubjectRegistration { TenantId = 301, SubjectOfferingId = created.Id,
                StudentEnrollmentId = 2, ClientRequestId = Guid.NewGuid(),
                State = EduOS.Core.Enums.Domain.SubjectRegistrationState.Approved },
            new StudentSubjectRegistration { TenantId = 301, SubjectOfferingId = created.Id,
                StudentEnrollmentId = 3, ClientRequestId = Guid.NewGuid(),
                State = EduOS.Core.Enums.Domain.SubjectRegistrationState.Completed },
            new StudentSubjectRegistration { TenantId = 301, SubjectOfferingId = created.Id,
                StudentEnrollmentId = 4, ClientRequestId = Guid.NewGuid(),
                State = EduOS.Core.Enums.Domain.SubjectRegistrationState.Rejected },
            new StudentSubjectRegistration { TenantId = 301, SubjectOfferingId = created.Id,
                StudentEnrollmentId = 5, ClientRequestId = Guid.NewGuid(),
                State = EduOS.Core.Enums.Domain.SubjectRegistrationState.Dropped });
        await db.SaveChangesAsync();
        var result = await sut.GetSubjectOfferingsAsync(ids.Batch, null);
        result.Success.Should().BeTrue();
        var offering = result.Data!.Items.Should().ContainSingle().Subject;
        offering.PendingSeats.Should().Be(1);
        offering.AllocatedSeats.Should().Be(2);
        offering.ReservedSeats.Should().Be(3);
        offering.SeatsRemaining.Should().Be(37);
        offering.IsOverCapacity.Should().BeFalse();
    }

    [Fact]
    public async Task Offering_register_excludes_foreign_tenant_reservations_and_bounds_available_seats()
    {
        var options = Options();
        await using var db = Context(options, 301);
        var ids = await SeedAsync(db, 301);
        var sut = Service(db, 301);
        var created = (await sut.CreateSubjectOfferingAsync(Request(ids))).Data!;
        var registrations = Enumerable.Range(1, 41).Select(i => new StudentSubjectRegistration
        {
            TenantId = 301, SubjectOfferingId = created.Id, StudentEnrollmentId = i,
            ClientRequestId = Guid.NewGuid(),
            State = EduOS.Core.Enums.Domain.SubjectRegistrationState.Approved
        }).ToList();
        registrations.Add(new StudentSubjectRegistration
        {
            TenantId = 302, SubjectOfferingId = created.Id, StudentEnrollmentId = 99,
            ClientRequestId = Guid.NewGuid(),
            State = EduOS.Core.Enums.Domain.SubjectRegistrationState.Pending
        });
        db.AddRange(registrations);
        await db.SaveChangesAsync();
        var result = await sut.GetSubjectOfferingsAsync(ids.Batch, null);
        var offering = result.Data!.Items.Should().ContainSingle().Subject;
        offering.ReservedSeats.Should().Be(41);
        offering.PendingSeats.Should().Be(0);
        offering.AllocatedSeats.Should().Be(41);
        offering.SeatsRemaining.Should().Be(0);
        offering.IsOverCapacity.Should().BeTrue();
    }

    private static SaveSubjectOfferingRequestDto Request((long Batch, long Mapping, long Year) ids) => new()
    {
        ClientRequestId = Guid.NewGuid(), AcademicBatchId = ids.Batch, CurriculumSubjectId = ids.Mapping,
        AcademicYearId = ids.Year, Code = "MATH-2026", Capacity = 40, DeliveryMode = EduOS.Core.Enums.Domain.DeliveryModeType.OnCampus
    };
    private static async Task<(long Batch, long Mapping, long Year)> SeedAsync(EduOSDbContext db, long tenant)
    {
        var campus = new Campus { TenantId = tenant, Name = "Main", Code = "MAIN" };
        var year = new AcademicYear { TenantId = tenant, Name = "2026", Code = "2026",
            StartDate = new DateOnly(2026,1,1), EndDate = new DateOnly(2026,12,31) };
        var program = new AcademicProgram { TenantId = tenant, Name = "Secondary", Code = "SEC" };
        db.AddRange(campus, year, program); await db.SaveChangesAsync();
        var level = new AcademicLevel { TenantId = tenant, AcademicProgramId = program.Id, Name = "Nine", Code = "N9", LevelNo = 9 };
        var subject = new Subject { TenantId = tenant, Name = "Mathematics", Code = "MATH" };
        var curriculum = new AcademicCurriculum { TenantId = tenant, AcademicProgramId = program.Id,
            Name = "Curriculum", Code = "CUR", EffectiveFrom = year.StartDate };
        db.AddRange(level, subject, curriculum); await db.SaveChangesAsync();
        var mapping = new CurriculumSubject { TenantId = tenant, AcademicCurriculumId = curriculum.Id,
            AcademicLevelId = level.Id, SubjectId = subject.Id };
        var batch = new AcademicBatch { TenantId = tenant, CampusId = campus.Id,
            AcademicYearId = year.Id, AcademicProgramId = program.Id, AcademicLevelId = level.Id,
            Name = "Nine A", Code = "N9A", Capacity = 40, StartDate = year.StartDate, EndDate = year.EndDate };
        db.AddRange(mapping, batch); await db.SaveChangesAsync();
        return (batch.Id, mapping.Id, year.Id);
    }
    private static DbContextOptions<EduOSDbContext> Options() => new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase("subject-offerings-" + Guid.NewGuid().ToString("N")).Options;
    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "17"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenant.ToString())
        ], "TestAuthentication"));
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static AcademicSetupService Service(EduOSDbContext db, long tenant) => new(
        new GenericRepository<AcademicProgram>(db), new GenericRepository<AcademicLevel>(db),
        new GenericRepository<Subject>(db), new GenericRepository<AcademicCurriculum>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<AcademicBatch>(db),
        new GenericRepository<Room>(db), new GenericRepository<ProgramCampus>(db),
        new GenericRepository<Campus>(db), new GenericRepository<AcademicDepartment>(db),
        new GenericRepository<AcademicYear>(db), new GenericRepository<AcademicTerm>(db),
        new GenericRepository<AcademicTrack>(db), new GenericRepository<Medium>(db),
        new GenericRepository<Shift>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<StudentSubjectRegistration>(db), db, new User(tenant),
        NullLogger<AcademicSetupService>.Instance);
    private sealed class User(long tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 17;
        public long TenantId => tenant;
        public string? FullName => "Academic Manager";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
