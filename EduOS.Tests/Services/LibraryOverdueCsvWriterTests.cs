using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Library;
using FluentAssertions;
using System.Text;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LibraryOverdueCsvWriterTests
{
    [Fact]
    public void Csv_is_utf8_bounded_and_escapes_spreadsheet_formulas_and_quotes()
    {
        var rows = new[] { new LibraryOverdueBranchDto
        {
            CampusId = 4, BranchCode = "+unsafe", BranchName = "=HYPERLINK(\"x\")",
            TotalOverdue = 3, Days1To7 = 1, Days8To30 = 1, Days31Plus = 1,
            OldestDueDate = new DateOnly(2026, 1, 2)
        } };
        var csv = Encoding.UTF8.GetString(LibraryOverdueCsvWriter.Write(rows, false));
        csv.Should().StartWith("\uFEFF");
        csv.Should().Contain("\"'+unsafe\"");
        csv.Should().Contain("\"'=HYPERLINK(\"\"x\"\")\"");
        csv.Should().Contain("2026-01-02");
        Action tooMany = () => LibraryOverdueCsvWriter.Write(
            Enumerable.Repeat(rows[0], 101).ToArray(), false);
        tooMany.Should().Throw<ArgumentOutOfRangeException>();
        Encoding.UTF8.GetString(LibraryOverdueCsvWriter.Write(rows, true))
            .Should().Contain("মেয়াদোত্তীর্ণ");
    }
}
