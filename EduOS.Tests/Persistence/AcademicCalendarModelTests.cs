using EduOS.Core.Entities.Academic;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace EduOS.Tests.Persistence;

public class AcademicCalendarModelTests
{
    [Fact]
    public void Calendar_policy_and_events_have_tenant_concurrency_and_database_guards()
    {
        using var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"academic-calendar-model-{Guid.NewGuid():N}").Options);
        var policy = context.Model.FindEntityType(typeof(AcademicCalendarPolicy))!;
        var calendarEvent = context.Model.FindEntityType(typeof(AcademicCalendarEvent))!;

        policy.GetDeclaredQueryFilters().Should().NotBeEmpty();
        calendarEvent.GetDeclaredQueryFilters().Should().NotBeEmpty();
        policy.FindProperty(nameof(AcademicCalendarPolicy.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        calendarEvent.FindProperty(nameof(AcademicCalendarEvent.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        policy.GetIndexes().Single(x => x.GetDatabaseName() == "UX_AcademicCalendarPolicies_Tenant_Request").IsUnique.Should().BeTrue();
        policy.GetIndexes().Single(x => x.GetDatabaseName() == "UX_AcademicCalendarPolicies_Tenant_Scope").IsUnique.Should().BeTrue();
        calendarEvent.GetIndexes().Single(x => x.GetDatabaseName() == "UX_AcademicCalendarEvents_Tenant_Request").IsUnique.Should().BeTrue();
        calendarEvent.GetIndexes().Single(x => x.GetDatabaseName() == "UX_AcademicCalendarEvents_Tenant_Scope_Title_Dates").IsUnique.Should().BeTrue();
        policy.GetForeignKeys().Should().OnlyContain(x => x.DeleteBehavior == DeleteBehavior.Restrict);
        calendarEvent.GetForeignKeys().Should().OnlyContain(x => x.DeleteBehavior == DeleteBehavior.Restrict);
    }
}
