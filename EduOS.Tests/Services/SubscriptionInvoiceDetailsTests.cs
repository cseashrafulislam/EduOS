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

public sealed class SubscriptionInvoiceDetailsTests
{
    private static Mock<ICurrentUserService> User(long tenantId = 42, bool superAdmin = false)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.TenantId).Returns(tenantId);
        user.SetupGet(x => x.IsSuperAdmin).Returns(superAdmin);
        return user;
    }

    private static SubscriptionInvoiceService Service(
        Mock<ISubscriptionInvoiceRepository> repo, Mock<ICurrentUserService> user,
        Mock<IMapper>? mapper = null) =>
        new(repo.Object, user.Object, (mapper ?? new Mock<IMapper>()).Object,
            NullLogger<SubscriptionInvoiceService>.Instance);

    [Fact]
    public async Task Detail_reads_only_lines_for_authenticated_invoice_tenant()
    {
        var repo = new Mock<ISubscriptionInvoiceRepository>(MockBehavior.Strict);
        var invoice = new SubscriptionInvoice
        {
            Id = 15, TenantId = 42, InvoiceNumber = "INV-15",
            Subtotal = 35m, TotalAmount = 35m, DueAmount = 35m
        };
        repo.Setup(x => x.GetByIdForSystemAsync(15, 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        repo.Setup(x => x.GetLinesForInvoiceAsync(42, 15, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SubscriptionInvoiceLine>
            {
                new() { Id = 9, TenantId = 42, SubscriptionInvoiceId = 15,
                    Description = "Monthly plan", Quantity = 2m, UnitPrice = 17.50m, Amount = 35m }
            });
        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<SubscriptionInvoiceDto>(It.IsAny<object>()))
            .Returns(new SubscriptionInvoiceDto { Id = 15, InvoiceNumber = "INV-15" });
        var result = await Service(repo, User(), mapper).GetByIdAsync(15);
        Assert.True(result.Success);
        var line = Assert.Single(result.Data!.Lines);
        Assert.Equal("Monthly plan", line.Description);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(17.50m, line.UnitPrice);
        Assert.Equal(35m, line.Amount);
        repo.Verify(x => x.GetLinesForInvoiceAsync(42, 15, It.IsAny<CancellationToken>()), Times.Once);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Missing_or_foreign_invoice_never_queries_line_items()
    {
        var repo = new Mock<ISubscriptionInvoiceRepository>(MockBehavior.Strict);
        repo.Setup(x => x.GetByIdForSystemAsync(15, 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionInvoice?)null);
        var result = await Service(repo, User()).GetByIdAsync(15);
        Assert.Equal(404, result.StatusCode);
        repo.Verify(x => x.GetLinesForInvoiceAsync(It.IsAny<long>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Platform_admin_scopes_lines_to_invoice_owning_tenant()
    {
        var repo = new Mock<ISubscriptionInvoiceRepository>(MockBehavior.Strict);
        repo.Setup(x => x.GetByIdForPlatformAsync(22, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionInvoice { Id = 22, TenantId = 77 });
        repo.Setup(x => x.GetLinesForInvoiceAsync(77, 22, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SubscriptionInvoiceLine>());
        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<SubscriptionInvoiceDto>(It.IsAny<object>()))
            .Returns(new SubscriptionInvoiceDto { Id = 22 });
        var result = await Service(repo, User(0, true), mapper).GetByIdAsync(22);
        Assert.True(result.Success);
        Assert.Empty(result.Data!.Lines);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Missing_tenant_rejected_without_repository_access()
    {
        var repo = new Mock<ISubscriptionInvoiceRepository>(MockBehavior.Strict);
        var result = await Service(repo, User(0)).GetByIdAsync(15);
        Assert.Equal(403, result.StatusCode);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public void Invoice_detail_endpoint_retains_tenant_admin_authorization()
    {
        var type = typeof(SubscriptionController);
        Assert.Equal("TenantAdmin", Assert.Single(type.GetCustomAttributes(
            typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        var method = type.GetMethod("GetInvoice");
        Assert.NotNull(method);
        Assert.Equal("invoices/{id:long}", Assert.Single(method!.GetCustomAttributes(
            typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>()).Template);
    }
}
