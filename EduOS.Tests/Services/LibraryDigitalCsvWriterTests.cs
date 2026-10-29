using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Library;
using FluentAssertions;
using System.Text;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LibraryDigitalCsvWriterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Export_is_utf8_bounded_metadata_and_neutralizes_spreadsheet_formulas(bool bangla)
    {
        var data = new[] { new LibraryDigitalResourceDto
        {
            BookTitle = "=HYPERLINK(\"https://example.com\")",
            Title = "+SUM(1,1)", FileName = "@external.pdf",
            ContentType = "application/pdf", SizeBytes = 200,
            IsActive = true, CreatedAt = new DateTime(2026, 10, 29, 1, 0, 0, DateTimeKind.Utc)
        } };
        var bytes = LibraryDigitalCsvWriter.Write(data, bangla);
        bytes.Take(3).Should().Equal((byte)0xEF, (byte)0xBB, (byte)0xBF);
        var text = Encoding.UTF8.GetString(bytes);
        text.Should().Contain("'=HYPERLINK");
        text.Should().Contain("'+SUM");
        text.Should().Contain("'@external.pdf");
        text.Should().NotContain("StorageKey");
        text.Should().NotContain("PersonId");
    }
}
