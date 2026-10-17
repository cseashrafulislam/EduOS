using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces;
using EduOS.Service.Services.SaaS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TenantFeatureAuditExportTests
{
    [Fact]
    public async Task Anonymous_export_is_denied_without_querying_database()
    {
        var user = new Mock<ICurrentUserService>();
        var service = new TenantFeatureAuditService(null!, user.Object,
            NullLogger<TenantFeatureAuditService>.Instance);
        Assert.Equal(403, (await service.ExportCsvAsync()).StatusCode);
    }

    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData(" +SUM(1,2)", "\"' +SUM(1,2)\"")]
    [InlineData("Safe, \"quoted\"", "\"Safe, \"\"quoted\"\"\"")]
    [InlineData(null, "\"\"")]
    public void Csv_cells_are_quoted_and_formula_neutralized(string? value, string expected) =>
        Assert.Equal(expected, TenantFeatureAuditService.CsvCell(value));

    [Fact]
    public void Export_is_authorized_and_read_only()
    {
        var controller = typeof(TenantFeatureAuditApiController);
        Assert.Equal("TenantAdmin", Assert.Single(controller.GetCustomAttributes(
            typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(ResponseCacheAttribute), true));
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        Assert.NotEmpty(controller.GetMethod("Export")!.GetCustomAttributes(typeof(HttpGetAttribute), true));
    }
}
