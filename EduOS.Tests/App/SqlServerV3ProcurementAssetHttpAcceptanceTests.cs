using System.Net;
using System.Net.Http;
using System.Data;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Inventory;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerV3ProcurementPurchaseOrderTests
{
    [Fact]
    public async Task SQL_supplier_finance_multi_page_CSV_uses_one_serializable_transaction()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        await using var db = new EduOSDbContext(options);
        var invoices = Enumerable.Range(0, 101).Select(i => new ProcurementSupplierPayableAgingRowDto
        {
            InvoiceReference = Guid.NewGuid(), JournalReference = Guid.NewGuid(),
            SupplierInvoiceNumber = $"SQL-SNAPSHOT-{i:000}", OrderNumber = "PO-SNAPSHOT",
            SupplierId = 1, CampusId = 1, DueDate = new DateOnly(2026, 11, 1),
            GrossPostedAmount = 10m, SourceLinkedOutstandingAmount = 10m,
            SettlementIntegrityStatus = "SourceLinked", AgingBucket = "1-30"
        }).ToList();
        var transactionIds = new List<Guid>();
        var mock = new Mock<IProcurementSupplierInvoiceService>();
        mock.Setup(x => x.AgingAsync(null, null, false, It.IsAny<int>(), 100,
                It.IsAny<CancellationToken>()))
            .Returns((DateOnly? _, long? _, bool _, int page, int _, CancellationToken _) =>
            {
                var transaction = db.Database.CurrentTransaction;
                Assert.NotNull(transaction);
                Assert.Equal(IsolationLevel.Serializable,
                    transaction.GetDbTransaction().IsolationLevel);
                transactionIds.Add(transaction.TransactionId);
                return Task.FromResult(ApiResponse<PagedResult<ProcurementSupplierPayableAgingRowDto>>
                    .SuccessResponse(new PagedResult<ProcurementSupplierPayableAgingRowDto>
                    {
                        Page = page, PageSize = 100, TotalCount = invoices.Count,
                        Items = invoices.Skip((page - 1) * 100).Take(100).ToList()
                    }));
            });
        var controller = new ProcurementSupplierInvoicesController(mock.Object, db);
        var response = await controller.AgingExport(null, null, false, CancellationToken.None);
        var csv = Assert.IsType<FileContentResult>(response);
        var body = Encoding.UTF8.GetString(csv.FileContents);
        Assert.Contains("SQL-SNAPSHOT-000", body, StringComparison.Ordinal);
        Assert.Contains("SQL-SNAPSHOT-100", body, StringComparison.Ordinal);
        Assert.Equal(4, transactionIds.Count);
        Assert.Single(transactionIds.Distinct());
        Assert.Null(db.Database.CurrentTransaction);
    }

    // Real disposable SQL and MVC authorization, with TEST-ONLY header identity
    // and synthetic module grants. Not a real cookie/Chromium acceptance claim.
    private sealed class SqlHttpFinanceActor(IHttpContextAccessor accessor) : ICurrentUserService
    {
        private HttpContext? Context => accessor.HttpContext;
        public bool IsAuthenticated => Context?.User.Identity?.IsAuthenticated == true;
        public long TenantId => Context?.Items.TryGetValue("TenantId", out var value) == true &&
            value is long tenant ? tenant : 0;
        public long UserId => long.TryParse(Context?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var id) ? id : 0;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => IsInRole("TenantAdmin");
        public IReadOnlyList<string> Roles =>
            Context?.User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray() ?? [];
        public bool IsInRole(string role) => Context?.User.IsInRole(role) == true;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private sealed class SqlHttpFinanceAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (!long.TryParse(Request.Headers["X-Test-Actor"], out var actor) || actor <= 0 ||
                !long.TryParse(Request.Headers["X-Test-Tenant"], out var tenant) || tenant <= 0 ||
                string.IsNullOrWhiteSpace(role))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, actor.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim("TenantId", tenant.ToString())
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private static TestServer SupplierAssetSqlHttpServer(DbContextOptions<EduOSDbContext> options)
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
                services.AddScoped<ICurrentUserService, SqlHttpFinanceActor>();
                services.AddScoped(_ => new EduOSDbContext(options, accessor));
                services.AddSingleton(TimeProvider.System);
                services.AddScoped<InventoryMovementService>();
                services.AddScoped<AssetLifecycleService>();
                services.AddScoped<AssetAccountingService>();
                services.AddScoped<ProcurementSupplierInvoiceService>();
                services.AddScoped<IProcurementSupplierInvoiceService>(sp =>
                    sp.GetRequiredService<ProcurementSupplierInvoiceService>());
                services.AddAuthentication("SqlFinance")
                    .AddScheme<AuthenticationSchemeOptions, SqlHttpFinanceAuthentication>(
                        "SqlFinance", _ => { });
                services.AddAuthorization();
                services.AddSingleton<IAuthorizationPolicyProvider, ModuleAuthorizationPolicyProvider>();
                services.AddSingleton<IAuthorizationHandler, ModuleAccessHandler>();
                services.AddSingleton(modules.Object);
                services.AddAntiforgery();
                services.AddRateLimiter(x => x.AddFixedWindowLimiter("ApiPolicy", y =>
                {
                    y.PermitLimit = 1000;
                    y.Window = TimeSpan.FromMinutes(1);
                    y.QueueLimit = 0;
                }));
                services.AddControllersWithViews()
                    .AddApplicationPart(typeof(AssetAccountingController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseRateLimiter();
                app.UseAuthentication();
                app.Use(async (context, next) =>
                {
                    // Test tenant is bound to the authenticated test principal.
                    if (long.TryParse(context.User.FindFirstValue("TenantId"), out var tenant) &&
                        tenant > 0)
                        context.Items["TenantId"] = tenant;
                    await next();
                });
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/__sql_test/antiforgery", async context =>
                    {
                        var tokens = context.RequestServices.GetRequiredService<IAntiforgery>()
                            .GetAndStoreTokens(context);
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync(tokens.RequestToken!);
                    });
                    endpoints.MapControllers();
                });
            }));
    }

    private static HttpRequestMessage SqlFinanceRequest(HttpMethod method, string path,
        long tenantId, long actorId, string role = "Accountant",
        bool inventory = true, bool finance = true)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("X-Test-Actor", actorId.ToString());
        request.Headers.TryAddWithoutValidation("X-Test-Tenant", tenantId.ToString());
        if (!string.IsNullOrWhiteSpace(role))
            request.Headers.TryAddWithoutValidation("X-Test-Role", role);
        request.Headers.TryAddWithoutValidation("X-Test-INVENTORY", inventory ? "true" : "false");
        request.Headers.TryAddWithoutValidation("X-Test-FINANCE", finance ? "true" : "false");
        return request;
    }

    private static async Task<(HttpStatusCode Status, string Body)> SqlFinanceGet(
        HttpClient client, string path, long tenantId, long actorId,
        string role = "Accountant", bool inventory = true, bool finance = true)
    {
        using var request = SqlFinanceRequest(HttpMethod.Get, path, tenantId, actorId,
            role, inventory, finance);
        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task VerifySqlBackedSupplierAssetHttpAsync(
        DbContextOptions<EduOSDbContext> options, long tenantId, long makerId,
        Guid invoiceReference, Guid assetReference,
        CreateAssetCapitalizationRequest originalCapitalization)
    {
        long campusId;
        await using (var lookup = Open(options, tenantId, makerId))
            campusId = (await lookup.Assets.AsNoTracking()
                .Where(x => x.PublicId == assetReference)
                .Select(x => x.CampusId).SingleAsync())
                ?? throw new InvalidOperationException("SQL asset campus is required.");

        long foreignTenantId;
        await using (var root = new EduOSDbContext(options))
        {
            var suffix = Guid.NewGuid().ToString("N")[..12];
            var foreign = new Tenant
            {
                Code = "SI-HTTP-" + suffix, Name = "Foreign SQL HTTP tenant",
                Email = "si-http-" + suffix + "@example.test", State = TenantState.Active
            };
            root.Tenants.Add(foreign);
            await root.SaveChangesAsync();
            foreignTenantId = foreign.Id;
        }

        var invoicePath = $"/api/procurement/supplier-invoices/{invoiceReference}";
        var listPath = "/api/procurement/supplier-invoices";
        var agingPath = "/api/procurement/supplier-invoices/aging";
        var agingCsvPath = "/api/procurement/supplier-invoices/aging/export";
        string invoiceNumber;
        await using (var lookupInvoice = Open(options, tenantId, makerId))
            invoiceNumber = await lookupInvoice.ProcurementSupplierInvoices.AsNoTracking()
                .Where(x => x.PublicId == invoiceReference)
                .Select(x => x.SupplierInvoiceNumber).SingleAsync();
        var statementPath = $"/api/inventory/asset-accounting/{assetReference}/statement";
        var capitalizationPath = $"/api/inventory/asset-accounting/{assetReference}/capitalization";
        using var server = SupplierAssetSqlHttpServer(options);
        using var client = server.CreateClient();

        foreach (var path in new[] { invoicePath, statementPath, capitalizationPath })
        {
            var denied = await SqlFinanceGet(client, path, tenantId, makerId);
            Assert.Equal(HttpStatusCode.NotFound, denied.Status);
            Assert.DoesNotContain(invoiceReference.ToString(), denied.Body,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(assetReference.ToString(), denied.Body,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(HttpStatusCode.NotFound,
                (await SqlFinanceGet(client, path, foreignTenantId, makerId)).Status);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await SqlFinanceGet(client, path, tenantId, makerId, "Student")).Status);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await SqlFinanceGet(client, path, tenantId, makerId, "")).Status);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await SqlFinanceGet(client, path, tenantId, makerId,
                    inventory: false)).Status);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await SqlFinanceGet(client, path, tenantId, makerId,
                    finance: false)).Status);
        }
        Assert.DoesNotContain(invoiceReference.ToString(),
            (await SqlFinanceGet(client, listPath, tenantId, makerId)).Body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(invoiceReference.ToString(),
            (await SqlFinanceGet(client, agingPath, tenantId, makerId)).Body,
            StringComparison.OrdinalIgnoreCase);

        // The CSV must not disclose a campus-limited posted invoice before grant,
        // even when an unauthorized user guesses the export URL directly.
        foreach (var deniedScope in new[] { tenantId, foreignTenantId })
        {
            var hiddenCsv = await SqlFinanceGet(client, agingCsvPath, deniedScope, makerId);
            Assert.Equal(HttpStatusCode.OK, hiddenCsv.Status);
            Assert.DoesNotContain(invoiceNumber, hiddenCsv.Body, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SqlFinanceGet(client, agingCsvPath, tenantId, makerId, "Student")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SqlFinanceGet(client, agingCsvPath, tenantId, makerId, "")).Status);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SqlFinanceGet(client, agingCsvPath, tenantId, makerId, inventory: false)).Status);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SqlFinanceGet(client, agingCsvPath, tenantId, makerId, finance: false)).Status);

        await using (var grant = Open(options, tenantId, makerId))
            Assert.Equal(1, await grant.UserCampusAccesses
                .Where(x => x.TenantId == tenantId && x.UserId == makerId &&
                    x.CampusId == campusId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true)));

        foreach (var path in new[] { invoicePath, statementPath, capitalizationPath })
            Assert.Equal(HttpStatusCode.OK,
                (await SqlFinanceGet(client, path, tenantId, makerId)).Status);
        Assert.Contains(invoiceReference.ToString(),
            (await SqlFinanceGet(client, listPath, tenantId, makerId)).Body,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(invoiceReference.ToString(),
            (await SqlFinanceGet(client, agingPath, tenantId, makerId)).Body,
            StringComparison.OrdinalIgnoreCase);

        var grantedCsv = await SqlFinanceGet(client, agingCsvPath, tenantId, makerId);
        Assert.Equal(HttpStatusCode.OK, grantedCsv.Status);
        Assert.Contains("SourceLinkedNet_BDT", grantedCsv.Body, StringComparison.Ordinal);
        Assert.Contains(invoiceNumber, grantedCsv.Body, StringComparison.OrdinalIgnoreCase);

        // The original, already-posted source-linked command is a safe replay:
        // no second inventory issue, AssetBook or Finance GL may be created.
        var body = JsonSerializer.Serialize(originalCapitalization);
        using (var noToken = SqlFinanceRequest(HttpMethod.Post, capitalizationPath,
            tenantId, makerId))
        {
            noToken.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(noToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        string token, cookie;
        using (var tokenRequest = SqlFinanceRequest(HttpMethod.Get,
            "/__sql_test/antiforgery", tenantId, makerId))
        using (var response = await client.SendAsync(tokenRequest))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            token = await response.Content.ReadAsStringAsync();
            Assert.True(response.Headers.TryGetValues("Set-Cookie", out var values));
            cookie = values!.First().Split(';')[0];
        }
        using (var withToken = SqlFinanceRequest(HttpMethod.Post, capitalizationPath,
            tenantId, makerId))
        {
            withToken.Headers.TryAddWithoutValidation("Cookie", cookie);
            withToken.Headers.TryAddWithoutValidation("RequestVerificationToken", token);
            withToken.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(withToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        await using (var verify = Open(options, tenantId, makerId))
        {
            Assert.Equal(1, await verify.InventoryMovements.CountAsync(x =>
                x.SourceType == "AssetCapitalization" && x.StateCode == "Posted"));
            Assert.Equal(1, await verify.Journals.CountAsync(x =>
                x.SourceType == "AssetCapitalization" && x.State == JournalState.Posted));
        }

        await using (var revoke = Open(options, tenantId, makerId))
            Assert.Equal(1, await revoke.UserCampusAccesses
                .Where(x => x.TenantId == tenantId && x.UserId == makerId &&
                    x.CampusId == campusId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)));
        // Same client after revocation: no cached private Finance evidence.
        var revokedCsv = await SqlFinanceGet(client, agingCsvPath, tenantId, makerId);
        Assert.Equal(HttpStatusCode.OK, revokedCsv.Status);
        Assert.DoesNotContain(invoiceNumber, revokedCsv.Body, StringComparison.OrdinalIgnoreCase);
        foreach (var path in new[] { invoicePath, statementPath, capitalizationPath })
            Assert.Equal(HttpStatusCode.NotFound,
                (await SqlFinanceGet(client, path, tenantId, makerId)).Status);
        Assert.DoesNotContain(invoiceReference.ToString(),
            (await SqlFinanceGet(client, listPath, tenantId, makerId)).Body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(invoiceReference.ToString(),
            (await SqlFinanceGet(client, agingPath, tenantId, makerId)).Body,
            StringComparison.OrdinalIgnoreCase);
    }
}
