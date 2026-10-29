using EduOS.App.Controllers;
using EduOS.App.Reporting;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Interfaces.IServices;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Text;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class FeeInstallmentBillingAuditExportTests
{
    private static readonly Guid Plan = Guid.Parse("c1295bd6-a0b7-46bb-978c-9b4b3e112ab2");

    private static StudentFeeInstallmentBillingAuditDto Evidence() => new()
    {
        PlanReference = Plan, StudentCode = "=HYPERLINK(\"unsafe\")",
        CampusId = 12, CurrencyCode = "BDT", PlanStateCode = 3,
        Months = 1, BilledMonths = 0, ReviewMonths = 1,
        ContractGross = 100m, FrozenLineGross = 100m,
        PlanIssues = ["@injected"],
        Rows = [new StudentFeeInstallmentBillingAuditMonthDto
        {
            MonthStart = new DateOnly(2026, 2, 1), ContractDueDate = new DateOnly(2026, 2, 28),
            ContractGross = 100m, InvoiceNumber = "+SUM(1,1)",
            InvoiceNet = 80.25m, InvoiceDue = 80.25m,
            JournalLinked = true, Status = "Review",
            Issues = ["Needs \"review\", now"]
        }]
    };

    [Fact]
    public void Csv_preserves_invariant_money_and_prevents_spreadsheet_formula_injection()
    {
        var csv = FeeInstallmentBillingAuditCsv.Build(Evidence());
        csv.Should().StartWith("\uFEFF");
        csv.Should().Contain("\"'=HYPERLINK");
        csv.Should().Contain("\"'+SUM(1,1)\"");
        csv.Should().Contain("\"'@injected\"");
        csv.Should().Contain("80.25");
        csv.Should().Contain("Link only");
        csv.Should().Contain("Needs \"\"review\"\", now");
        csv.Should().NotContain("80,25");
    }

    [Fact]
    public async Task Csv_and_print_each_recheck_authorized_service_and_fail_closed()
    {
        var service = new Mock<IFeeBillingService>();
        service.Setup(x => x.GetInstallmentBillingAuditAsync(Plan, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<StudentFeeInstallmentBillingAuditDto>.SuccessResponse(Evidence()));
        var controller = new FeeInstallmentAuditExportsController(service.Object);
        var csv = await controller.ExportCsv(Plan);
        var file = csv.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Contain("text/csv");
        Encoding.UTF8.GetString(file.FileContents).Should().Contain("Review");
        file.FileDownloadName.Should().Contain(Plan.ToString("N"));
        var print = await controller.Print(Plan);
        print.Should().BeOfType<ViewResult>().Which.Model.Should()
            .BeOfType<StudentFeeInstallmentBillingAuditDto>();
        service.Verify(x => x.GetInstallmentBillingAuditAsync(Plan, It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        service.Setup(x => x.GetInstallmentBillingAuditAsync(Plan, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<StudentFeeInstallmentBillingAuditDto>.ErrorResponse("Plan unavailable.", 404));
        (await controller.ExportCsv(Plan)).Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(404);
        (await controller.Print(Plan)).Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(404);
        service.Verify(x => x.GetInstallmentBillingAuditAsync(Plan, It.IsAny<CancellationToken>()),
            Times.Exactly(4));
    }
}
