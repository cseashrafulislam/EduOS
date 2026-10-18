using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.System;
using EduOS.Core.Interfaces;
using EduOS.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AuditLogCsvExportTests
{
    [Fact]
    public async Task Unauthenticated_export_is_rejected_before_database_access()
    {
        var user = new Mock<ICurrentUserService>();
        var service = new AuditLogService(null!, user.Object, NullLogger<AuditLogService>.Instance);
        Assert.Equal(403, (await service.ExportCsvAsync(new AuditLogFilterDto())).StatusCode);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData(" +SUM(1,2)", "\"' +SUM(1,2)\"")]
    [InlineData("Normal, \"value\"", "\"Normal, \"\"value\"\"\"")]
    [InlineData(null, "\"\"")]
    public void Csv_cells_quote_and_neutralize_formulas(string? value, string expected) =>
        Assert.Equal(expected, AuditLogService.CsvCell(value));

    [Fact]
    public void Export_endpoint_is_authorized_and_read_only()
    {
        var controller = typeof(AuditLogController);
        Assert.Equal("TenantAdmin,Principal", Assert.Single(controller.GetCustomAttributes(
            typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        var action = controller.GetMethod(nameof(AuditLogController.Export));
        Assert.NotNull(action);
        Assert.NotEmpty(action!.GetCustomAttributes(typeof(HttpGetAttribute), true));
        Assert.Empty(action.GetCustomAttributes(typeof(HttpPostAttribute), true));
    }
}
