using System;
using System.Linq;
using System.Text;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Library;
using Microsoft.AspNetCore.Authorization;
using Xunit;
using ApiLibraryController = EduOS.App.Controllers.Api.LibraryController;

namespace EduOS.Tests.Services;

public sealed class LibraryMyLoanStatementTests
{
    [Fact]
    public void Self_service_loan_csv_is_utf8_safe_and_contains_no_borrower_identity()
    {
        var csv = Encoding.UTF8.GetString(LibraryMyLoanCsvWriter.Write(new[] {
            new BookIssueDto {
                BookTitle = "=HYPERLINK(\"https://invalid.example\")",
                AccessionNumber = "  +SUM(1,1)",
                StudentName = "Private borrower",
                IssueDate = new DateOnly(2026, 10, 1),
                DueDate = new DateOnly(2026, 10, 15),
                FineAmount = 12.50m
            }
        }, false));
        Assert.StartsWith("\uFEFF", csv);
        Assert.Contains("\"'=HYPERLINK", csv);
        Assert.Contains("\"'  +SUM", csv.Replace("\"  '", "\"'  "));
        Assert.DoesNotContain("Private borrower", csv);
        Assert.Contains("12.50", csv);
    }

    [Theory]
    [InlineData(nameof(ApiLibraryController.MyIssues))]
    [InlineData(nameof(ApiLibraryController.ExportMyIssues))]
    public void Loan_statement_endpoints_require_student_role(string method)
    {
        var action = typeof(ApiLibraryController).GetMethod(method)!;
        Assert.Contains(action.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), attribute => attribute.Roles == "Student");
    }
}
