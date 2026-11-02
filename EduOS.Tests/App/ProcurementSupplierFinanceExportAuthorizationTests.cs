using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Real ASP.NET module/role middleware with synthetic Finance responses, plus
/// bounded CSV controller contracts. No SQL, bank transfer or financial POST.
/// </summary>
public sealed class ProcurementSupplierFinanceExportAuthorizationTests
{
    private sealed class HeaderAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (string.IsNullOrWhiteSpace(role))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "901"),
                new Claim(ClaimTypes.Role, role)
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private static ApiResponse<PagedResult<T>> Ok<T>(int page, int total, params T[] items) =>
        ApiResponse<PagedResult<T>>.SuccessResponse(new PagedResult<T>
        {
            Page = page, PageSize = 100, TotalCount = total, Items = items.ToList()
        });

    private static ProcurementSupplierInvoiceDto Invoice(int id) => new()
    {
        Reference = Guid.Parse($"00000000-0000-4000-8000-{id:D12}"),
        SupplierInvoiceNumber = id == 1 ? "=HYPERLINK(\"unsafe\")" : $"INV-{id}",
        Lines = [new ProcurementSupplierInvoiceLineDto
        {
            LineNo = 1, LotOrBatchNo = "+SUM(1,1)\r\nINJECTED", Quantity = 1m,
            InvoiceAmount = 10m
        }]
    };

    private static ProcurementSupplierPayableAgingRowDto Aging(int id) => new()
    {
        InvoiceReference = Invoice(id).Reference,
        SupplierInvoiceNumber = id == 1 ? "=HYPERLINK(\"unsafe\")" : $"INV-{id}",
        SourceLinkedOutstandingAmount = 10m
    };

    private static TestServer Server(Mock<IProcurementSupplierInvoiceService> service)
    {
        var accessor = new HttpContextAccessor();
        var modules = new Mock<ITenantModuleService>();
        modules.Setup(x => x.IsCurrentTenantModuleAvailableAsync(It.IsAny<string>()))
            .Returns((string code) => Task.FromResult(
                accessor.HttpContext?.Request.Headers["X-Test-" + code.ToUpperInvariant()]
                    .ToString() == "true"));
        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                services.AddSingleton<IHttpContextAccessor>(accessor);
                services.AddAuthentication("Header")
                    .AddScheme<AuthenticationSchemeOptions, HeaderAuthentication>("Header", _ => { });
                services.AddAuthorization();
                services.AddSingleton<IAuthorizationPolicyProvider, ModuleAuthorizationPolicyProvider>();
                services.AddSingleton<IAuthorizationHandler, ModuleAccessHandler>();
                services.AddSingleton(modules.Object);
                services.AddSingleton(service.Object);
                services.AddAntiforgery();
                services.AddRateLimiter(x => x.AddFixedWindowLimiter("ApiPolicy", y =>
                {
                    y.PermitLimit = 1000;
                    y.Window = TimeSpan.FromMinutes(1);
                    y.QueueLimit = 0;
                }));
                services.AddControllersWithViews()
                    .AddApplicationPart(typeof(ProcurementSupplierInvoicesController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseRateLimiter();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(x => x.MapControllers());
            }));
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string path,
        string? role, bool inventory, bool finance)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (role != null) request.Headers.TryAddWithoutValidation("X-Test-Role", role);
        request.Headers.TryAddWithoutValidation("X-Test-INVENTORY", inventory ? "true" : "false");
        request.Headers.TryAddWithoutValidation("X-Test-FINANCE", finance ? "true" : "false");
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Finance_exports_aging_and_posting_options_require_both_modules_and_finance_role()
    {
        var service = new Mock<IProcurementSupplierInvoiceService>(MockBehavior.Strict);
        service.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, Invoice(1)));
        service.Setup(x => x.AgingAsync(null, null, false, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, Aging(1)));
        service.Setup(x => x.AgingAsync(null, null, false, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<ProcurementSupplierPayableAgingRowDto>>.SuccessResponse(
                new() { Page = 1, PageSize = 25, TotalCount = 1, Items = [Aging(1)] }));
        service.Setup(x => x.PostingOptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ProcurementInvoicePostingOptionsDto>.SuccessResponse(new()));
        using var server = Server(service);
        using var client = server.CreateClient();
        foreach (var path in new[]
        {
            "/api/procurement/supplier-invoices/export",
            "/api/procurement/supplier-invoices/aging/export",
            "/api/procurement/supplier-invoices/aging",
            "/api/procurement/supplier-invoices/options"
        })
        {
            using (var denied = await Get(client, path, "Accountant", true, false))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var denied = await Get(client, path, "Accountant", false, true))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var denied = await Get(client, path, "Student", true, true))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var denied = await Get(client, path, null, true, true))
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            using (var allowed = await Get(client, path, "Accountant", true, true))
                Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        service.Verify(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.AgingAsync(null, null, false, 1, 100, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.PostingOptionsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Supplier_invoice_export_rejects_duplicate_missing_and_changed_page_visibility()
    {
        foreach (var drift in new[] { "duplicate", "missing", "count" })
        {
            var service = new Mock<IProcurementSupplierInvoiceService>();
            service.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Ok(1, 101, Enumerable.Range(1, 100).Select(Invoice).ToArray()));
            service.Setup(x => x.ListAsync(null, null, 2, 100, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Ok(2, drift == "count" ? 102 : 101,
                    drift == "missing" ? Array.Empty<ProcurementSupplierInvoiceDto>() :
                    [Invoice(drift == "duplicate" ? 100 : 101)]));
            var result = Assert.IsType<ObjectResult>(
                await new ProcurementSupplierInvoicesController(service.Object)
                    .Export(null, null, CancellationToken.None));
            Assert.Equal(409, result.StatusCode);
        }
    }

    [Fact]
    public async Task Supplier_payable_aging_export_rejects_changed_or_duplicate_authorized_pages()
    {
        foreach (var drift in new[] { "duplicate", "count" })
        {
            var service = new Mock<IProcurementSupplierInvoiceService>();
            service.Setup(x => x.AgingAsync(null, null, false, 1, 100, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Ok(1, 101, Enumerable.Range(1, 100).Select(Aging).ToArray()));
            service.Setup(x => x.AgingAsync(null, null, false, 2, 100, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Ok(2, drift == "count" ? 102 : 101,
                    Aging(drift == "duplicate" ? 100 : 101)));
            var result = Assert.IsType<ObjectResult>(
                await new ProcurementSupplierInvoicesController(service.Object)
                    .AgingExport(null, null, false, CancellationToken.None));
            Assert.Equal(409, result.StatusCode);
        }
    }

    [Fact]
    public async Task Authorized_stable_supplier_exports_are_bounded_and_formula_safe()
    {
        var service = new Mock<IProcurementSupplierInvoiceService>();
        service.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, Invoice(1)));
        service.Setup(x => x.AgingAsync(null, null, false, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, Aging(1)));
        var controller = new ProcurementSupplierInvoicesController(service.Object);
        foreach (var action in new Func<Task<IActionResult>>[]
        {
            () => controller.Export(null, null, CancellationToken.None),
            () => controller.AgingExport(null, null, false, CancellationToken.None)
        })
        {
            var file = Assert.IsType<FileContentResult>(await action());
            var csv = Encoding.UTF8.GetString(file.FileContents);
            Assert.StartsWith("\uFEFF", csv);
            Assert.Contains("\"'=HYPERLINK(\"\"unsafe\"\")\"", csv);
            Assert.DoesNotContain("\r\nINJECTED", csv);
            Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        }
        var invoice = Assert.IsType<FileContentResult>(
            await controller.Export(null, null, CancellationToken.None));
        Assert.Contains("\"'+SUM(1,1)  INJECTED\"", Encoding.UTF8.GetString(invoice.FileContents));
    }

    [Fact]
    public async Task Finance_export_denial_or_oversized_scope_never_returns_csv()
    {
        var service = new Mock<IProcurementSupplierInvoiceService>();
        service.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<ProcurementSupplierInvoiceDto>>
                .ErrorResponse("Campus access revoked.", 403));
        var denied = Assert.IsType<ObjectResult>(
            await new ProcurementSupplierInvoicesController(service.Object)
                .Export(null, null, CancellationToken.None));
        Assert.Equal(403, denied.StatusCode);
        service.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok<ProcurementSupplierInvoiceDto>(1, 501));
        var oversized = Assert.IsType<ObjectResult>(
            await new ProcurementSupplierInvoicesController(service.Object)
                .Export(null, null, CancellationToken.None));
        Assert.Equal(413, oversized.StatusCode);
    }
}
