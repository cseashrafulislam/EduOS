using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed partial class StudentPromotionServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Promotion_replay_rejects_changed_source_or_audit_note(bool changeSource)
    {
        var options = Options();
        var fixture = await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101));
        var original = Request(fixture);
        (await service.PromoteAsync(fixture.StudentReference, original)).Success.Should().BeTrue();

        var retry = Request(fixture);
        retry.ClientRequestId = original.ClientRequestId;
        if (changeSource) retry.SourceEnrollmentId++;
        else retry.Note = "Changed progression justification";

        var response = await service.PromoteAsync(fixture.StudentReference, retry);
        response.StatusCode.Should().Be(409);
        (await db.StudentPromotionRecords.CountAsync()).Should().Be(1);
        (await db.StudentEnrollments.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Promotion_replay_accepts_equivalent_trimmed_audit_note()
    {
        var options = Options();
        var fixture = await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101));
        var original = Request(fixture);
        (await service.PromoteAsync(fixture.StudentReference, original)).Success.Should().BeTrue();

        var retry = Request(fixture);
        retry.ClientRequestId = original.ClientRequestId;
        retry.Note = "  Annual promotion  ";
        var response = await service.PromoteAsync(fixture.StudentReference, retry);
        response.Success.Should().BeTrue();
        response.Data!.AlreadyProcessed.Should().BeTrue();
        (await db.StudentPromotionRecords.CountAsync()).Should().Be(1);
    }
}
