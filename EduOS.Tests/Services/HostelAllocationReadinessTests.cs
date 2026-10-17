using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelAllocationReadinessTests
{
    private static readonly DateOnly Today = new(2026, 10, 18);

    [Fact]
    public void Future_reservation_is_scheduled_when_eligible()
    {
        Assert.Equal("Scheduled arrival", Classify(Today.AddDays(2)));
    }

    [Fact]
    public void Current_allocation_is_not_reported_as_an_exception()
    {
        Assert.Equal("Ready", Classify(Today));
    }

    [Fact]
    public void Inactive_bed_overrides_future_arrival()
    {
        Assert.Equal("Inactive bed", Classify(Today.AddDays(2), bedActive: false));
    }

    [Fact]
    public void Cross_campus_allocation_requires_attention()
    {
        Assert.Equal("Enrollment campus mismatch", Classify(Today, campusMatch: false));
    }

    [Fact]
    public void Non_current_enrollment_requires_attention()
    {
        Assert.Equal("Inactive enrollment", Classify(Today, current: false));
    }

    [Fact]
    public void Inactive_student_requires_attention()
    {
        Assert.Equal("Inactive student", Classify(Today, studentActive: false));
    }

    private static string Classify(DateOnly start, bool campusActive = true, bool facilityActive = true,
        bool roomActive = true, bool bedActive = true, bool campusMatch = true,
        bool current = true, bool enrollmentActive = true, bool studentActive = true) =>
        HostelService.ClassifyAllocationReadiness(campusActive, facilityActive, roomActive,
            bedActive, campusMatch, current, enrollmentActive, studentActive, start, Today);
}
