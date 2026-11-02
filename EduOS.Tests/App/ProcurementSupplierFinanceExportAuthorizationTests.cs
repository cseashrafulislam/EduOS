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
        PurchaseOrderId = 7, CampusId = 3, SupplierId = 9,
        TotalAmount = 10m, StockValue = 10m, PriceVariance = 0m,
        Lines = [new ProcurementSupplierInvoiceLineDto
        {
            Id = 1, LineNo = 1, InventoryItemId = 11, PurchaseOrderLineId = 12,
            GoodsReceiptLineId = 13, LotOrBatchNo = "+SUM(1,1)\r\nINJECTED",
            Quantity = 1m, ReceivedQuantity = 1m, PoUnitPrice = 10m,
            StockUnitCost = 10m, InvoiceUnitPrice = 10m,
            InvoiceAmount = 10m, StockValue = 10m
        }]
    };

    private static ProcurementSupplierPayableAgingRowDto Aging(int id) => new()
    {
        InvoiceReference = Invoice(id).Reference,
        SupplierInvoiceNumber = id == 1 ? "=HYPERLINK(\"unsafe\")" : $"INV-{id}",
        JournalReference = Guid.Parse($"00000000-0000-4000-9000-{id:D12}"),
        SupplierId = 9, CampusId = 3, DueDate = new DateOnly(2026, 11, 1),
        GrossPostedAmount = 10m, SourceLinkedOutstandingAmount = 10m
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
                app.UseEndpoints(x =>
                {
                    x.MapControllers();
                    x.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
                });
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


    [Theory]
    [InlineData("conflicted-status")]
    [InlineData("missing-net")]
    [InlineData("negative-net")]
    [InlineData("missing-journal")]
    [InlineData("missing-campus")]
    [InlineData("missing-supplier")]
    [InlineData("negative-settlement")]
    [InlineData("negative-credit")]
    [InlineData("negative-reservation")]
    [InlineData("invalid-refund")]
    [InlineData("wrong-net")]
    [InlineData("zero-gross")]
    public async Task Supplier_payable_aging_CSV_refuses_unreconciled_financial_evidence(string defect)
    {
        var row = Aging(1);
        switch (defect)
        {
            case "conflicted-status": row.SettlementIntegrityStatus = "EvidenceConflict"; row.SourceLinkedOutstandingAmount = null; break;
            case "missing-net": row.SourceLinkedOutstandingAmount = null; break;
            case "negative-net": row.SourceLinkedOutstandingAmount = -2m; break;
            case "missing-journal": row.JournalReference = Guid.Empty; break;
            case "missing-campus": row.CampusId = 0; break;
            case "missing-supplier": row.SupplierId = 0; break;
            case "negative-settlement": row.PostedSettlementAmount = -1m; break;
            case "negative-credit": row.PostedSupplierCreditAmount = -1m; break;
            case "negative-reservation": row.ReservedUnpostedAmount = -1m; break;
            case "invalid-refund": row.SupplierRefundReceivableAmount = 1m; break;
            case "wrong-net": row.SourceLinkedOutstandingAmount = 9m; break;
            case "zero-gross": row.GrossPostedAmount = 0m; break;
        }
        var service = new Mock<IProcurementSupplierInvoiceService>();
        service.Setup(x => x.AgingAsync(null, null, false, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, row));
        var response = Assert.IsType<ObjectResult>(await new ProcurementSupplierInvoicesController(service.Object)
            .AgingExport(null, null, false, CancellationToken.None));
        Assert.Equal(409, response.StatusCode);
        service.Verify(x => x.AgingAsync(null, null, false, 1, 100, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
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
        service.Verify(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()), Times.Exactly(2));
        service.Verify(x => x.AgingAsync(null, null, false, 1, 100, It.IsAny<CancellationToken>()), Times.Exactly(2));
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
        var invoiceRow = Invoice(1);
        invoiceRow.PriceVariance = -5m;
        invoiceRow.StockValue = 15m;
        invoiceRow.Lines[0].StockUnitCost = 15m;
        invoiceRow.Lines[0].StockValue = 15m;
        var agingRow = Aging(1);
        agingRow.PostedSettlementAmount = 2m;
        agingRow.SourceLinkedOutstandingAmount = 8m;
        service.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, invoiceRow));
        service.Setup(x => x.AgingAsync(null, null, false, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, agingRow));
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
        Assert.Contains("\"-5\"", Encoding.UTF8.GetString(invoice.FileContents));
        Assert.DoesNotContain("\"'-5\"", Encoding.UTF8.GetString(invoice.FileContents));
        var agingFile = Assert.IsType<FileContentResult>(
            await controller.AgingExport(null, null, false, CancellationToken.None));
        Assert.Contains("\"8\"", Encoding.UTF8.GetString(agingFile.FileContents));
        Assert.DoesNotContain("\"'8\"", Encoding.UTF8.GetString(agingFile.FileContents));
    }

    [Fact]
    public async Task Supplier_finance_exports_reject_same_count_changed_amount_or_replaced_reference()
    {
        var invoiceBefore = Invoice(1);
        invoiceBefore.TotalAmount = 10m;
        var invoiceAfter = Invoice(1);
        invoiceAfter.TotalAmount = 11m;
        var invoiceService = new Mock<IProcurementSupplierInvoiceService>();
        invoiceService.SetupSequence(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, invoiceBefore))
            .ReturnsAsync(Ok(1, 1, invoiceAfter));
        var invoiceResult = Assert.IsType<ObjectResult>(
            await new ProcurementSupplierInvoicesController(invoiceService.Object)
                .Export(null, null, CancellationToken.None));
        Assert.Equal(409, invoiceResult.StatusCode);

        var agingBefore = Aging(1);
        agingBefore.PostedSettlementAmount = 5m;
        var agingAfter = Aging(2);
        agingAfter.PostedSettlementAmount = 5m;
        var agingService = new Mock<IProcurementSupplierInvoiceService>();
        agingService.SetupSequence(x => x.AgingAsync(null, null, false, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, agingBefore))
            .ReturnsAsync(Ok(1, 1, agingAfter));
        var agingResult = Assert.IsType<ObjectResult>(
            await new ProcurementSupplierInvoicesController(agingService.Object)
                .AgingExport(null, null, false, CancellationToken.None));
        Assert.Equal(409, agingResult.StatusCode);
    }

    [Fact]
    public async Task Supplier_export_rechecks_authorization_and_never_returns_csv_after_revocation()
    {
        var service = new Mock<IProcurementSupplierInvoiceService>();
        service.SetupSequence(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, Invoice(1)))
            .ReturnsAsync(ApiResponse<PagedResult<ProcurementSupplierInvoiceDto>>
                .ErrorResponse("Campus access revoked.", 403));
        var result = Assert.IsType<ObjectResult>(
            await new ProcurementSupplierInvoicesController(service.Object)
                .Export(null, null, CancellationToken.None));
        Assert.Equal(403, result.StatusCode);
        Assert.IsNotType<FileContentResult>(result);
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
    [Fact]
    public async Task Supplier_invoice_financial_workspace_and_intake_fail_closed_without_finance_entitlement()
    {
        var service = new Mock<IProcurementSupplierInvoiceService>(MockBehavior.Strict);
        using var server = Server(service);
        using var client = server.CreateClient();
        var reference = Guid.Parse("f1000000-0000-4000-8000-000000000001");
        foreach (var path in new[]
        {
            "/ProcurementSupplierInvoiceMatching/Index",
            "/api/procurement/supplier-invoices",
            $"/api/procurement/supplier-invoices/{reference}"
        })
        {
            using var denied = await Get(client, path, "Accountant", true, false);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var deniedInventory = await Get(client, path, "Accountant", false, true);
            Assert.Equal(HttpStatusCode.Forbidden, deniedInventory.StatusCode);
        }

        foreach (var path in new[]
        {
            "/api/procurement/supplier-invoices",
            $"/api/procurement/supplier-invoices/{reference}/submit"
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("X-Test-Role", "Accountant");
            request.Headers.TryAddWithoutValidation("X-Test-INVENTORY", "true");
            request.Headers.TryAddWithoutValidation("X-Test-FINANCE", "false");
            using var denied = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Supplier_invoice_financial_reads_still_work_with_both_entitlements()
    {
        var reference = Invoice(1).Reference;
        var service = new Mock<IProcurementSupplierInvoiceService>(MockBehavior.Strict);
        service.Setup(x => x.ListAsync(null, null, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, Invoice(1)));
        service.Setup(x => x.GetAsync(reference, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ProcurementSupplierInvoiceDto>.SuccessResponse(Invoice(1)));
        using var server = Server(service);
        using var client = server.CreateClient();
        using (var list = await Get(client, "/api/procurement/supplier-invoices",
            "Accountant", true, true))
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using (var detail = await Get(client,
            $"/api/procurement/supplier-invoices/{reference}", "Accountant", true, true))
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        service.Verify(x => x.ListAsync(null, null, 1, 25, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.GetAsync(reference, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Supplier_finance_exports_share_unicode_formula_protection_and_keep_signed_amounts()
    {
        var invoice = Invoice(1);
        invoice.SupplierInvoiceNumber = "\uFEFF\u200B=HYPERLINK(\"unsafe\")";
        invoice.OrderNumber = "বাংলা\u200Dনাম";
        invoice.Lines[0].LotOrBatchNo = "\u202E+SUM(1,1)\u2028EXTRA";
        invoice.PriceVariance = -5m;
        invoice.StockValue = 15m;
        invoice.Lines[0].StockUnitCost = 15m;
        invoice.Lines[0].StockValue = 15m;
        var aging = Aging(1);
        aging.SupplierInvoiceNumber = "\u2060@SUM(1,1)";
        aging.PostedSettlementAmount = 2m;
        aging.SourceLinkedOutstandingAmount = 8m;
        var service = new Mock<IProcurementSupplierInvoiceService>();
        service.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, invoice));
        service.Setup(x => x.AgingAsync(null, null, false, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, aging));
        var controller = new ProcurementSupplierInvoicesController(service.Object);
        var invoiceFile = Assert.IsType<FileContentResult>(
            await controller.Export(null, null, CancellationToken.None));
        var invoiceCsv = Encoding.UTF8.GetString(invoiceFile.FileContents);
        Assert.Contains("\"'  =HYPERLINK(\"\"unsafe\"\")\"", invoiceCsv);
        Assert.Contains("\"' +SUM(1,1) EXTRA\"", invoiceCsv);
        Assert.Contains("বাংলা\u200Dনাম", invoiceCsv);
        Assert.Contains("\"-5\"", invoiceCsv);
        Assert.False(invoiceCsv.Contains("\uFEFF\u200B", StringComparison.Ordinal));
        Assert.False(invoiceCsv.Contains("\u202E", StringComparison.Ordinal));

        var agingFile = Assert.IsType<FileContentResult>(
            await controller.AgingExport(null, null, false, CancellationToken.None));
        var agingCsv = Encoding.UTF8.GetString(agingFile.FileContents);
        Assert.Contains("\"' @SUM(1,1)\"", agingCsv);
        Assert.Contains("\"8\"", agingCsv);
        Assert.DoesNotContain("\"'8\"", agingCsv);
    }

    [Fact]
    public async Task Supplier_finance_export_failures_redact_private_service_messages_at_every_read_stage()
    {
        const string privateDetail = "BANK-EVIDENCE-SECRET: private campus settlement";
        var firstDenied = new Mock<IProcurementSupplierInvoiceService>();
        firstDenied.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<ProcurementSupplierInvoiceDto>>.ErrorResponse(privateDetail, 403));
        var initial = Assert.IsType<ObjectResult>(
            await new ProcurementSupplierInvoicesController(firstDenied.Object)
                .Export(null, null, CancellationToken.None));
        Assert.Equal(403, initial.StatusCode);
        Assert.DoesNotContain(privateDetail, System.Text.Json.JsonSerializer.Serialize(initial.Value));

        var laterDenied = new Mock<IProcurementSupplierInvoiceService>();
        laterDenied.Setup(x => x.AgingAsync(null, null, false, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 101, Enumerable.Range(1, 100).Select(Aging).ToArray()));
        laterDenied.Setup(x => x.AgingAsync(null, null, false, 2, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<PagedResult<ProcurementSupplierPayableAgingRowDto>>
                .ErrorResponse(privateDetail, 403));
        var pageDenied = Assert.IsType<ObjectResult>(
            await new ProcurementSupplierInvoicesController(laterDenied.Object)
                .AgingExport(null, null, false, CancellationToken.None));
        Assert.Equal(403, pageDenied.StatusCode);
        Assert.DoesNotContain(privateDetail, System.Text.Json.JsonSerializer.Serialize(pageDenied.Value));

        var recheckDenied = new Mock<IProcurementSupplierInvoiceService>();
        recheckDenied.SetupSequence(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, Invoice(1)))
            .ReturnsAsync(ApiResponse<PagedResult<ProcurementSupplierInvoiceDto>>
                .ErrorResponse(privateDetail, 403));
        var replayDenied = Assert.IsType<ObjectResult>(
            await new ProcurementSupplierInvoicesController(recheckDenied.Object)
                .Export(null, null, CancellationToken.None));
        Assert.Equal(403, replayDenied.StatusCode);
        Assert.DoesNotContain(privateDetail, System.Text.Json.JsonSerializer.Serialize(replayDenied.Value));
    }


    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("invalid")]
    [InlineData("oversized")]
    public async Task Supplier_invoice_export_rejects_missing_or_ambiguous_source_lines(string variant)
    {
        var invoice = Invoice(1);
        switch (variant)
        {
            case "empty":
                invoice.Lines.Clear();
                break;
            case "duplicate":
                invoice.Lines.Add(new ProcurementSupplierInvoiceLineDto { LineNo = 1, Quantity = 1m });
                break;
            case "invalid":
                invoice.Lines[0].LineNo = 0;
                break;
            case "oversized":
                invoice.Lines = Enumerable.Range(1, 101)
                    .Select(i => new ProcurementSupplierInvoiceLineDto { LineNo = i, Quantity = 1m })
                    .ToList();
                break;
        }
        var service = new Mock<IProcurementSupplierInvoiceService>();
        service.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, invoice));
        var response = Assert.IsType<ObjectResult>(
            await new ProcurementSupplierInvoicesController(service.Object)
                .Export(null, null, CancellationToken.None));
        Assert.Equal(409, response.StatusCode);
        Assert.IsNotType<FileContentResult>(response);
    }

    [Fact]
    public async Task Supplier_exports_reject_in_place_financial_mutation_of_reused_service_DTOs()
    {
        // A caching or shared service projection may return the SAME object in
        // both passes. Compare immutable first-pass evidence, not live aliases.
        var invoice = Invoice(1);
        invoice.TotalAmount = 10m;
        var invoiceReads = 0;
        var invoiceService = new Mock<IProcurementSupplierInvoiceService>();
        invoiceService.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (++invoiceReads == 2) invoice.TotalAmount = 11m;
                return Ok(1, 1, invoice);
            });
        var invoiceResult = Assert.IsType<ObjectResult>(
            await new ProcurementSupplierInvoicesController(invoiceService.Object)
                .Export(null, null, CancellationToken.None));
        Assert.Equal(409, invoiceResult.StatusCode);
        Assert.Equal(2, invoiceReads);

        var aging = Aging(1);
        aging.PostedSettlementAmount = 5m;
        var agingReads = 0;
        var agingService = new Mock<IProcurementSupplierInvoiceService>();
        agingService.Setup(x => x.AgingAsync(null, null, false, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (++agingReads == 2) aging.PostedSettlementAmount = 6m;
                return Ok(1, 1, aging);
            });
        var agingResult = Assert.IsType<ObjectResult>(
            await new ProcurementSupplierInvoicesController(agingService.Object)
                .AgingExport(null, null, false, CancellationToken.None));
        Assert.Equal(409, agingResult.StatusCode);
        Assert.Equal(2, agingReads);
    }

    [Fact]
    public async Task Supplier_finance_mutations_without_antiforgery_token_are_rejected_before_service()
    {
        var service = new Mock<IProcurementSupplierInvoiceService>(MockBehavior.Strict);
        using var server = Server(service);
        using var client = server.CreateClient();
        var reference = Guid.Parse("f1000000-0000-4000-8000-000000000001");
        foreach (var path in new[]
        {
            "/api/procurement/supplier-invoices",
            $"/api/procurement/supplier-invoices/{reference}/submit",
            $"/api/procurement/supplier-invoices/{reference}/settlements"
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("X-Test-Role", "Accountant");
            request.Headers.TryAddWithoutValidation("X-Test-INVENTORY", "true");
            request.Headers.TryAddWithoutValidation("X-Test-FINANCE", "true");
            using var denied = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
        service.VerifyNoOtherCalls();
    }


    [Theory]
    [InlineData("missing-po")]
    [InlineData("missing-campus")]
    [InlineData("missing-supplier")]
    [InlineData("missing-item")]
    [InlineData("missing-receipt")]
    [InlineData("over-received")]
    [InlineData("invalid-price")]
    [InlineData("bad-invoice-line-amount")]
    [InlineData("bad-stock-line-amount")]
    [InlineData("bad-header-total")]
    [InlineData("bad-header-variance")]
    [InlineData("posted-without-journal")]
    public async Task Supplier_invoice_export_rejects_unreconciled_source_and_GL_evidence(string defect)
    {
        var invoice = Invoice(1);
        var line = invoice.Lines[0];
        switch (defect)
        {
            case "missing-po": invoice.PurchaseOrderId = 0; break;
            case "missing-campus": invoice.CampusId = 0; break;
            case "missing-supplier": invoice.SupplierId = 0; break;
            case "missing-item": line.InventoryItemId = 0; break;
            case "missing-receipt": line.GoodsReceiptLineId = 0; break;
            case "over-received": line.Quantity = 2m; break;
            case "invalid-price": line.InvoiceUnitPrice = 0m; break;
            case "bad-invoice-line-amount": line.InvoiceAmount = 11m; break;
            case "bad-stock-line-amount": line.StockValue = 11m; break;
            case "bad-header-total": invoice.TotalAmount = 11m; invoice.PriceVariance = 1m; break;
            case "bad-header-variance": invoice.PriceVariance = 1m; break;
            case "posted-without-journal": invoice.StateCode = "Posted"; break;
        }
        var service = new Mock<IProcurementSupplierInvoiceService>();
        service.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, invoice));
        var response = Assert.IsType<ObjectResult>(
            await new ProcurementSupplierInvoicesController(service.Object)
                .Export(null, null, CancellationToken.None));
        Assert.Equal(409, response.StatusCode);
        Assert.IsNotType<FileContentResult>(response);
    }

    [Fact]
    public async Task Posted_supplier_invoice_export_keeps_fractional_prices_and_negative_variance()
    {
        var invoice = Invoice(1);
        invoice.StateCode = "Posted";
        invoice.JournalReference = Guid.NewGuid();
        invoice.TotalAmount = 10m;
        invoice.StockValue = 15m;
        invoice.PriceVariance = -5m;
        var line = invoice.Lines[0];
        line.Quantity = 8m;
        line.ReceivedQuantity = 8m;
        line.PoUnitPrice = 1.125m;
        line.InvoiceUnitPrice = 1.25m;
        line.StockUnitCost = 1.875m;
        line.InvoiceAmount = 10m;
        line.StockValue = 15m;
        var service = new Mock<IProcurementSupplierInvoiceService>();
        service.Setup(x => x.ListAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, invoice));
        var file = Assert.IsType<FileContentResult>(
            await new ProcurementSupplierInvoicesController(service.Object)
                .Export(null, null, CancellationToken.None));
        var csv = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains(invoice.JournalReference.Value.ToString(), csv);
        Assert.Contains("\"1.875\"", csv);
        Assert.Contains("\"-5\"", csv);
    }
}
