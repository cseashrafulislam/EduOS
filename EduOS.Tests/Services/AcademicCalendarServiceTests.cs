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

public class AcademicCalendarServiceTests
{
    [Fact]
    public async Task Policy_and_holiday_events_are_retry_safe_and_produce_working_days()
    {
        var options = CreateOptions();
        var seed = await SeedAsync(options, 101);
        await using var context = CreateContext(options, 101, 7, "TenantAdmin");
        var service = CreateService(context, new TestCurrentUser(101, 7, "TenantAdmin"));
        var policyRequest = new SaveAcademicCalendarPolicyDto
        {
            ClientRequestId = Guid.NewGuid(),
            AcademicYearId = seed.YearId,
            CampusId = seed.CampusId,
            WeekendDays = [DayOfWeek.Friday]
        };

        var policy = await service.SavePolicyAsync(policyRequest);
        var policyReplay = await service.SavePolicyAsync(policyRequest);
        var eventRequest = new CreateAcademicCalendarEventDto
        {
            ClientRequestId = Guid.NewGuid(),
            CampusId = seed.CampusId,
            AcademicYearId = seed.YearId,
            AcademicTermId = seed.TermId,
            EventType = AcademicCalendarEventType.Holiday,
            Title = "Autumn holiday",
            StartDate = new DateTime(2026, 9, 27),
            EndDate = new DateTime(2026, 9, 27),
            IsPublicVisible = true
        };
        var calendarEvent = await service.CreateEventAsync(eventRequest);
        var eventReplay = await service.CreateEventAsync(eventRequest);
        var days = await service.GetWorkingDaysAsync(seed.YearId, seed.CampusId, new DateTime(2026, 9, 25), new DateTime(2026, 9, 27));

        policy.StatusCode.Should().Be(201);
        policyReplay.Data!.Id.Should().Be(policy.Data!.Id);
        calendarEvent.StatusCode.Should().Be(201);
        calendarEvent.Data!.IsHoliday.Should().BeTrue();
        eventReplay.Data!.Id.Should().Be(calendarEvent.Data.Id);
        days.Success.Should().BeTrue();
        days.Data.Should().HaveCount(3);
        days.Data![0].Date.DayOfWeek.Should().Be(DayOfWeek.Friday);
        days.Data[0].IsWeekend.Should().BeTrue();
        days.Data[0].IsWorkingDay.Should().BeFalse();
        days.Data[1].IsWorkingDay.Should().BeTrue();
        days.Data[2].HolidayNames.Should().ContainSingle().Which.Should().Be("Autumn holiday");
        days.Data[2].IsWorkingDay.Should().BeFalse();
        (await context.AcademicCalendarPolicies.CountAsync()).Should().Be(1);
        (await context.AcademicCalendarEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Non_managers_only_read_public_events()
    {
        var options = CreateOptions();
        var seed = await SeedAsync(options, 101);
        await using (var managerContext = CreateContext(options, 101, 7, "TenantAdmin"))
        {
            var manager = CreateService(managerContext, new TestCurrentUser(101, 7, "TenantAdmin"));
            (await manager.SavePolicyAsync(new SaveAcademicCalendarPolicyDto
            {
                ClientRequestId = Guid.NewGuid(),
                AcademicYearId = seed.YearId,
                CampusId = seed.CampusId,
                WeekendDays = [DayOfWeek.Friday]
            })).Success.Should().BeTrue();
            (await manager.CreateEventAsync(Event(seed, "Public exam", true))).Success.Should().BeTrue();
            (await manager.CreateEventAsync(Event(seed, "Private planning meeting", false))).Success.Should().BeTrue();
            var privateHoliday = Event(seed, "Confidential closure reason", false);
            privateHoliday.EventType = AcademicCalendarEventType.Holiday;
            privateHoliday.StartDate = privateHoliday.EndDate = new DateTime(2026, 9, 22);
            (await manager.CreateEventAsync(privateHoliday)).Success.Should().BeTrue();
        }

        await using var studentContext = CreateContext(options, 101, 71, "Student");
        var student = CreateService(studentContext, new TestCurrentUser(101, 71, "Student"));
        var events = await student.GetEventsAsync(seed.YearId, seed.CampusId, new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));

        events.Success.Should().BeTrue();
        events.Data.Should().ContainSingle();
        events.Data!.Single().Title.Should().Be("Public exam");
        var workingDay = await student.GetWorkingDaysAsync(seed.YearId, seed.CampusId, new DateTime(2026, 9, 22), new DateTime(2026, 9, 22));
        workingDay.Data.Should().ContainSingle();
        workingDay.Data![0].IsWorkingDay.Should().BeFalse();
        workingDay.Data[0].HolidayNames.Should().ContainSingle().Which.Should().Be("Holiday");
    }

    [Fact]
    public async Task Event_update_rejects_stale_version_and_cross_tenant_scope_is_not_found()
    {
        var options = CreateOptions();
        var seed = await SeedAsync(options, 101);
        long eventId;
        await using (var managerContext = CreateContext(options, 101, 7, "TenantAdmin"))
        {
            var manager = CreateService(managerContext, new TestCurrentUser(101, 7, "TenantAdmin"));
            eventId = (await manager.CreateEventAsync(Event(seed, "Assessment week", true))).Data!.Id;
            var row = await managerContext.AcademicCalendarEvents.SingleAsync(x => x.Id == eventId);
            row.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
            await managerContext.SaveChangesAsync();
            var stale = await manager.UpdateEventAsync(eventId, Update("Changed by stale client", [8, 7, 6, 5, 4, 3, 2, 1]));
            var updated = await manager.UpdateEventAsync(eventId, Update("Assessment and review week", row.RowVersion));

            stale.Success.Should().BeFalse();
            stale.StatusCode.Should().Be(409);
            updated.Success.Should().BeTrue();
            updated.Data!.Title.Should().Be("Assessment and review week");
        }

        await SeedAsync(options, 202);
        await using var otherContext = CreateContext(options, 202, 8, "TenantAdmin");
        var other = CreateService(otherContext, new TestCurrentUser(202, 8, "TenantAdmin"));
        var crossTenant = await other.SavePolicyAsync(new SaveAcademicCalendarPolicyDto
        {
            ClientRequestId = Guid.NewGuid(),
            AcademicYearId = seed.YearId,
            CampusId = seed.CampusId,
            WeekendDays = [DayOfWeek.Friday]
        });

        crossTenant.Success.Should().BeFalse();
        crossTenant.StatusCode.Should().Be(404);
        (await otherContext.AcademicCalendarPolicies.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    private static CreateAcademicCalendarEventDto Event(CalendarSeed seed, string title, bool visible) => new()
    {
        ClientRequestId = Guid.NewGuid(),
        CampusId = seed.CampusId,
        AcademicYearId = seed.YearId,
        AcademicTermId = seed.TermId,
        EventType = AcademicCalendarEventType.Academic,
        Title = title,
        StartDate = new DateTime(2026, 9, 20),
        EndDate = new DateTime(2026, 9, 21),
        IsPublicVisible = visible
    };

    private static UpdateAcademicCalendarEventDto Update(string title, byte[] rowVersion) => new()
    {
        EventType = AcademicCalendarEventType.Examination,
        Title = title,
        StartDate = new DateTime(2026, 9, 20),
        EndDate = new DateTime(2026, 9, 22),
        IsPublicVisible = true,
        IsActive = true,
        RowVersion = Convert.ToBase64String(rowVersion)
    };

    private static AcademicCalendarService CreateService(EduOSDbContext context, ICurrentUserService currentUser) => new(
        new GenericRepository<AcademicCalendarPolicy>(context),
        new GenericRepository<AcademicCalendarEvent>(context),
        new GenericRepository<AcademicYear>(context),
        new GenericRepository<AcademicTerm>(context),
        new GenericRepository<Campus>(context),
        context,
        currentUser,
        TimeProvider.System,
        NullLogger<AcademicCalendarService>.Instance);

    private static async Task<CalendarSeed> SeedAsync(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        await using var context = CreateContext(options, tenantId, tenantId, "TenantAdmin");
        var campus = new Campus { TenantId = tenantId, Name = $"Campus {tenantId}", Code = $"C-{tenantId}", IsActive = true };
        var year = new AcademicYear { TenantId = tenantId, Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true, IsActive = true };
        context.AddRange(campus, year);
        await context.SaveChangesAsync();
        var term = new AcademicTerm { TenantId = tenantId, AcademicYearId = year.Id, Name = "Autumn", StartDate = new DateTime(2026, 7, 1), EndDate = new DateTime(2026, 12, 15), IsActive = true };
        context.AcademicTerms.Add(term);
        await context.SaveChangesAsync();
        return new CalendarSeed(campus.Id, year.Id, term.Id);
    }

    private static DbContextOptions<EduOSDbContext> CreateOptions() => new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase($"academic-calendar-{Guid.NewGuid():N}").Options;

    private static EduOSDbContext CreateContext(DbContextOptions<EduOSDbContext> options, long tenantId, long userId, string role)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role), new Claim("TenantId", tenantId.ToString())
        ], "TestAuthentication")) };
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed record CalendarSeed(long CampusId, long YearId, long TermId);

    private sealed class TestCurrentUser(long tenantId, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "Calendar User";
        public string? Email => "calendar@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS tests";
    }
}
