using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed partial class StudentPromotionServiceTests
{
    [Fact]
    public async Task Promotion_history_page_uses_original_enrollment_and_bounded_paging()
    {
        var options = Options();
        var fixture = await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101));
        (await service.PromoteAsync(fixture.StudentReference, Request(fixture)))
            .Success.Should().BeTrue();

        var first = await service.GetHistoryPageAsync(fixture.StudentReference, 1, 1);
        first.Success.Should().BeTrue(first.Message);
        first.Data!.TotalCount.Should().Be(1);
        first.Data.PageSize.Should().Be(1);
        first.Data.Items.Should().ContainSingle();
        first.Data.Items[0].FromRoll.Should().Be("4");
        first.Data.Items[0].ToRoll.Should().Be("5");
        first.Data.Items[0].FromAcademicLevelId.Should().Be(fixture.SourceLevelId);
        first.Data.Items[0].ToAcademicLevelId.Should().Be(fixture.TargetLevelId);

        var beyond = await service.GetHistoryPageAsync(fixture.StudentReference, 2, 1);
        beyond.Success.Should().BeTrue();
        beyond.Data!.TotalCount.Should().Be(1);
        beyond.Data.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task Promotion_history_rejects_invalid_paging(int page, int pageSize)
    {
        var options = Options();
        var fixture = await SeedAsync(options);
        await using var db = Context(options, 101);
        (await Service(db, new User(101))
            .GetHistoryPageAsync(fixture.StudentReference, page, pageSize))
            .StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Promotion_history_denies_foreign_tenant_and_teacher()
    {
        var options = Options();
        var fixture = await SeedAsync(options);
        await using var foreign = Context(options, 202);
        (await Service(foreign, new User(202))
            .GetHistoryPageAsync(fixture.StudentReference)).StatusCode.Should().Be(404);
        await using var teacher = Context(options, 101);
        (await Service(teacher, new User(101, "Teacher"))
            .GetHistoryPageAsync(fixture.StudentReference)).StatusCode.Should().Be(403);
    }
}
