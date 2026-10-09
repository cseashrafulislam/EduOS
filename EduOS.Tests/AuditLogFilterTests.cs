using EduOS.Core.DTOs.System;
using Xunit;

namespace EduOS.Tests;

public sealed class AuditLogFilterTests
{
    [Fact]
    public void Defaults_use_safe_page_and_page_size()
    {
        var filter = new AuditLogFilterDto();
        Assert.Equal(1, filter.Page);
        Assert.Equal(10, filter.PageSize);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-100, 1)]
    [InlineData(1_000_001, 1_000_000)]
    public void Page_is_clamped(int requested, int expected)
    {
        var filter = new AuditLogFilterDto { Page = requested };
        Assert.Equal(expected, filter.Page);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(101, 100)]
    public void Page_size_is_bounded(int requested, int expected)
    {
        var filter = new AuditLogFilterDto { PageSize = requested };
        Assert.Equal(expected, filter.PageSize);
    }
}
