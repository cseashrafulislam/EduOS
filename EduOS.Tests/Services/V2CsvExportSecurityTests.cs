using System.Text;
using EduOS.App.Controllers.Api;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Enums.Domain;
using Xunit;

namespace EduOS.Tests.Services;

/// <summary>Cross-module V2 export security: no untrusted field may execute as a spreadsheet formula.</summary>
public sealed class V2CsvExportSecurityTests
{
    [Theory]
    [InlineData(" =1+1", "\"' =1+1\"")]
    [InlineData("\t@SUM(A1)", "\"' @SUM(A1)\"")]
    [InlineData("\uFEFF+1", "\"' +1\"")]
    [InlineData("\u200B-2", "\"' -2\"")]
    [InlineData("\u0001=2", "\"' =2\"")]
    [InlineData("Normal,\"quoted\"", "\"Normal,\"\"quoted\"\"\"")]
    public void Untrusted_csv_cells_are_normalized_and_formula_safe(string input, string expected)
        => Assert.Equal(expected, PrivateCsvCell.Escape(input));

    [Fact]
    public void Library_overdue_circulation_and_reservation_exports_share_formula_protection()
    {
        var overdue = new LibraryOverdueBranchDto
        {
            CampusId = 1, BranchCode = " \t=2+2", BranchName = "\uFEFF@SUM(A1)",
            OldestDueDate = new DateOnly(2026, 10, 1)
        };
        var overdueCsv = Encoding.UTF8.GetString(LibraryOverdueCsvWriter.Write([overdue], false));
        Assert.Contains("\"'  =2+2\"", overdueCsv);
        Assert.Contains("\"' @SUM(A1)\"", overdueCsv);

        var circulation = new LibraryCirculationRowDto
        {
            Issue = new BookIssueDto
            {
                StudentName = "\u200B=HYPERLINK(\"x\")",
                BookTitle = " \t+1",
                IssueDate = new DateOnly(2026, 10, 1),
                DueDate = new DateOnly(2026, 10, 2),
                State = BookIssueState.Issued
            }
        };
        var circulationCsv = Encoding.UTF8.GetString(LibraryCirculationCsvWriter.Write([circulation], false));
        Assert.Contains("\"' =HYPERLINK(\"\"x\"\")\"", circulationCsv);
        Assert.Contains("\"'  +1\"", circulationCsv);

        var reservation = new BookReservationDto
        {
            BookTitle = "\uFEFF=1+1",
            ReservedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var reservationCsv = Encoding.UTF8.GetString(LibraryReservationCsvWriter.Write([reservation], false));
        Assert.Contains("\"' =1+1\"", reservationCsv);
    }
}
