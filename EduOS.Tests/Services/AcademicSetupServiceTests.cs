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

public class AcademicSetupServiceTests
{
    [Fact]
    public async Task Canonical_setup_creates_complete_retry_safe_curriculum_and_batch_flow()
    {
        var options = CreateOptions();
        var seed = await SeedReferenceDataAsync(options, 101);
        await using var context = CreateContext(options, 101, 7, "TenantAdmin");
        var service = CreateService(context, new TestCurrentUser(101, 7, "TenantAdmin"));

        var program = await service.CreateProgramAsync(new CreateAcademicProgramDto { CampusId = seed.CampusId, Name = "Secondary", Code = " sec ", DurationInMonths = 60 });
        var level = await service.CreateLevelAsync(program.Data!.Id, new CreateAcademicLevelDto { Name = "Grade Nine", Code = "g9", LevelNo = 9 });
        var trackRequest = new CreateAcademicTrackDto { AcademicProgramId = program.Data.Id, Name = "Science", Code = " sci ", IsDefault = true };
        var track = await service.CreateTrackAsync(trackRequest);
        var trackReplay = await service.CreateTrackAsync(trackRequest);
        var subjectRequest = new CreateAcademicSubjectDto { Name = "Mathematics", Code = " math ", DefaultFullMarks = 100, DefaultPassMarks = 33, DefaultCreditHours = 4 };
        var subject = await service.CreateSubjectAsync(subjectRequest);
        var subjectReplay = await service.CreateSubjectAsync(subjectRequest);
        var curriculum = await service.CreateCurriculumAsync(new CreateAcademicCurriculumDto { AcademicProgramId = program.Data.Id, Name = "Secondary 2026", Code = "sec-2026", EffectiveFromAcademicYearId = seed.YearId, IsCurrent = true });
        var registration = await service.RegisterCurriculumSubjectAsync(curriculum.Data!.Id, new RegisterCurriculumSubjectDto { AcademicLevelId = level.Data!.Id, SubjectId = subject.Data!.Id, AcademicTrackId = track.Data!.Id, FullMarks = 100, PassMarks = 33, CreditHours = 4 });
        var batchRequest = new CreateAcademicBatchDto { CampusId = seed.CampusId, AcademicYearId = seed.YearId, AcademicProgramId = program.Data.Id, AcademicLevelId = level.Data.Id, AcademicTrackId = track.Data.Id, Name = "Grade Nine A", Code = "g9-a", Capacity = 40, StartDate = new DateTime(2026, 1, 10), EndDate = new DateTime(2026, 12, 10) };
        var batch = await service.CreateBatchAsync(batchRequest);
        var batchReplay = await service.CreateBatchAsync(batchRequest);
        var room = await service.CreateRoomAsync(new CreateAcademicRoomDto { CampusId = seed.CampusId, Name = "Room 101", Code = "r101", Capacity = 45 });
        var catalog = await service.GetCatalogAsync(seed.YearId);

        program.StatusCode.Should().Be(201);
        subject.StatusCode.Should().Be(201);
        subject.Data!.Code.Should().Be("MATH");
        subjectReplay.Success.Should().BeTrue();
        subjectReplay.Data!.Id.Should().Be(subject.Data.Id);
        track.StatusCode.Should().Be(201);
        trackReplay.Data!.Id.Should().Be(track.Data.Id);
        registration.StatusCode.Should().Be(201);
        batch.StatusCode.Should().Be(201);
        batchReplay.Data!.Id.Should().Be(batch.Data!.Id);
        room.StatusCode.Should().Be(201);
        catalog.Success.Should().BeTrue();
        catalog.Data!.Programs.Should().ContainSingle();
        catalog.Data.Levels.Should().ContainSingle();
        catalog.Data.Tracks.Should().ContainSingle();
        catalog.Data.Subjects.Should().ContainSingle();
        catalog.Data.CurriculumSubjects.Should().ContainSingle();
        catalog.Data.Batches.Should().ContainSingle();
        (await context.Subjects.SingleAsync()).ClassId.Should().BeNull();
        (await context.ProgramCampuses.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Track_rejects_foreign_programme_and_second_default_in_same_scope()
    {
        var options = CreateOptions();
        var own = await SeedReferenceDataAsync(options, 101);
        var foreign = await SeedReferenceDataAsync(options, 202);
        long foreignProgramId;
        await using (var foreignContext = CreateContext(options, 202, 8, "TenantAdmin"))
        {
            var foreignService = CreateService(foreignContext, new TestCurrentUser(202, 8, "TenantAdmin"));
            foreignProgramId = (await foreignService.CreateProgramAsync(new CreateAcademicProgramDto { CampusId = foreign.CampusId, Name = "Foreign Programme", Code = "FOREIGN", DurationInMonths = 12 })).Data!.Id;
        }
        await using var context = CreateContext(options, 101, 7, "TenantAdmin");
        var service = CreateService(context, new TestCurrentUser(101, 7, "TenantAdmin"));
        var program = (await service.CreateProgramAsync(new CreateAcademicProgramDto { CampusId = own.CampusId, Name = "Secondary", Code = "SEC", DurationInMonths = 60 })).Data!;

        var first = await service.CreateTrackAsync(new CreateAcademicTrackDto { AcademicProgramId = program.Id, Name = "Science", Code = "SCI", IsDefault = true });
        var duplicateDefault = await service.CreateTrackAsync(new CreateAcademicTrackDto { AcademicProgramId = program.Id, Name = "Business", Code = "BUS", IsDefault = true });
        var foreignProgramme = await service.CreateTrackAsync(new CreateAcademicTrackDto { AcademicProgramId = foreignProgramId, Name = "Foreign", Code = "FOR" });

        first.Success.Should().BeTrue();
        duplicateDefault.StatusCode.Should().Be(409);
        foreignProgramme.StatusCode.Should().Be(404);
        (await context.AcademicTracks.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Curriculum_registration_rejects_level_from_another_program()
    {
        var options = CreateOptions();
        var seed = await SeedReferenceDataAsync(options, 101);
        await using var context = CreateContext(options, 101, 7, "TenantAdmin");
        var service = CreateService(context, new TestCurrentUser(101, 7, "TenantAdmin"));
        var first = (await service.CreateProgramAsync(new CreateAcademicProgramDto { CampusId = seed.CampusId, Name = "First", Code = "P1", DurationInMonths = 24 })).Data!;
        var second = (await service.CreateProgramAsync(new CreateAcademicProgramDto { CampusId = seed.CampusId, Name = "Second", Code = "P2", DurationInMonths = 24 })).Data!;
        var foreignLevel = (await service.CreateLevelAsync(second.Id, new CreateAcademicLevelDto { Name = "Level One", Code = "L1", LevelNo = 1 })).Data!;
        var subject = (await service.CreateSubjectAsync(new CreateAcademicSubjectDto { Name = "Science", Code = "SCI", DefaultFullMarks = 100, DefaultPassMarks = 33 })).Data!;
        var curriculum = (await service.CreateCurriculumAsync(new CreateAcademicCurriculumDto { AcademicProgramId = first.Id, Name = "First Curriculum", Code = "P1-C1", IsCurrent = true })).Data!;

        var result = await service.RegisterCurriculumSubjectAsync(curriculum.Id, new RegisterCurriculumSubjectDto { AcademicLevelId = foreignLevel.Id, SubjectId = subject.Id, FullMarks = 100, PassMarks = 33 });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("different programme");
        (await context.CurriculumSubjects.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Cross_tenant_setup_references_are_neutral_not_found()
    {
        var options = CreateOptions();
        var seed = await SeedReferenceDataAsync(options, 101);
        await using (var firstContext = CreateContext(options, 101, 7, "TenantAdmin"))
        {
            var firstService = CreateService(firstContext, new TestCurrentUser(101, 7, "TenantAdmin"));
            (await firstService.CreateProgramAsync(new CreateAcademicProgramDto { CampusId = seed.CampusId, Name = "Private Programme", Code = "PRIVATE", DurationInMonths = 12 })).Success.Should().BeTrue();
        }
        await SeedReferenceDataAsync(options, 202);
        await using var secondContext = CreateContext(options, 202, 8, "TenantAdmin");
        var secondService = CreateService(secondContext, new TestCurrentUser(202, 8, "TenantAdmin"));

        var catalog = await secondService.GetCatalogAsync(null);
        var foreignCampusWrite = await secondService.CreateProgramAsync(new CreateAcademicProgramDto { CampusId = seed.CampusId, Name = "Invalid", Code = "INVALID", DurationInMonths = 12 });

        catalog.Success.Should().BeTrue();
        catalog.Data!.Programs.Should().BeEmpty();
        foreignCampusWrite.Success.Should().BeFalse();
        foreignCampusWrite.StatusCode.Should().Be(404);
    }

    private static AcademicSetupService CreateService(EduOSDbContext context, ICurrentUserService currentUser) => new(
        new GenericRepository<AcademicProgram>(context),
        new GenericRepository<AcademicLevel>(context),
        new GenericRepository<Subject>(context),
        new GenericRepository<AcademicCurriculum>(context),
        new GenericRepository<CurriculumSubject>(context),
        new GenericRepository<AcademicBatch>(context),
        new GenericRepository<Room>(context),
        new GenericRepository<ProgramCampus>(context),
        new GenericRepository<Campus>(context),
        new GenericRepository<Department>(context),
        new GenericRepository<AcademicYear>(context),
        new GenericRepository<AcademicTerm>(context),
        new GenericRepository<AcademicTrack>(context),
        new GenericRepository<Medium>(context),
        new GenericRepository<Shift>(context),
        context,
        currentUser,
        NullLogger<AcademicSetupService>.Instance);

    private static async Task<SeedData> SeedReferenceDataAsync(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        await using var context = CreateContext(options, tenantId, tenantId, "TenantAdmin");
        var campus = new Campus { TenantId = tenantId, Name = $"Campus {tenantId}", Code = $"C-{tenantId}", IsActive = true };
        var year = new AcademicYear { TenantId = tenantId, Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true, IsActive = true };
        context.AddRange(campus, year);
        await context.SaveChangesAsync();
        return new SeedData(campus.Id, year.Id);
    }

    private static DbContextOptions<EduOSDbContext> CreateOptions() => new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase($"academic-setup-{Guid.NewGuid():N}").Options;

    private static EduOSDbContext CreateContext(DbContextOptions<EduOSDbContext> options, long tenantId, long userId, string role)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role)], "TestAuthentication")) };
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed record SeedData(long CampusId, long YearId);

    private sealed class TestCurrentUser(long tenantId, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "Academic Manager";
        public string? Email => "academic@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS tests";
    }
}
