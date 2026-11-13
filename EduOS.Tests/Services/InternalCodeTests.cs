using EduOS.Service.Services.Platform;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class InternalCodeTests
{
    [Fact]
    public void Generated_master_codes_are_unique_and_fit_column()
    {
        var codes = Enumerable.Range(0, 1000).Select(_ => InternalCode.New("ADP", 30)).ToArray();
        Assert.Equal(1000, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code =>
        {
            Assert.StartsWith("ADP-", code);
            Assert.Equal(30, code.Length);
            Assert.Matches("^ADP-[A-F0-9]{26}$", code);
        });
    }

    [Fact]
    public void Invalid_prefix_or_length_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => InternalCode.New("adp"));
        Assert.Throws<ArgumentOutOfRangeException>(() => InternalCode.New("ADP", 16));
    }
}
