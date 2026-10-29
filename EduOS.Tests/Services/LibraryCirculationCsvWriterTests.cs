using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Enums.Domain;
using FluentAssertions;
using System.Text;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LibraryCirculationCsvWriterTests
{
    [Fact]
    public void Csv_is_bounded_bilingual_formula_safe_and_omits_private_ids()
    {
        var rows = new[] { new LibraryCirculationRowDto
        {
            CampusId = 7, DaysOverdue = 5, IsOverdue = true,
            Issue = new BookIssueDto
            {
                Reference = Guid.NewGuid(), StudentReference = Guid.NewGuid(),
                StudentName = " =HYPERLINK(\"x\")", BookTitle = "+unsafe",
                AccessionNumber = "A,\"1\"", State = BookIssueState.Issued,
                IssueDate = new DateOnly(2026, 10, 1),
                DueDate = new DateOnly(2026, 10, 20)
            }
        } };
        var csv = Encoding.UTF8.GetString(LibraryCirculationCsvWriter.Write(rows, false));
        csv.Should().StartWith("\uFEFF");
        csv.Should().Contain("\"' =HYPERLINK(\"\"x\"\")\"");
        csv.Should().Contain("\"'+unsafe\"");
        csv.Should().Contain("\"A,\"\"1\"\"\"");
        csv.Should().Contain("2026-10-20");
        csv.Should().NotContain(rows[0].Issue.Reference.ToString());
        csv.Should().NotContain(rows[0].Issue.StudentReference.ToString());
        Encoding.UTF8.GetString(LibraryCirculationCsvWriter.Write(rows, true))
            .Should().Contain("শিক্ষার্থী").And.Contain("ইস্যুকৃত");
        Action tooMany = () => LibraryCirculationCsvWriter.Write(
            Enumerable.Repeat(rows[0], 101).ToArray(), false);
        tooMany.Should().Throw<ArgumentOutOfRangeException>();
    }
}
