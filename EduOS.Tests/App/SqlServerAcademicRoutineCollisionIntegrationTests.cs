using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Learners;
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
        long tenantId, userId, roomId, batchAId, batchBId;
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
                batchAId = batchA.Id;
                batchBId = batchB.Id;

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
        var service = BuildService(db, tenantId, userId);

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

        // Execute the new batch timetable paging query against SQL Server, not EF InMemory.
        var pageA = await service.GetBatchTimetablePageAsync(batchAId, null, 1, 1);
        var pageB = await service.GetBatchTimetablePageAsync(batchBId, null, 1, 1);
        Assert.True(pageA.Success, pageA.Message);
        Assert.True(pageB.Success, pageB.Message);
        Assert.Equal(1, pageA.Data!.TotalCount);
        Assert.Equal(1, pageB.Data!.TotalCount);
        Assert.Single(pageA.Data.Items);
        Assert.Single(pageB.Data.Items);
        Assert.Equal(400, (await service.GetBatchTimetablePageAsync(batchAId, null, 1, 101)).StatusCode);
        Assert.Equal(404, (await BuildService(db, tenantId + 1, userId)
            .GetBatchTimetablePageAsync(batchAId, null, 1, 50)).StatusCode);

        // Independent contexts simulate competing authenticated requests for one room.
        // Both bookings are individually valid; exactly one may commit.
        await using var competingDbA = new EduOSDbContext(options, accessor);
        await using var competingDbB = new EduOSDbContext(options, accessor);
        var requestA = Entry(offeringA, morning.Data!.Id);
        var requestB = Entry(offeringB, morning.Data.Id);
        requestA.DayOfWeek = DayOfWeek.Tuesday;
        requestB.DayOfWeek = DayOfWeek.Tuesday;
        var attempts = await Task.WhenAll(
            BuildService(competingDbA, tenantId, userId).CreateEntryAsync(requestA),
            BuildService(competingDbB, tenantId, userId).CreateEntryAsync(requestB));
        Assert.Single(attempts.Where(x => x.Success));
        Assert.Single(attempts.Where(x => x.StatusCode == 409));
        Assert.Equal(3, await db.RoutineEntries.CountAsync(x => x.TenantId == tenantId));

        // Replaying the winner is idempotent; retrying the loser cannot double-book.
        var winner = attempts[0].Success ? requestA : requestB;
        var loser = attempts[0].Success ? requestB : requestA;
        Assert.True((await service.CreateEntryAsync(winner)).Success);
        Assert.Equal(409, (await service.CreateEntryAsync(loser)).StatusCode);
        Assert.Equal(3, await db.RoutineEntries.CountAsync(x => x.TenantId == tenantId));

        // Real SQL: independent requests compete for one cover date, then register,
        // summary, replay, cancellation and cross-tenant reads share persisted state.
        var personA = new Person { FullName = "Original SQL Instructor" };
        var personB = new Person { FullName = "Cover SQL Instructor" };
        var designation = new Designation { TenantId = tenantId, Name = "Teacher", Code = "TEACH-" + suffix };
        db.AddRange(personA, personB, designation);
        await db.SaveChangesAsync();
        var original = new Employee
        {
            TenantId = tenantId, PersonId = personA.Id, DesignationId = designation.Id,
            EmployeeCode = "ORIG-" + suffix, FullName = "Original SQL Instructor",
            JoiningDate = new DateOnly(2026, 1, 1), CanTeach = true, State = EmployeeState.Active
        };
        var substitute = new Employee
        {
            TenantId = tenantId, PersonId = personB.Id, DesignationId = designation.Id,
            EmployeeCode = "COVER-" + suffix, FullName = "Cover SQL Instructor",
            JoiningDate = new DateOnly(2026, 1, 1), CanTeach = true, State = EmployeeState.Active
        };
        db.AddRange(original, substitute);
        await db.SaveChangesAsync();
        var offeringId = await db.SubjectOfferings.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.PublicId == offeringA)
            .Select(x => x.Id).SingleAsync();
        var assignment = new InstructorAssignment
        {
            TenantId = tenantId, SubjectOfferingId = offeringId, EmployeeId = original.Id,
            EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true
        };
        db.Add(assignment);
        await db.SaveChangesAsync();
        var scheduledRoutine = await db.RoutineEntries.SingleAsync(x =>
            x.TenantId == tenantId && x.SubjectOfferingId == offeringId &&
            x.DayOfWeek == DayOfWeek.Monday);
        scheduledRoutine.InstructorAssignmentId = assignment.Id;
        await db.SaveChangesAsync();
        var routineId = scheduledRoutine.Id;
        db.ChangeTracker.Clear();

        var coverDate = new DateOnly(2026, 9, 21); // Monday, inside the academic year.
        CreateSubstitutionRequestDto Cover() => new()
        {
            ClientRequestId = Guid.NewGuid(), RoutineEntryId = routineId,
            Date = coverDate, SubstituteEmployeeReference = substitute.PublicId,
            Reason = "Approved cover"
        };
        var coverA = Cover();
        var coverB = Cover();
        await using var coverDbA = new EduOSDbContext(options, accessor);
        await using var coverDbB = new EduOSDbContext(options, accessor);
        var coverAttempts = await Task.WhenAll(
            BuildInstructionService(coverDbA, tenantId, userId).CreateSubstitutionAsync(coverA),
            BuildInstructionService(coverDbB, tenantId, userId).CreateSubstitutionAsync(coverB));
        Assert.Single(coverAttempts.Where(x => x.Success));
        Assert.Single(coverAttempts.Where(x => x.StatusCode == 409));
        Assert.Equal(1, await db.Substitutions.AsNoTracking().CountAsync(x =>
            x.TenantId == tenantId && x.Date == coverDate && !x.IsDeleted));

        var coverWinner = coverAttempts[0].Success ? coverA : coverB;
        var coverLoser = coverAttempts[0].Success ? coverB : coverA;
        var instruction = BuildInstructionService(db, tenantId, userId);
        var replay = await instruction.CreateSubstitutionAsync(coverWinner);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(coverAttempts.First(x => x.Success).Data!.Id, replay.Data!.Id);
        Assert.Equal(409, (await instruction.CreateSubstitutionAsync(coverLoser)).StatusCode);
        var active = await instruction.GetSubstitutionSummaryAsync(coverDate, coverDate, batchAId);
        Assert.True(active.Success, active.Message);
        Assert.Equal(1, active.Data!.TotalCount);
        Assert.Equal(1, active.Data.ActiveCount);
        var page = await instruction.GetSubstitutionsPageFilteredAsync(
            coverDate, coverDate, batchAId, false, 1, 20);
        Assert.True(page.Success, page.Message);
        Assert.Single(page.Data!.Items);
        Assert.Equal("Cover SQL Instructor", page.Data.Items[0].SubstituteEmployeeName);
        var otherTenant = await BuildInstructionService(db, tenantId + 1, userId)
            .GetSubstitutionSummaryAsync(coverDate, coverDate, batchAId);
        Assert.True(otherTenant.Success, otherTenant.Message);
        Assert.Equal(0, otherTenant.Data!.TotalCount);

        var cancelled = await instruction.CancelSubstitutionAsync(replay.Data.Id,
            new CancelSubstitutionRequestDto { RowVersion = replay.Data.RowVersion });
        Assert.True(cancelled.Success, cancelled.Message);
        var after = await instruction.GetSubstitutionSummaryAsync(coverDate, coverDate, batchAId);
        Assert.True(after.Success, after.Message);
        Assert.Equal(1, after.Data!.CancelledCount);
        Assert.Equal(0, after.Data.ActiveCount);
        var history = await instruction.GetSubstitutionsPageFilteredAsync(
            coverDate, coverDate, batchAId, true, 1, 20);
        Assert.True(history.Success, history.Message);
        Assert.Single(history.Data!.Items);
        Assert.True(history.Data.Items[0].IsCancelled);
        Assert.Equal(409, (await instruction.CancelSubstitutionAsync(replay.Data.Id,
            new CancelSubstitutionRequestDto { RowVersion = replay.Data.RowVersion })).StatusCode);
    }

    private static AcademicRoutineService BuildService(EduOSDbContext db, long tenantId, long userId) => new(
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
        new GenericRepository<EmployeeCampusAssignment>(db),
        new GenericRepository<Room>(db),
        new GenericRepository<UserCampusAccess>(db),
        new GenericRepository<Campus>(db),
        db, new Admin(tenantId, userId), TimeProvider.System,
        NullLogger<AcademicRoutineService>.Instance);


    private static AcademicInstructionService BuildInstructionService(EduOSDbContext db, long tenantId, long userId) => new(
        new GenericRepository<Substitution>(db),
        new GenericRepository<LessonPlan>(db),
        new GenericRepository<RoutineEntry>(db),
        new GenericRepository<RoutineTimeSlot>(db),
        new GenericRepository<InstructorAssignment>(db),
        new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db),
        new GenericRepository<Subject>(db),
        new GenericRepository<AcademicBatch>(db),
        new GenericRepository<UserCampusAccess>(db),
        new GenericRepository<Campus>(db),
        new GenericRepository<Employee>(db),
        new GenericRepository<EmployeeCampusAssignment>(db),
        new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicTerm>(db),
        db, new Admin(tenantId, userId), TimeProvider.System,
        NullLogger<AcademicInstructionService>.Instance);

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
