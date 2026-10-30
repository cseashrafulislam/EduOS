using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Library;
using System.Text;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LibraryLiabilityCsvTests
{
    [Fact]
    public void Finance_export_is_excel_safe_and_preserves_historical_amounts()
    {
        var bytes = LibraryLiabilityCsvWriter.Write(new[]
        {
            new LibraryLiabilityDto
            {
                AssessmentDate = new DateOnly(2026, 10, 30), CampusId = 2,
                BookTitle = "='BAD'", AccessionNumber = "LIB-001",
                StudentName = "=HYPERLINK(\"https://bad.test\")", StudentCode = "A1",
                Kind = LibraryLiabilityKind.Lost, Amount = 175.50m,
                State = LibraryLiabilityState.Posted, PaidAmount = 25m,
                DueAmount = 150.50m, InvoiceNumber = "LIB-0123", Reason = "+cmd"
            }
        }, false);
        var value = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("\uFEFFAssessment date", value);
        Assert.Contains("\"'=HYPERLINK(\"\"https://bad.test\"\")\"", value);
        Assert.Contains("\"175.50\"", value);
        Assert.Contains("\"150.50\"", value);
        Assert.Contains("\"'+cmd\"", value);
        Assert.DoesNotContain(",=HYPERLINK", value);
    }
}
