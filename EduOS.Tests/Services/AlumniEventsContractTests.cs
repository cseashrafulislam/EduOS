using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AlumniEventsContractTests
{
    [Theory]
    [InlineData(nameof(AlumniApiController.CreateEvent))]
    [InlineData(nameof(AlumniApiController.PublishEvent))]
    [InlineData(nameof(AlumniApiController.CancelEvent))]
    [InlineData(nameof(AlumniApiController.EventReport))]
    [InlineData(nameof(AlumniApiController.EventCampuses))]
    public void Administration_and_export_require_tenant_admin(string method)
    {
        Assert.Contains(typeof(AlumniApiController).GetMethod(method)!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true),
            attribute => ((AuthorizeAttribute)attribute).Roles == "TenantAdmin");
    }

    [Theory]
    [InlineData(nameof(AlumniApiController.Attendance))]
    [InlineData(nameof(AlumniApiController.CheckIn))]
    public void Attendance_requires_tenant_admin(string method)
    {
        Assert.Contains(typeof(AlumniApiController).GetMethod(method)!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true),
            attribute => ((AuthorizeAttribute)attribute).Roles == "TenantAdmin");
    }

    [Fact]
    public void Attendance_checks_consent_campus_rowversion_and_audit()
    {
        var source = Read("EduOS.Service", "Services", "Alumni", "AlumniAttendanceService.cs");
        Assert.Contains("p.DirectoryOptIn = 1", source);
        Assert.Contains("CampusGrantSql", source);
        Assert.Contains("s.PublicId = {3}", source);
        Assert.Contains("e.StartsAtUtc <= {4} AND e.EndsAtUtc >= {4}", source);
        Assert.Contains("CheckInReversed", source);
        Assert.Contains("CryptographicOperations.FixedTimeEquals", source);
        Assert.Contains("IsolationLevel.Serializable", source);
        var migration = Read("EduOS.Persistence", "Migrations",
            "20261101073000_V3AlumniAttendanceCheckIn.cs");
        Assert.Contains("CK_AlumniRsvp_CheckIn", migration);
    }

    [Fact]
    public void Events_enforce_maker_checker_tenant_campus_and_graduate_ownership()
    {
        var source = Read("EduOS.Service", "Services", "Alumni", "AlumniEventsService.cs");
        Assert.Contains("current.CreatedByUserId == actor", source);
        Assert.Contains("s.UserId = {1}", source);
        Assert.Contains("s.PublicId = {2}", source);
        Assert.Contains("p.DirectoryOptIn = 1", source);
        Assert.Contains("UserCampusAccesses", source);
        Assert.Contains("IsolationLevel.Serializable", source);
        Assert.Contains("CryptographicOperations.FixedTimeEquals", source);
        Assert.Contains("AlumniEventCommands", source);
        Assert.Contains("replay == null || replay.MyResponse != request.Response", source);
        Assert.Contains("EligibleEventRsvpFrom", source);
        Assert.Contains("p.CampusId = e.CampusId", source);
        Assert.Contains("x.FeesCleared = 1", source);
        Assert.Contains("r.CheckedInAtUtc IS NOT NULL", source);
        Assert.Contains("AlumniEventAudits", source);
        Assert.DoesNotContain("IgnoreQueryFilters", source);
    }

    [Fact]
    public void Migration_has_tenant_owned_unique_rsvp_and_event_command_constraints()
    {
        var source = Read("EduOS.Persistence", "Migrations", "20261101070000_V3AlumniEventsEngagement.cs");
        Assert.Contains("UX_AlumniEventRsvps_Event_Profile", source);
        Assert.Contains("UX_AlumniEventCommands_Request", source);
        Assert.Contains("UX_AlumniEvents_Create", source);
        Assert.Contains("CK_AlumniEvents_Time", source);
        Assert.Contains("RowVersion", source);
        var consent = Read("EduOS.Service", "Services", "Alumni", "AlumniService.cs");
        Assert.Contains("DELETE FROM dbo.AlumniEventRsvps", consent);
    }

    private static string Read(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }
}
