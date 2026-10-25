using AutoMapper;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Service.Services.SaaS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class SubscriptionInvoiceHistoryTests
{
    private static SubscriptionInvoiceService Create(
        Mock<ISubscriptionInvoiceRepository> repository, Mock<ICurrentUserService> user,
        Mock<IMapper>? mapper = null) =>
        new(repository.Object, user.Object, (mapper ?? new Mock<IMapper>()).Object,
            NullLogger<SubscriptionInvoiceService>.Instance);

    private static Mock<ICurrentUserService> Tenant(long tenantId = 42)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.TenantId).Returns(tenantId);
        return user;
    }

    [Fact]
    public async Task Missing_tenant_is_rejected_before_repository_access()
    {
        var repository = new Mock<ISubscriptionInvoiceRepository>(MockBehavior.Strict);
        var user = Tenant(0);
        var result = await Create(repository, user).GetPageAsync(1, 10);
        Assert.Equal(403, result.StatusCode);
        repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task Invalid_paging_is_rejected(int page, int pageSize)
    {
        var repository = new Mock<ISubscriptionInvoiceRepository>(MockBehavior.Strict);
        var result = await Create(repository, Tenant()).GetPageAsync(page, pageSize);
        Assert.Equal(400, result.StatusCode);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Unknown_state_is_rejected_without_database_query()
    {
        var repository = new Mock<ISubscriptionInvoiceRepository>(MockBehavior.Strict);
        var result = await Create(repository, Tenant()).GetPageAsync(1, 10, (InvoiceState)999);
        Assert.Equal(400, result.StatusCode);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Paged_history_uses_authenticated_tenant_and_preserves_total()
    {
        var repository = new Mock<ISubscriptionInvoiceRepository>();
        var invoices = new List<SubscriptionInvoice> { new() { Id = 15, TenantId = 42, InvoiceNumber = "INV-15" } };
        repository.Setup(x => x.GetByTenantAsync(42, 2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((invoices, 21));
        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<List<SubscriptionInvoiceDto>>(It.IsAny<object>()))
            .Returns(new List<SubscriptionInvoiceDto> { new() { Id = 15, InvoiceNumber = "INV-15" } });
        var result = await Create(repository, Tenant(), mapper).GetPageAsync(2, 10);
        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Page);
        Assert.Equal(10, result.Data.PageSize);
        Assert.Equal(21, result.Data.TotalCount);
        Assert.Equal(3, result.Data.TotalPages);
        Assert.Equal("INV-15", Assert.Single(result.Data.Items).InvoiceNumber);
        repository.Verify(x => x.GetByTenantAsync(42, 2, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Status_filter_uses_existing_tenant_scoped_repository()
    {
        var repository = new Mock<ISubscriptionInvoiceRepository>();
        repository.Setup(x => x.GetByStateAsync(42, InvoiceState.Paid, 1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<SubscriptionInvoice>(), 0));
        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<List<SubscriptionInvoiceDto>>(It.IsAny<object>()))
            .Returns(new List<SubscriptionInvoiceDto>());
        var result = await Create(repository, Tenant(), mapper).GetPageAsync(1, 10, InvoiceState.Paid);
        Assert.True(result.Success);
        Assert.Empty(result.Data!.Items);
        repository.Verify(x => x.GetByStateAsync(42, InvoiceState.Paid, 1, 10, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(x => x.GetByTenantAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Endpoint_requires_tenant_admin_and_is_read_only()
    {
        var type = typeof(SubscriptionController);
        Assert.Equal("TenantAdmin", Assert.Single(type.GetCustomAttributes(
            typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        Assert.NotEmpty(type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        var method = type.GetMethod("GetInvoicePage");
        Assert.NotNull(method);
        Assert.Equal("invoices/paged", Assert.Single(method!.GetCustomAttributes(
            typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>()).Template);
    }
}
