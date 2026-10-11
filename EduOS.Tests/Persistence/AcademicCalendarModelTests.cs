using EduOS.Core.Entities.Academic;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace EduOS.Tests.Persistence;

public class AcademicCalendarModelTests
{
    [Fact]
    public void Calendar_policy_and_events_have_tenant_concurrency_and_database_guards()
    {
        using var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"academic-calendar-model-{Guid.NewGuid():N}").Options);
        // Relational constraints must be inspected through EF's design-time model.
        var model = context.GetService<IDesignTimeModel>().Model;
        var policy = model.FindEntityType(typeof(AcademicCalendarPolicy))!;
        var calendarEvent = model.FindEntityType(typeof(AcademicCalendarEvent))!;

        policy.GetDeclaredQueryFilters().Should().NotBeEmpty();
        calendarEvent.GetDeclaredQueryFilters().Should().NotBeEmpty();
        policy.FindProperty(nameof(AcademicCalendarPolicy.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        calendarEvent.FindProperty(nameof(AcademicCalendarEvent.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        policy.GetIndexes().Should().ContainSingle(x =>
            x.IsUnique && x.GetDatabaseName() == "UX_AcademicCalendarPolicies_Tenant_Campus" &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] {
                nameof(AcademicCalendarPolicy.TenantId), nameof(AcademicCalendarPolicy.CampusId)
            }));
        calendarEvent.GetIndexes().Should().ContainSingle(x =>
            x.IsUnique && x.GetDatabaseName() == "UX_AcademicCalendarEvents_Tenant_NaturalKey" &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] {
                nameof(AcademicCalendarEvent.TenantId), nameof(AcademicCalendarEvent.CampusId),
                nameof(AcademicCalendarEvent.AcademicYearId), nameof(AcademicCalendarEvent.AcademicTermId),
                nameof(AcademicCalendarEvent.EventType), nameof(AcademicCalendarEvent.Title),
                nameof(AcademicCalendarEvent.StartDate), nameof(AcademicCalendarEvent.EndDate)
            }));
        calendarEvent.GetCheckConstraints().Should().Contain(x => x.Name == "CK_AcademicCalendarEvent_DateRange");
        policy.GetForeignKeys().Should().OnlyContain(x => x.DeleteBehavior == DeleteBehavior.Restrict);
        calendarEvent.GetForeignKeys().Should().OnlyContain(x => x.DeleteBehavior == DeleteBehavior.Restrict);
    }
}
