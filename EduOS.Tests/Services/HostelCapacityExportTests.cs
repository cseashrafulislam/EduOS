using System.Reflection;
using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelCapacityExportTests
{
    [Theory]
    [InlineData("Room 1", "\"Room 1\"")]
    [InlineData("A,\"B\"", "\"A,\"\"B\"\"\"")]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("  @cmd", "\"'  @cmd\"")]
    [InlineData("-2", "\"'-2\"")]
    [InlineData("North\r\nWing", "\"North  Wing\"")]
    public void Csv_cell_escapes_quotes_newlines_and_formula_prefixes(string input, string expected)
    {
        var method = typeof(HostelService).GetMethod("CsvCell", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(expected, method!.Invoke(null, new object?[] { input }));
    }

    [Fact]
    public void Csv_cell_handles_null()
    {
        var method = typeof(HostelService).GetMethod("CsvCell", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal("\"\"", method!.Invoke(null, new object?[] { null }));
    }
}
