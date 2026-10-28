using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces;
using EduOS.Service.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LoginActivityExportTests
{
    [Fact]
    public async Task Anonymous_session_cannot_read_or_export_history()
    {
        var user = new Mock<ICurrentUserService>();
        var service = new LoginActivityService(null!, user.Object);
        Assert.Equal(403, (await service.GetMyHistoryAsync(1, 20, null)).StatusCode);
        Assert.Equal(403, (await service.ExportMyHistoryCsvAsync(null)).StatusCode);
    }

    [Fact]
    public async Task Invalid_date_range_is_rejected_before_database_query()
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.UserId).Returns(17);
        user.SetupGet(x => x.TenantId).Returns(42);
        var service = new LoginActivityService(null!, user.Object);
        var from = new DateOnly(2026, 10, 28);
        var before = new DateOnly(2026, 10, 27);
        var old = new DateOnly(2025, 1, 1);
        Assert.Equal(400, (await service.GetMyHistoryAsync(1, 20, null, from: from)).StatusCode);
        Assert.Equal(400, (await service.GetMyHistoryAsync(1, 20, null, from: from, to: before)).StatusCode);
        Assert.Equal(400, (await service.ExportMyHistoryCsvAsync(null, from: old, to: from)).StatusCode);
        Assert.Equal(400, (await service.ExportMyHistoryCsvAsync(null, to: from)).StatusCode);
    }

    [Theory]
    [InlineData("=2+2", "\"'=2+2\"")]
    [InlineData(" +SUM(1,2)", "\"' +SUM(1,2)\"")]
    [InlineData("safe, \"quoted\"", "\"safe, \"\"quoted\"\"\"")]
    public void Csv_cells_are_formula_safe_and_escaped(string value, string expected) =>
        Assert.Equal(expected, LoginActivityService.CsvCell(value));

    [Fact]
    public void Export_is_authenticated_read_only_and_no_store()
    {
        var type = typeof(LoginActivityApiController);
        Assert.NotEmpty(type.GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.NotEmpty(type.GetCustomAttributes(typeof(ResponseCacheAttribute), true));
        var method = type.GetMethod(nameof(LoginActivityApiController.Export))!;
        Assert.NotEmpty(method.GetCustomAttributes(typeof(HttpGetAttribute), true));
    }
}
