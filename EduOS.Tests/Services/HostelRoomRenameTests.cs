using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelRoomRenameTests
{
    [Theory]
    [InlineData(" a-101 ", "A-101")]
    [InlineData("B  2", "B  2")]
    [InlineData("r-3", "R-3")]
    public void Canonical_room_number_normalizes_for_duplicate_detection(string input, string expected)
    {
        Assert.True(HostelService.IsValidRoomNumber(input));
        Assert.Equal(expected, HostelService.NormalizeRoomNumber(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Room\n3")]
    [InlineData("Room\t3")]
    public void Rejects_blank_or_control_character_room_numbers(string input)
    {
        Assert.False(HostelService.IsValidRoomNumber(input));
    }

    [Fact]
    public void Rejects_room_numbers_over_fifty_characters()
    {
        Assert.True(HostelService.IsValidRoomNumber(new string('A', 50)));
        Assert.False(HostelService.IsValidRoomNumber(new string('A', 51)));
    }
}
