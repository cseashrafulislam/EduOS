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

public sealed class AcademicSetupServiceTests
{
    [Fact]
    public async Task Canonical_setup_retries_and_creates_curriculum_subjects_with_batch()
    {
        var options = Options();
        var seed = await SeedAsync(options, 101);
        await using var db = Context(options, 101, 7);
        var sut = Service(db, new User(101, 7));
        var request = new SaveAcademicProgramRequestDto
        {
            Name = "Secondary", Code = " sec ", DurationInMonths = 60, CampusIds = [seed.CampusId]
        };
        var program = await sut.CreateProgramAsync(request);
        var programReplay = await sut.CreateProgramAsync(request);
        program.Success.Should().BeTrue();
        programReplay.Data!.Id.Should().Be(program.Data!.Id);
        var level = await sut.CreateLevelAsync(new SaveAcademicLevelRequestDto
        {
            AcademicProgramId = program.Data.Id, Name = "Grade Nine", Code = "g9", LevelNo = 9
        });
        var trackRequest = new SaveAcademicTrackRequestDto
        {
            AcademicProgramId = program.Data.Id, Name = "Science", Code = "sci", IsDefault = true
        };
        var track = await sut.CreateTrackAsync(trackRequest);
        var trackReplay = await sut.CreateTrackAsync(trackRequest);
        var subject = await sut.CreateSubjectAsync(new SaveSubjectRequestDto
        {
            Name = "Mathematics", Code = "math", DefaultCreditHours = 4m
        });
        var curriculumRequest = new SaveAcademicCurriculumRequestDto
        {
            ClientRequestId = Guid.NewGuid(), AcademicProgramId = program.Data.Id,
            AcademicTrackId = track.Data!.Id, Name = "Secondary 2026", Code = "sec-2026",
            EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true,
            Subjects = [new SaveCurriculumSubjectRequestDto
            {
                AcademicLevelId = level.Data!.Id, SubjectId = subject.Data!.Id,
                FullMarks = 100m, PassMarks = 33m, CreditHours = 4m
            }]
        };
        var curriculum = await sut.CreateCurriculumAsync(curriculumRequest);
        var curriculumReplay = await sut.CreateCurriculumAsync(curriculumRequest);
        var batchRequest = new SaveAcademicBatchRequestDto
        {
            ClientRequestId = Guid.NewGuid(), CampusId = seed.CampusId,
            AcademicYearId = seed.YearId, AcademicProgramId = program.Data.Id,
            AcademicLevelId = level.Data!.Id, AcademicTrackId = track.Data.Id,
            Name = "Grade Nine A", Code = "g9-a", Capacity = 40,
            StartDate = new DateOnly(2026, 1, 10), EndDate = new DateOnly(2026, 12, 10)
        };
        var batch = await sut.CreateBatchAsync(batchRequest);
        var batchReplay = await sut.CreateBatchAsync(batchRequest);
        var room = await sut.CreateRoomAsync(new SaveRoomRequestDto
        {
            CampusId = seed.CampusId, Name = "Room 101", Code = "r101", Capacity = 45
        });
        var catalog = await sut.GetSetupOptionsAsync(seed.YearId, null, 20);
        program.StatusCode.Should().Be(201);
        level.StatusCode.Should().Be(201);
        track.StatusCode.Should().Be(201);
        trackReplay.Data!.Id.Should().Be(track.Data.Id);
        subject.Data!.Code.Should().Be("MATH");
        curriculum.StatusCode.Should().Be(201);
        curriculumReplay.Data!.Id.Should().Be(curriculum.Data!.Id);
        curriculum.Data.Subjects.Should().ContainSingle();
        batch.StatusCode.Should().Be(201);
        batchReplay.Data!.Id.Should().Be(batch.Data!.Id);
        room.StatusCode.Should().Be(201);
        catalog.Success.Should().BeTrue();
        catalog.Data!.Programs.Should().ContainSingle();
        catalog.Data.CurriculumSubjects.Should().ContainSingle();
        catalog.Data.Batches.Should().ContainSingle();
        (await db.ProgramCampuses.CountAsync()).Should().Be(1);
        (await db.CurriculumSubjects.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Foreign_program_and_duplicate_default_track_are_rejected()
    {
        var options = Options();
        var own = await SeedAsync(options, 101);
        await SeedAsync(options, 202);
        long foreignProgramId;
        await using (var db = Context(options, 202, 8))
        {
            foreignProgramId = (await Service(db, new User(202, 8)).CreateProgramAsync(
                new SaveAcademicProgramRequestDto
                { Name = "Foreign", Code = "FOREIGN", DurationInMonths = 12 })).Data!.Id;
        }
        await using var ownDb = Context(options, 101, 7);
        var sut = Service(ownDb, new User(101, 7));
        var program = (await sut.CreateProgramAsync(new SaveAcademicProgramRequestDto
        {
            Name = "Secondary", Code = "SEC", CampusIds = [own.CampusId]
        })).Data!;
        var first = await sut.CreateTrackAsync(new SaveAcademicTrackRequestDto
        {
            AcademicProgramId = program.Id, Name = "Science", Code = "SCI", IsDefault = true
        });
        var second = await sut.CreateTrackAsync(new SaveAcademicTrackRequestDto
        {
            AcademicProgramId = program.Id, Name = "Business", Code = "BUS", IsDefault = true
        });
        var foreign = await sut.CreateTrackAsync(new SaveAcademicTrackRequestDto
        {
            AcademicProgramId = foreignProgramId, Name = "Foreign", Code = "FOR"
        });
        first.Success.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        foreign.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Curriculum_rejects_levels_outside_its_program()
    {
        var options = Options();
        await SeedAsync(options, 101);
        await using var db = Context(options, 101, 7);
        var sut = Service(db, new User(101, 7));
        var first = (await sut.CreateProgramAsync(new SaveAcademicProgramRequestDto
        { Name = "First", Code = "P1" })).Data!;
        var second = (await sut.CreateProgramAsync(new SaveAcademicProgramRequestDto
        { Name = "Second", Code = "P2" })).Data!;
        var level = (await sut.CreateLevelAsync(new SaveAcademicLevelRequestDto
        { AcademicProgramId = second.Id, Name = "Level 1", Code = "L1", LevelNo = 1 })).Data!;
        var subject = (await sut.CreateSubjectAsync(new SaveSubjectRequestDto
        { Name = "Science", Code = "SCI" })).Data!;
        var invalid = await sut.CreateCurriculumAsync(new SaveAcademicCurriculumRequestDto
        {
            ClientRequestId = Guid.NewGuid(), AcademicProgramId = first.Id,
            Name = "First Curriculum", Code = "P1-C1", IsCurrent = true,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Subjects = [new SaveCurriculumSubjectRequestDto
            {
                AcademicLevelId = level.Id, SubjectId = subject.Id
            }]
        });
        invalid.StatusCode.Should().Be(409);
        (await db.AcademicCurricula.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Catalog_is_tenant_scoped_and_bounded()
    {
        var options = Options();
        var seed = await SeedAsync(options, 101);
        await using (var db = Context(options, 101, 7))
        {
            (await Service(db, new User(101, 7)).CreateProgramAsync(
                new SaveAcademicProgramRequestDto
                { Name = "Private", Code = "PRIVATE", CampusIds = [seed.CampusId] })).Success.Should().BeTrue();
        }
        await SeedAsync(options, 202);
        await using var foreignDb = Context(options, 202, 8);
        var other = Service(foreignDb, new User(202, 8));
        var catalog = await other.GetSetupOptionsAsync(null, null, 10);
        var invalidCampus = await other.CreateProgramAsync(new SaveAcademicProgramRequestDto
        { Name = "Invalid", Code = "INVALID", CampusIds = [seed.CampusId] });
        catalog.Data!.Programs.Should().BeEmpty();
        invalidCampus.StatusCode.Should().Be(404);
        (await other.GetSetupOptionsAsync(null, null, 101)).StatusCode.Should().Be(400);
    }

    private static AcademicSetupService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<AcademicProgram>(db),
        new GenericRepository<AcademicLevel>(db),
        new GenericRepository<Subject>(db),
        new GenericRepository<AcademicCurriculum>(db),
        new GenericRepository<CurriculumSubject>(db),
        new GenericRepository<AcademicBatch>(db),
        new GenericRepository<Room>(db),
        new GenericRepository<ProgramCampus>(db),
        new GenericRepository<Campus>(db),
        new GenericRepository<AcademicDepartment>(db),
        new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicTerm>(db),
        new GenericRepository<AcademicTrack>(db),
        new GenericRepository<Medium>(db),
        new GenericRepository<Shift>(db),
        db, user, NullLogger<AcademicSetupService>.Instance);

    private static async Task<Seed> SeedAsync(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        await using var db = Context(options, tenant, tenant);
        var campus = new Campus { TenantId = tenant, Name = $"Campus {tenant}", Code = $"C-{tenant}" };
        var year = new AcademicYear
        {
            TenantId = tenant, Name = "2026", Code = "2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            IsCurrent = true, IsActive = true
        };
        db.AddRange(campus, year);
        await db.SaveChangesAsync();
        return new Seed(campus.Id, year.Id);
    }

    private static DbContextOptions<EduOSDbContext> Options() => new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase($"academic-setup-{Guid.NewGuid():N}").Options;

    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant, long userId)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenant.ToString())
        ], "TestAuthentication"));
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed record Seed(long CampusId, long YearId);

    private sealed class User(long tenant, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenant;
        public string? FullName => "Academic Manager";
        public string? Email => "academic@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string name) => name == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS test";
    }
}
