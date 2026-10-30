using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Academic;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task Calendar_aware_schedule_skips_private_holiday_and_preserves_committed_replays()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await PlannedTripAsync(connection);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var firstDate = today.AddDays(1);
        var holidayDate = today.AddDays(2);
        long holidayId;

        await using (var setup = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var route = await setup.Routes.SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.PublicId == state.Fixture.RouteReference);
            var link = await setup.RouteVehicleAssignments.SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.RouteId == route.Id &&
                x.VehicleId == state.Fixture.VehicleId && x.IsCurrent && !x.IsDeleted);
            link.EffectiveTo = holidayDate;
            var year = await setup.AcademicYears.SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId &&
                x.StartDate <= firstDate && x.EndDate >= holidayDate);
            var weekend = Enum.GetValues<DayOfWeek>()
                .First(x => x != firstDate.DayOfWeek && x != holidayDate.DayOfWeek);
            setup.AcademicCalendarPolicies.Add(new AcademicCalendarPolicy
            {
                TenantId = state.Fixture.TenantId, CampusId = route.CampusId,
                WeekendDay1 = weekend, IsActive = true
            });
            var holiday = new AcademicCalendarEvent
            {
                TenantId = state.Fixture.TenantId, CampusId = route.CampusId,
                AcademicYearId = year.Id, Title = "Confidential campus closure",
                StartDate = holidayDate, EndDate = holidayDate,
                IsHoliday = true, IsPublicVisible = false
            };
            setup.AcademicCalendarEvents.Add(holiday);
            await setup.SaveChangesAsync();
            holidayId = holiday.Id;
        }

        var request = new ScheduleTransportTripsRequestDto
        {
            ClientRequestId = Guid.NewGuid(),
            RouteReference = state.Fixture.RouteReference,
            VehicleReference = state.Fixture.VehicleReference,
            StartDate = firstDate, EndDate = holidayDate, Direction = "Pickup",
            UseAcademicCalendar = true
        };
        async Task<EduOS.Core.Common.ApiResponse<TransportTripScheduleResultDto>> RunAsync()
        {
            await using var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
            return await TripService(db, state.Fixture).ScheduleAsync(request);
        }

        await using (var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var calendar = await TripService(db, state.Fixture).GetScheduleCalendarAsync(
                request.RouteReference, firstDate, holidayDate);
            Assert.True(calendar.Success, calendar.Message);
            Assert.Equal(2, calendar.Data!.Days.Count);
            Assert.True(calendar.Data.Days[0].IsOperatingDay);
            Assert.Equal("Holiday", calendar.Data.Days[1].ClosureReason);
            Assert.DoesNotContain("Confidential campus closure",
                System.Text.Json.JsonSerializer.Serialize(calendar.Data));
            Assert.Equal(404, (await TripService(db, state.Fixture).GetScheduleCalendarAsync(
                Guid.NewGuid(), firstDate, holidayDate)).StatusCode);
        }

        var initial = await RunAsync();
        Assert.True(initial.Success, initial.Message);
        Assert.Equal(1, initial.Data!.NewlyPlanned);
        Assert.Equal(0, initial.Data.AlreadyPlanned);
        Assert.Equal(new[] { holidayDate }, initial.Data.SkippedDates);
        Assert.Single(initial.Data.Trips);

        await using (var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var holiday = await db.AcademicCalendarEvents.SingleAsync(x => x.Id == holidayId);
            holiday.IsHoliday = false;
            await db.SaveChangesAsync();
        }
        var reopened = await RunAsync();
        Assert.True(reopened.Success, reopened.Message);
        Assert.Equal(1, reopened.Data!.AlreadyPlanned);
        Assert.Equal(1, reopened.Data.NewlyPlanned);
        Assert.Empty(reopened.Data.SkippedDates);

        await using (var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var holiday = await db.AcademicCalendarEvents.SingleAsync(x => x.Id == holidayId);
            holiday.StartDate = firstDate;
            holiday.IsHoliday = true;
            await db.SaveChangesAsync();
        }
        var replay = await RunAsync();
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(2, replay.Data!.AlreadyPlanned);
        Assert.Equal(0, replay.Data.NewlyPlanned);
        Assert.Empty(replay.Data.SkippedDates);

        await using var verify = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
        Assert.Equal(2, await verify.TransportTrips.CountAsync(x =>
            x.TenantId == state.Fixture.TenantId && x.VehicleId == state.Fixture.VehicleId &&
            x.OperatingDate >= firstDate && x.OperatingDate <= holidayDate &&
            x.Direction == "Pickup" && !x.IsDeleted));
    }

    [Fact]
    public async Task Calendar_aware_schedule_requires_configured_policy_before_any_write()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await PlannedTripAsync(connection);
        var tomorrow = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime).AddDays(1);
        var request = new ScheduleTransportTripsRequestDto
        {
            ClientRequestId = Guid.NewGuid(),
            RouteReference = state.Fixture.RouteReference,
            VehicleReference = state.Fixture.VehicleReference,
            StartDate = tomorrow, EndDate = tomorrow,
            Direction = "Pickup", UseAcademicCalendar = true
        };
        await using var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
        var response = await TripService(db, state.Fixture).ScheduleAsync(request);
        Assert.Equal(409, response.StatusCode);
        Assert.False(response.Success);
        Assert.Equal(0, await db.TransportTrips.CountAsync(x =>
            x.TenantId == state.Fixture.TenantId && x.OperatingDate == tomorrow &&
            x.VehicleId == state.Fixture.VehicleId && !x.IsDeleted));
    }
}
