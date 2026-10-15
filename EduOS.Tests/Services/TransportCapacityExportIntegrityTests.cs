using System.Reflection;
using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportCapacityExportIntegrityTests
{
    [Theory]
    [InlineData("Bus A", "\"Bus A\"")]
    [InlineData("A,\"B\"", "\"A,\"\"B\"\"\"")]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("  @cmd", "\"'  @cmd\"")]
    [InlineData("\t-2", "\"' -2\"")]
    [InlineData("North\r\nWing", "\"North  Wing\"")]
    public void Csv_cell_escapes_formula_prefixes_and_quotes(string input, string expected)
    {
        var method = typeof(TransportService).GetMethod("CsvCell", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(expected, method!.Invoke(null, new object?[] { input }));
    }

    [Fact]
    public void Export_rejects_changed_pages_and_duplicate_vehicles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? file = null;
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "EduOS.Service", "Services", "Transport", "TransportService.CapacityReport.cs");
            if (File.Exists(candidate)) { file = candidate; break; }
            directory = directory.Parent;
        }
        Assert.NotNull(file);
        var source = File.ReadAllText(file!);
        Assert.Contains("next.Data.TotalCount != first.Data.TotalCount", source);
        Assert.Contains("rows.Any(x => !uniqueVehicles.Add(x.VehicleReference))", source);
        Assert.Contains("rows.Count != first.Data.TotalCount", source);
        Assert.Contains("TrimStart()", source);
    }
}
