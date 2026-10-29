using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Enums.Domain;
using FluentAssertions;
using System.Text;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LibraryCopyIntegrityCsvTests
{
    [Fact]
    public void Csv_is_bounded_formula_safe_and_contains_no_borrower_data()
    {
        var row = new LibraryCopyIntegrityDto
        {
            AccessionNumber = "+unsafe",
            BookTitle = "=HYPERLINK(\"x\")",
            BranchCode = "@branch",
            CopyState = BookCopyState.Issued,
            OpenLoanCount = 2,
            StateMismatch = false,
            MultipleOpenLoans = true
        };
        var csv = Encoding.UTF8.GetString(LibraryCopyIntegrityCsvWriter.Write([row], false));
        csv.Should().StartWith("\uFEFF");
        csv.Should().Contain("\"'+unsafe\"");
        csv.Should().Contain("\"'=HYPERLINK(\"\"x\"\")\"");
        csv.Should().Contain("\"'@branch\"");
        csv.Should().Contain("Multiple open loans");
        csv.Should().NotContain("StudentName");
        Action oversized = () => LibraryCopyIntegrityCsvWriter.Write(
            Enumerable.Repeat(row, 101).ToArray(), false);
        oversized.Should().Throw<ArgumentOutOfRangeException>();
        Encoding.UTF8.GetString(LibraryCopyIntegrityCsvWriter.Write([row], true))
            .Should().Contain("অ্যাকসেশন");
    }
}
