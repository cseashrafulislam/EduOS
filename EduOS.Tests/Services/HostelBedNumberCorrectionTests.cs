using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelBedNumberCorrectionTests
{
    [Theory]
    [InlineData(" a-01 ", "A-01")]
    [InlineData("B  2", "B  2")]
    [InlineData("bed-3", "BED-3")]
    public void Bed_label_normalizes_for_duplicate_detection(string input, string expected)
    {
        Assert.True(HostelService.IsValidBedNumber(input));
        Assert.Equal(expected, HostelService.NormalizeBedNumber(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Bed\n3")]
    [InlineData("Bed\t3")]
    public void Rejects_blank_or_control_character_labels(string input)
    {
        Assert.False(HostelService.IsValidBedNumber(input));
    }

    [Fact]
    public void Rejects_labels_over_fifty_characters()
    {
        Assert.True(HostelService.IsValidBedNumber(new string('A', 50)));
        Assert.False(HostelService.IsValidBedNumber(new string('A', 51)));
    }
}
