using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Persistence.Repositories.SaaS;
using EduOS.Service.Helpers.Payment;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.SaaS;
using EduOS.Core.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

public sealed class SqlServerManualPaymentIntegrationTests
{
    [Fact]
    public async Task Manual_transfer_creates_one_pending_payment_and_one_private_file_with_retry_safety()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, userId, invoiceId;
        await using (var seed = new EduOSDbContext(options))
        {
            var tenant = new Tenant
            {
                Code = "CI-PAY-" + suffix, Name = "Manual Payment CI",
                Email = "payment-" + suffix + "@example.test", State = TenantState.Active
            };
            var user = new ApplicationUser
            {
                UserName = "pay-" + suffix, NormalizedUserName = "PAY-" + suffix.ToUpperInvariant(),
                Email = "payer-" + suffix + "@example.test", FullName = "Payment tester"
            };
            var plan = new SubscriptionPlan { Code = "PAY-" + suffix, Name = "Payment CI Plan", MonthlyPrice = 100m };
            seed.Tenants.Add(tenant); seed.Users.Add(user); seed.SubscriptionPlans.Add(plan);
            await seed.SaveChangesAsync();
            tenantId = tenant.Id; userId = user.Id;
            seed.ChangeTracker.Clear();
            using (seed.BeginSystemTenantScope(tenantId))
            {
                var subscription = new TenantSubscription
                {
                    TenantId = tenantId, SubscriptionPlanId = plan.Id,
                    State = SubscriptionState.PendingPayment, PriceSnapshot = 100m,
                    StartsAt = DateTime.UtcNow, EndsAt = DateTime.UtcNow.AddMonths(1)
                };
                seed.TenantSubscriptions.Add(subscription);
                await seed.SaveChangesAsync();
                var invoice = new SubscriptionInvoice
                {
                    TenantId = tenantId, TenantSubscriptionId = subscription.Id,
                    InvoiceNumber = "CI-PAY-" + suffix,
                    InvoiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                    Subtotal = 100m, TotalAmount = 100m, DueAmount = 100m,
                    State = InvoiceState.Issued
                };
                seed.SubscriptionInvoices.Add(invoice);
                await seed.SaveChangesAsync();
                invoiceId = invoice.Id;
            }
        }

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenantId.ToString())
            }, "TestAuthentication"))
        };
        http.Items["TenantId"] = tenantId;
        await using var db = new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
        var storage = new Mock<IFileUploadService>();
        var uploads = 0;
        storage.Setup(x => x.ValidateFile(It.IsAny<IFormFile>())).Returns(true);
        storage.Setup(x => x.UploadPrivateForTenantAsync(It.IsAny<IFormFile>(), "deposit-slips", tenantId))
            .ReturnsAsync(() => new FileUploadResult
            {
                Success = true,
                FileUrl = "tenant-" + tenantId + "/deposit-slips/ci-" + (++uploads) + ".pdf"
            });
        storage.Setup(x => x.DeletePrivateAsync(It.IsAny<string>())).ReturnsAsync(true);
        var paymentService = new SubscriptionPaymentService(
            new SubscriptionPaymentRepository(db), new SubscriptionInvoiceRepository(db),
            new GenericRepository<Tenant>(db), new GenericRepository<FileAsset>(db), db,
            new TestCurrentUser(tenantId, userId), Mock.Of<IAamarPayClient>(), storage.Object,
            new GenericRepository<TenantSubscription>(db), Options.Create(new AamarPaySettings()),
            Options.Create(new ManualPaymentSettings
            {
                Enabled = true, BankName = "Test Bank", AccountName = "Test Account",
                AccountNumber = "000001"
            }), NullLogger<SubscriptionPaymentService>.Instance);
        var request = new ManualPaymentSubmitDto
        {
            InvoiceId = invoiceId, Amount = 100m,
            PayerBankName = "Payer Bank", PayerAccountNumber = "123456",
            DepositSlipNumber = "PAYREF-" + suffix, DepositDate = DateTime.UtcNow.Date
        };
        using (var receipt = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 }))
        {
            var first = await paymentService.SubmitManualPaymentAsync(request, Upload(receipt));
            Assert.True(first.Success, first.Message);
            Assert.Equal(PaymentState.AwaitingVerification, first.Data!.State);
        }
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.SubscriptionPayments.CountAsync(x => x.TenantId == tenantId));
        Assert.Equal(1, await db.FileAssets.CountAsync(x => x.TenantId == tenantId));
        using (var retryReceipt = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 }))
        {
            var repeat = await paymentService.SubmitManualPaymentAsync(request, Upload(retryReceipt));
            Assert.Equal(409, repeat.StatusCode);
            Assert.Equal("A payment is already pending.", repeat.Message);
        }
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.SubscriptionPayments.CountAsync(x => x.TenantId == tenantId));
        Assert.Equal(1, await db.FileAssets.CountAsync(x => x.TenantId == tenantId));
        storage.Verify(x => x.DeletePrivateAsync(It.IsAny<string>()), Times.Once);
    }

    private static PrivateFileUploadDto Upload(Stream content) => new()
    {
        Content = content, Length = content.Length, FileName = "receipt.pdf",
        ContentType = "application/pdf"
    };

    private sealed class TestCurrentUser(long tenant, long user) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => user;
        public long TenantId => tenant;
        public string? FullName => "Payment CI";
        public string? Email => "payment@example.test";
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL Server CI";
    }
}
