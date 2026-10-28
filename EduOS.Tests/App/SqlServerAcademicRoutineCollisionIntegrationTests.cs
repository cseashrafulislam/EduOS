using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Academic;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>Executes the routine collision EXISTS joins on real SQL Server, not only EF InMemory.</summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerAcademicRoutineCollisionIntegrationTests
{
    [Fact]
    public async Task Overlapping_room_booking_is_rejected_across_batches_and_nonoverlap_is_allowed()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, userId, roomId;
        Guid offeringA, offeringB;

        await using (var seed = new EduOSDbContext(options))
        {
            var tenant = new Tenant
            {
                Code = "CI-RT-" + suffix, Name = "Routine Collision CI",
                Email = "routine-" + suffix + "@example.test", State = TenantState.Active
            };
            var login = "routine-admin-" + suffix + "@example.test";
            var user = new ApplicationUser
            {
                UserName = login, NormalizedUserName = login.ToUpperInvariant(),
                Email = login, NormalizedEmail = login.ToUpperInvariant(),
                FullName = "Routine CI Manager", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            seed.AddRange(tenant, user);
            await seed.SaveChangesAsync();
            tenantId = tenant.Id;
            userId = user.Id;
            seed.ChangeTracker.Clear();

            using (seed.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN" };
                var year = new AcademicYear
                {
                    TenantId = tenantId, Name = "2026", Code = "2026",
                    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31)
                };
                var program = new AcademicProgram { TenantId = tenantId, Name = "Secondary", Code = "SEC" };
                var subject = new Subject { TenantId = tenantId, Name = "Math", Code = "MATH" };
                seed.AddRange(campus, year, program, subject);
                await seed.SaveChangesAsync();

                var level = new AcademicLevel
                {
                    TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Nine", Code = "G9", LevelNo = 9
                };
                var curriculum = new AcademicCurriculum
                {
                    TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "2026", Code = "SEC26", EffectiveFrom = new DateOnly(2026, 1, 1)
                };
                var room = new Room
                {
                    TenantId = tenantId, CampusId = campus.Id,
                    Name = "Shared Room", Code = "ROOM1", Capacity = 40
                };
                seed.AddRange(level, curriculum, room);
                await seed.SaveChangesAsync();
                roomId = room.Id;

                var item = new CurriculumSubject
                {
                    TenantId = tenantId, AcademicCurriculumId = curriculum.Id,
                    AcademicLevelId = level.Id, SubjectId = subject.Id
                };
                var batchA = new AcademicBatch
                {
                    TenantId = tenantId, CampusId = campus.Id, AcademicYearId = year.Id,
                    AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                    Name = "Nine A", Code = "NINE-A", Capacity = 30
                };
                var batchB = new AcademicBatch
                {
                    TenantId = tenantId, CampusId = campus.Id, AcademicYearId = year.Id,
                    AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                    Name = "Nine B", Code = "NINE-B", Capacity = 30
                };
                seed.AddRange(item, batchA, batchB);
                await seed.SaveChangesAsync();

                var first = new SubjectOffering
                {
                    TenantId = tenantId, AcademicBatchId = batchA.Id,
                    AcademicYearId = year.Id, CurriculumSubjectId = item.Id, Code = "MATH-A"
                };
                var second = new SubjectOffering
                {
                    TenantId = tenantId, AcademicBatchId = batchB.Id,
                    AcademicYearId = year.Id, CurriculumSubjectId = item.Id, Code = "MATH-B"
                };
                seed.SubjectOfferings.AddRange(first, second);
                await seed.SaveChangesAsync();
                offeringA = first.PublicId;
                offeringB = second.PublicId;
            }
        }

        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        await using var db = new EduOSDbContext(options, accessor);
        var service = new AcademicRoutineService(
            new GenericRepository<RoutineTimeSlot>(db),
            new GenericRepository<InstructorAssignment>(db),
            new GenericRepository<RoutineEntry>(db),
            new GenericRepository<AcademicBatch>(db),
            new GenericRepository<AcademicYear>(db),
            new GenericRepository<AcademicTerm>(db),
            new GenericRepository<SubjectOffering>(db),
            new GenericRepository<CurriculumSubject>(db),
            new GenericRepository<Subject>(db),
            new GenericRepository<Employee>(db),
            new GenericRepository<Room>(db),
            new GenericRepository<UserCampusAccess>(db),
            new GenericRepository<Campus>(db),
            db, new Admin(tenantId, userId), TimeProvider.System,
            NullLogger<AcademicRoutineService>.Instance);

        var morning = await service.CreateTimeSlotAsync(new SaveRoutineTimeSlotRequestDto
        { Name = "Morning", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 45) });
        var overlapping = await service.CreateTimeSlotAsync(new SaveRoutineTimeSlotRequestDto
        { Name = "Overlap", StartTime = new TimeOnly(9, 30), EndTime = new TimeOnly(10, 15) });
        var afternoon = await service.CreateTimeSlotAsync(new SaveRoutineTimeSlotRequestDto
        { Name = "Afternoon", StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(11, 45) });
        Assert.True(morning.Success, morning.Message);
        Assert.True(overlapping.Success, overlapping.Message);
        Assert.True(afternoon.Success, afternoon.Message);

        SaveRoutineEntryRequestDto Entry(Guid offering, long slot) => new()
        {
            ClientRequestId = Guid.NewGuid(), SubjectOfferingReference = offering,
            RoutineTimeSlotId = slot, RoomId = roomId,
            DayOfWeek = DayOfWeek.Monday, EffectiveFrom = new DateOnly(2026, 1, 5),
            IsActive = true
        };
        var firstEntry = await service.CreateEntryAsync(Entry(offeringA, morning.Data!.Id));
        Assert.True(firstEntry.Success, firstEntry.Message);
        var collision = await service.CreateEntryAsync(Entry(offeringB, overlapping.Data!.Id));
        Assert.Equal(409, collision.StatusCode);
        Assert.Contains("Room", collision.Message);
        var valid = await service.CreateEntryAsync(Entry(offeringB, afternoon.Data!.Id));
        Assert.True(valid.Success, valid.Message);
        Assert.Equal(2, await db.RoutineEntries.CountAsync(x => x.TenantId == tenantId));
    }

    private sealed class Admin(long tenant, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => userId;
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? FullName => "Academic CI";
        public string? Email => "ci@example.test";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL academic routine CI";
    }
}
