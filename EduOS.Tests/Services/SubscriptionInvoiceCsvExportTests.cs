using AutoMapper;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Service.Services.SaaS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class SubscriptionInvoiceCsvExportTests
{
    private static Mock<ICurrentUserService> User(long tenantId = 42, bool superAdmin = false)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.TenantId).Returns(tenantId);
        user.SetupGet(x => x.IsSuperAdmin).Returns(superAdmin);
        return user;
    }

    private static SubscriptionInvoiceService Service(Mock<ISubscriptionInvoiceRepository> repo,
        Mock<ICurrentUserService> user, Mock<IMapper>? mapper = null) =>
        new(repo.Object, user.Object, (mapper ?? new Mock<IMapper>()).Object,
            NullLogger<SubscriptionInvoiceService>.Instance);

    [Fact]
    public async Task Export_contains_historical_lines_and_escapes_spreadsheet_formulas()
    {
        var repo = new Mock<ISubscriptionInvoiceRepository>(MockBehavior.Strict);
        repo.Setup(x => x.GetByIdForSystemAsync(15, 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionInvoice { Id = 15, TenantId = 42, InvoiceNumber = "+INV-15" });
        repo.Setup(x => x.GetLinesForInvoiceAsync(42, 15, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SubscriptionInvoiceLine>
            {
                new() { TenantId = 42, SubscriptionInvoiceId = 15, Description = "=SUM(1,1)",
                    Quantity = 2m, UnitPrice = 17.5m, Amount = 35m },
                new() { TenantId = 42, SubscriptionInvoiceId = 15, Description = "Plan, \"special\"",
                    Quantity = 1m, UnitPrice = 5m, Amount = 5m }
            });
        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<SubscriptionInvoiceDto>(It.IsAny<object>()))
            .Returns(new SubscriptionInvoiceDto
            {
                Id = 15, InvoiceNumber = "+INV-15", CurrencyCode = "BDT",
                Subtotal = 40m, TaxAmount = 2m, TotalAmount = 42m,
                PaidAmount = 7m, DueAmount = 35m,
                InvoiceDate = new DateOnly(2026, 10, 26), DueDate = new DateOnly(2026, 11, 2)
            });
        var result = await Service(repo, User(), mapper).ExportCsvAsync(15);
        Assert.True(result.Success);
        Assert.Contains("\"'+INV-15\"", result.Data);
        Assert.Contains("\"'=SUM(1,1)\",2,17.5,35", result.Data);
        Assert.Contains("\"Plan, \"\"special\"\"\",1,5,5", result.Data);
        Assert.Contains("Outstanding,35", result.Data);
        Assert.Contains("Invoice Date,\"2026-10-26\"", result.Data);
        repo.VerifyAll();
    }

    [Fact]
    public async Task Export_rejects_foreign_invoice_without_reading_lines()
    {
        var repo = new Mock<ISubscriptionInvoiceRepository>(MockBehavior.Strict);
        repo.Setup(x => x.GetByIdForSystemAsync(15, 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionInvoice?)null);
        var result = await Service(repo, User()).ExportCsvAsync(15);
        Assert.Equal(404, result.StatusCode);
        repo.Verify(x => x.GetLinesForInvoiceAsync(It.IsAny<long>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
        repo.VerifyAll();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(42, true)]
    public async Task Export_requires_tenant_self_service_session(long tenantId, bool superAdmin)
    {
        var repo = new Mock<ISubscriptionInvoiceRepository>(MockBehavior.Strict);
        var result = await Service(repo, User(tenantId, superAdmin)).ExportCsvAsync(15);
        Assert.Equal(403, result.StatusCode);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public void Export_endpoint_is_tenant_admin_read_only_and_non_cacheable()
    {
        var type = typeof(SubscriptionController);
        Assert.Equal("TenantAdmin", Assert.Single(type.GetCustomAttributes(
            typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        var method = type.GetMethod(nameof(SubscriptionController.ExportInvoice))!;
        Assert.Equal("invoices/{id:long}/export", Assert.Single(method.GetCustomAttributes(
            typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>()).Template);
        Assert.True(Assert.Single(method.GetCustomAttributes(
            typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>()).NoStore);
    }
}
