using EduOS.Core.DTOs.Academic;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
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
        var policyRequest = new SaveAcademicCalendarPolicyRequestDto
        {
            CampusId = seed.CampusId,
            WeekendDay1 = DayOfWeek.Friday
        };

        var policy = await service.SavePolicyAsync(policyRequest);
        var policyReplay = await service.SavePolicyAsync(policyRequest);
        var eventRequest = new SaveAcademicCalendarEventRequestDto
        {
            CampusId = seed.CampusId,
            AcademicYearId = seed.YearId,
            AcademicTermId = seed.TermId,
            EventType = CalendarEventKind.Holiday,
            Title = "Autumn holiday",
            StartDate = new DateOnly(2026, 9, 27),
            EndDate = new DateOnly(2026, 9, 27),
            IsPublicVisible = true
        };
        var calendarEvent = await service.CreateEventAsync(eventRequest);
        var eventReplay = await service.CreateEventAsync(eventRequest);
        var days = await service.GetWorkingDaysAsync(seed.YearId, seed.CampusId, new DateOnly(2026, 9, 25), new DateOnly(2026, 9, 27));

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
            (await manager.SavePolicyAsync(new SaveAcademicCalendarPolicyRequestDto
            {
                CampusId = seed.CampusId,
                WeekendDay1 = DayOfWeek.Friday
            })).Success.Should().BeTrue();
            (await manager.CreateEventAsync(Event(seed, "Public exam", true))).Success.Should().BeTrue();
            (await manager.CreateEventAsync(Event(seed, "Private planning meeting", false))).Success.Should().BeTrue();
            var privateHoliday = Event(seed, "Confidential closure reason", false);
            privateHoliday.EventType = CalendarEventKind.Holiday;
            privateHoliday.StartDate = privateHoliday.EndDate = new DateOnly(2026, 9, 22);
            (await manager.CreateEventAsync(privateHoliday)).Success.Should().BeTrue();
        }

        await using var studentContext = CreateContext(options, 101, 71, "Student");
        var student = CreateService(studentContext, new TestCurrentUser(101, 71, "Student"));
        var events = await student.GetEventsAsync(seed.YearId, seed.CampusId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        events.Success.Should().BeTrue();
        events.Data.Should().ContainSingle();
        events.Data!.Single().Title.Should().Be("Public exam");
        var workingDay = await student.GetWorkingDaysAsync(seed.YearId, seed.CampusId, new DateOnly(2026, 9, 22), new DateOnly(2026, 9, 22));
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
            var stale = await manager.UpdateEventAsync(eventId, Update(seed, "Changed by stale client", [8, 7, 6, 5, 4, 3, 2, 1]));
            var updated = await manager.UpdateEventAsync(eventId, Update(seed, "Assessment and review week", row.RowVersion));

            stale.Success.Should().BeFalse();
            stale.StatusCode.Should().Be(409);
            updated.Success.Should().BeTrue();
            updated.Data!.Title.Should().Be("Assessment and review week");
        }

        await SeedAsync(options, 202);
        await using var otherContext = CreateContext(options, 202, 8, "TenantAdmin");
        var other = CreateService(otherContext, new TestCurrentUser(202, 8, "TenantAdmin"));
        var crossTenant = await other.SavePolicyAsync(new SaveAcademicCalendarPolicyRequestDto
        {
            CampusId = seed.CampusId,
            WeekendDay1 = DayOfWeek.Friday
        });

        crossTenant.Success.Should().BeFalse();
        crossTenant.StatusCode.Should().Be(404);
        (await otherContext.AcademicCalendarPolicies.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Large_calendar_ranges_fail_explicitly_instead_of_silently_omitting_holidays()
    {
        var options = CreateOptions();
        var seed = await SeedAsync(options, 101);
        await using var context = CreateContext(options, 101, 7, "TenantAdmin");
        var service = CreateService(context, new TestCurrentUser(101, 7, "TenantAdmin"));
        (await service.SavePolicyAsync(new SaveAcademicCalendarPolicyRequestDto
        {
            CampusId = seed.CampusId, WeekendDay1 = DayOfWeek.Friday
        })).Success.Should().BeTrue();
        context.AcademicCalendarEvents.AddRange(Enumerable.Range(1, 1001).Select(i => new AcademicCalendarEvent
        {
            TenantId = 101, CampusId = seed.CampusId, AcademicYearId = seed.YearId,
            Title = "Closure-" + i, EventType = CalendarEventKind.Holiday,
            StartDate = new DateOnly(2026, 9, 20), EndDate = new DateOnly(2026, 9, 20),
            IsHoliday = true, IsPublicVisible = true
        }));
        await context.SaveChangesAsync();

        var events = await service.GetEventsAsync(seed.YearId, seed.CampusId,
            new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 20));
        var days = await service.GetWorkingDaysAsync(seed.YearId, seed.CampusId,
            new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 20));
        events.Success.Should().BeFalse();
        events.StatusCode.Should().Be(409);
        days.Success.Should().BeFalse();
        days.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Campus_manager_can_edit_only_granted_campus_and_cannot_mutate_institution_wide_calendar()
    {
        var options = CreateOptions();
        var seed = await SeedAsync(options, 101);
        await using var db = CreateContext(options, 101, 7, "TenantAdmin");
        var admin = CreateService(db, new TestCurrentUser(101, 7, "TenantAdmin"));
        var privateGlobal = Event(seed, "Private institution meeting", false);
        privateGlobal.CampusId = null;
        (await admin.CreateEventAsync(privateGlobal)).Success.Should().BeTrue();
        (await admin.CreateEventAsync(Event(seed, "Campus private planning", false))).Success.Should().BeTrue();
        (await admin.SavePolicyAsync(new SaveAcademicCalendarPolicyRequestDto
        {
            CampusId = null, WeekendDay1 = DayOfWeek.Friday
        })).Success.Should().BeTrue();
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = 101, UserId = 9, CampusId = seed.CampusId, IsActive = true
        });
        await db.SaveChangesAsync();
        var principal = CreateService(db, new TestCurrentUser(101, 9, "Principal"));
        var rangeStart = new DateOnly(2026, 9, 1);
        var rangeEnd = new DateOnly(2026, 9, 30);
        var campusEvents = await principal.GetEventsAsync(seed.YearId, seed.CampusId, rangeStart, rangeEnd);
        campusEvents.Success.Should().BeTrue();
        campusEvents.Data.Should().ContainSingle().Which.Title.Should().Be("Campus private planning");
        var globalEvents = await principal.GetEventsAsync(seed.YearId, null, rangeStart, rangeEnd);
        globalEvents.Success.Should().BeTrue();
        globalEvents.Data.Should().BeEmpty();
        (await principal.GetPolicyAsync(null)).Success.Should().BeTrue();
        (await principal.SavePolicyAsync(new SaveAcademicCalendarPolicyRequestDto
        {
            CampusId = null, WeekendDay1 = DayOfWeek.Saturday
        })).StatusCode.Should().Be(403);
        (await principal.CreateEventAsync(privateGlobal)).StatusCode.Should().Be(403);
        (await principal.SavePolicyAsync(new SaveAcademicCalendarPolicyRequestDto
        {
            CampusId = seed.CampusId, WeekendDay1 = DayOfWeek.Saturday
        })).Success.Should().BeTrue();
    }

    [Fact]
    public async Task Unassigned_or_inactive_campus_grants_block_principal_calendar_access()
    {
        var options = CreateOptions();
        var seed = await SeedAsync(options, 101);
        await using var db = CreateContext(options, 101, 10, "VicePrincipal");
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = 101, UserId = 10, CampusId = seed.CampusId, IsActive = false
        });
        await db.SaveChangesAsync();
        var vicePrincipal = CreateService(db, new TestCurrentUser(101, 10, "VicePrincipal"));
        (await vicePrincipal.GetPolicyAsync(seed.CampusId)).StatusCode.Should().Be(403);
        (await vicePrincipal.GetEventsAsync(seed.YearId, seed.CampusId,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30))).StatusCode.Should().Be(403);
        (await vicePrincipal.GetWorkingDaysAsync(seed.YearId, seed.CampusId,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30))).StatusCode.Should().Be(403);
        (await vicePrincipal.SavePolicyAsync(new SaveAcademicCalendarPolicyRequestDto
        {
            CampusId = seed.CampusId, WeekendDay1 = DayOfWeek.Friday
        })).StatusCode.Should().Be(403);
        (await vicePrincipal.CreateEventAsync(Event(seed, "Denied event", true))).StatusCode.Should().Be(403);
    }


    [Fact]
    public async Task Official_BD_2027_holiday_import_is_reviewed_scoped_idempotent_and_audited()
    {
        var options = CreateOptions();
        var seed = await SeedAsync(options, 101);
        await using var db = CreateContext(options, 101, 7, "TenantAdmin");
        var year = await db.AcademicYears.SingleAsync(x => x.Id == seed.YearId);
        year.StartDate = new DateOnly(2027, 1, 1);
        year.EndDate = new DateOnly(2027, 12, 31);
        await db.SaveChangesAsync();
        var service = CreateService(db, new TestCurrentUser(101, 7, "TenantAdmin"));
        var from = new DateOnly(2027, 1, 1);
        var to = new DateOnly(2027, 12, 31);
        var preview = await service.PreviewGovernmentHolidaysAsync(seed.YearId, null, from, to);
        preview.Success.Should().BeTrue();
        preview.Data!.Holidays.Should().HaveCount(19);
        preview.Data.Holidays.Should().OnlyContain(x => x.Status == "Missing");
        preview.Data.Holidays.Should().Contain(x => x.Code == "BD27-EID-FITR" &&
            x.StartDate == new DateOnly(2027, 3, 8) &&
            x.EndDate == new DateOnly(2027, 3, 12) && x.MoonDependent);
        preview.Data.Holidays.Should().NotContain(x => x.Title.Contains("Chaitra Sankranti"));
        var request = new ImportGovernmentHolidaysRequestDto
        {
            AcademicYearId = seed.YearId, FromDate = from, ToDate = to,
            SourceVersion = preview.Data.SourceVersion,
            ClientRequestId = Guid.NewGuid(), Confirmed = true,
            SelectedCodes = ["BD27-LANGUAGE", "BD27-EID-FITR"]
        };
        var first = await service.ImportGovernmentHolidaysAsync(request);
        first.Success.Should().BeTrue(first.Message);
        first.Data!.CreatedCount.Should().Be(2);
        (await db.AcademicCalendarEvents.CountAsync()).Should().Be(2);
        (await db.AuditLogs.CountAsync(x => x.Action == "AcademicCalendar.ImportGovernmentHolidays"))
            .Should().Be(1);
        var replay = await service.ImportGovernmentHolidaysAsync(request);
        replay.Success.Should().BeTrue();
        replay.Data!.CreatedCount.Should().Be(2);
        var next = new ImportGovernmentHolidaysRequestDto
        {
            AcademicYearId = request.AcademicYearId, FromDate = from, ToDate = to,
            SourceVersion = request.SourceVersion, ClientRequestId = Guid.NewGuid(),
            Confirmed = true, SelectedCodes = request.SelectedCodes
        };
        var already = await service.ImportGovernmentHolidaysAsync(next);
        already.Success.Should().BeTrue();
        already.Data!.CreatedCount.Should().Be(0);
        already.Data.ExistingCount.Should().Be(2);
        (await db.AcademicCalendarEvents.CountAsync()).Should().Be(2);
        var changedPayload = new ImportGovernmentHolidaysRequestDto
        {
            AcademicYearId = request.AcademicYearId, FromDate = from, ToDate = to,
            SourceVersion = request.SourceVersion, ClientRequestId = request.ClientRequestId,
            Confirmed = true, SelectedCodes = ["BD27-VICTORY"]
        };
        (await service.ImportGovernmentHolidaysAsync(changedPayload)).StatusCode.Should().Be(409);
        (await db.AcademicCalendarEvents.CountAsync()).Should().Be(2);
        var report = await service.GetWorkingDaysAsync(seed.YearId, seed.CampusId,
            new DateOnly(2027, 3, 8), new DateOnly(2027, 3, 12));
        report.Success.Should().BeTrue();
        report.Data!.Should().OnlyContain(x => !x.IsWorkingDay);
    }

    [Fact]
    public async Task Official_holiday_preview_and_import_reject_cross_country_unauthorized_and_conflicts()
    {
        var options = CreateOptions();
        var seed = await SeedAsync(options, 101);
        await using var db = CreateContext(options, 101, 7, "TenantAdmin");
        var year = await db.AcademicYears.SingleAsync(x => x.Id == seed.YearId);
        year.StartDate = new DateOnly(2027, 1, 1);
        year.EndDate = new DateOnly(2027, 12, 31);
        await db.SaveChangesAsync();
        var admin = CreateService(db, new TestCurrentUser(101, 7, "TenantAdmin"));
        var teacher = CreateService(db, new TestCurrentUser(101, 71, "Teacher"));
        var principal = CreateService(db, new TestCurrentUser(101, 9, "Principal"));
        var from = new DateOnly(2027, 1, 1);
        var to = new DateOnly(2027, 12, 31);
        (await teacher.PreviewGovernmentHolidaysAsync(seed.YearId, null, from, to)).StatusCode.Should().Be(403);
        (await principal.PreviewGovernmentHolidaysAsync(seed.YearId, null, from, to)).StatusCode.Should().Be(403);
        (await principal.PreviewGovernmentHolidaysAsync(seed.YearId, seed.CampusId, from, to)).StatusCode.Should().Be(403);
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = 101, UserId = 9, CampusId = seed.CampusId, IsActive = true
        });
        await db.SaveChangesAsync();
        (await principal.PreviewGovernmentHolidaysAsync(seed.YearId, seed.CampusId, from, to))
            .Success.Should().BeTrue();
        db.AcademicCalendarEvents.Add(new AcademicCalendarEvent
        {
            TenantId = 101, AcademicYearId = seed.YearId, CampusId = null,
            EventType = CalendarEventKind.Academic, IsHoliday = false,
            Title = "Language Martyrs' Day",
            StartDate = new DateOnly(2027, 2, 21),
            EndDate = new DateOnly(2027, 2, 21)
        });
        await db.SaveChangesAsync();
        var conflict = await admin.PreviewGovernmentHolidaysAsync(seed.YearId, null, from, to);
        conflict.Data!.Holidays.Single(x => x.Code == "BD27-LANGUAGE").Status.Should().Be("Conflict");
        var request = new ImportGovernmentHolidaysRequestDto
        {
            ClientRequestId = Guid.NewGuid(), AcademicYearId = seed.YearId,
            FromDate = from, ToDate = to,
            SourceVersion = conflict.Data.SourceVersion, Confirmed = true,
            SelectedCodes = ["BD27-LANGUAGE"]
        };
        (await admin.ImportGovernmentHolidaysAsync(request)).StatusCode.Should().Be(409);
        (await db.AcademicCalendarEvents.CountAsync()).Should().Be(1);
        var tenant = await db.Tenants.SingleAsync(x => x.Id == 101);
        tenant.CountryCode = "US";
        await db.SaveChangesAsync();
        (await admin.PreviewGovernmentHolidaysAsync(seed.YearId, null, from, to))
            .StatusCode.Should().Be(403);
        (await db.AcademicCalendarEvents.CountAsync()).Should().Be(1);
    }

    private static SaveAcademicCalendarEventRequestDto Event(CalendarSeed seed, string title, bool visible) => new()
    {
        CampusId = seed.CampusId,
        AcademicYearId = seed.YearId,
        AcademicTermId = seed.TermId,
        EventType = CalendarEventKind.Academic,
        Title = title,
        StartDate = new DateOnly(2026, 9, 20),
        EndDate = new DateOnly(2026, 9, 21),
        IsPublicVisible = visible
    };

    private static SaveAcademicCalendarEventRequestDto Update(CalendarSeed seed, string title, byte[] rowVersion) => new()
    {
        CampusId = seed.CampusId,
        AcademicYearId = seed.YearId,
        AcademicTermId = seed.TermId,
        EventType = CalendarEventKind.Examination,
        Title = title,
        StartDate = new DateOnly(2026, 9, 20),
        EndDate = new DateOnly(2026, 9, 22),
        IsPublicVisible = true,
        RowVersion = Convert.ToBase64String(rowVersion)
    };

    private static AcademicCalendarService CreateService(EduOSDbContext context, ICurrentUserService currentUser) => new(
        new GenericRepository<AcademicCalendarPolicy>(context),
        new GenericRepository<AcademicCalendarEvent>(context),
        new GenericRepository<AcademicYear>(context),
        new GenericRepository<AcademicTerm>(context),
        new GenericRepository<Campus>(context),
        new GenericRepository<UserCampusAccess>(context),
        context,
        currentUser,
        TimeProvider.System,
        NullLogger<AcademicCalendarService>.Instance,
        context);

    private static async Task<CalendarSeed> SeedAsync(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        await using var context = CreateContext(options, tenantId, tenantId, "TenantAdmin");
        var campus = new Campus { TenantId = tenantId, Name = $"Campus {tenantId}", Code = $"C-{tenantId}", IsActive = true };
        var year = new AcademicYear { TenantId = tenantId, Name = "2026", Code = "2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), IsCurrent = true, IsActive = true };
        context.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Calendar Institution " + tenantId,
            Code = "CAL-" + tenantId, Email = "calendar" + tenantId + "@example.test",
            CountryCode = "BD"
        });
        context.AddRange(campus, year);
        await context.SaveChangesAsync();
        var term = new AcademicTerm { TenantId = tenantId, AcademicYearId = year.Id, Name = "Autumn", StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 12, 15), IsActive = true };
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
