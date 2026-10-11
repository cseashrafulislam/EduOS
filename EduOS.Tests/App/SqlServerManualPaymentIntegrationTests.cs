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
using System.Security.Cryptography;
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
        var data = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };
        storage.Setup(x => x.GetPrivateFileAsync(It.IsAny<string>())).ReturnsAsync(
            new FileDownloadResult { Content = data, ContentType = "application/pdf", FileName = "receipt.pdf" });
        // No scanner installed must never be treated as a successful security review.
        await using var adminDb = new EduOSDbContext(options);
        var admin = new SubscriptionPaymentService(
            new SubscriptionPaymentRepository(adminDb), new SubscriptionInvoiceRepository(adminDb),
            new GenericRepository<Tenant>(adminDb), new GenericRepository<FileAsset>(adminDb), adminDb,
            new TestCurrentUser(tenantId, userId, true), Mock.Of<IAamarPayClient>(), storage.Object,
            new GenericRepository<TenantSubscription>(adminDb), Options.Create(new AamarPaySettings()),
            Options.Create(new ManualPaymentSettings { Enabled = true, BankName = "Test Bank", AccountName = "Test Account", AccountNumber = "000001" }),
            NullLogger<SubscriptionPaymentService>.Instance);
        var pending = await admin.GetPendingManualVerificationsAsync(1, 25, null);
        Assert.True(pending.Success, pending.Message);
        Assert.Contains(pending.Data!.Items, x => x.SubscriptionInvoiceId == invoiceId && !x.ReceiptIsVerifiedSafe);
        var payment = pending.Data.Items.Single(x => x.SubscriptionInvoiceId == invoiceId);
        Assert.Equal("123456", payment.PayerAccountNumber);
        Assert.Equal(409, (await admin.GetDepositSlipAsync(payment.Id)).StatusCode);
        Assert.Equal(409, (await admin.VerifyManualPaymentAsync(new VerifyManualPaymentDto
        {
            PaymentId = payment.Id, Approve = true, RowVersion = payment.RowVersion
        })).StatusCode);
        Assert.Equal(503, (await admin.ScanDepositSlipAsync(payment.Id)).StatusCode);
        storage.Setup(x => x.ScanPrivateForMalwareAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Convert.ToHexString(SHA256.HashData(data)));
        var cleared = await admin.ScanDepositSlipAsync(payment.Id);
        Assert.True(cleared.Success, cleared.Message);
        Assert.True((await admin.GetDepositSlipAsync(payment.Id)).Success);
        var verified = await admin.VerifyManualPaymentAsync(new VerifyManualPaymentDto
        {
            PaymentId = payment.Id, Approve = true, BankCreditConfirmed = true,
            RowVersion = payment.RowVersion, VerificationNote = "Bank credit confirmed"
        });
        Assert.True(verified.Success, verified.Message);
        Assert.Equal(409, (await admin.VerifyManualPaymentAsync(new VerifyManualPaymentDto
        {
            PaymentId = payment.Id, Approve = true, RowVersion = payment.RowVersion
        })).StatusCode);
        adminDb.ChangeTracker.Clear();
        var posted = await adminDb.SubscriptionPayments.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.TenantId == tenantId && x.Id == payment.Id);
        var paidInvoice = await adminDb.SubscriptionInvoices.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Id == invoiceId && x.TenantId == tenantId);
        var active = await adminDb.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Id == paidInvoice.TenantSubscriptionId && x.TenantId == tenantId);
        Assert.Equal(PaymentState.Successful, posted.State);
        Assert.Equal(InvoiceState.Paid, paidInvoice.State);
        Assert.Equal(0m, paidInvoice.DueAmount);
        Assert.Equal(100m, paidInvoice.PaidAmount);
        Assert.Equal(SubscriptionState.Active, active.State);
    }

    private static PrivateFileUploadDto Upload(Stream content) => new()
    {
        Content = content, Length = content.Length, FileName = "receipt.pdf",
        ContentType = "application/pdf"
    };

    private sealed class TestCurrentUser(long tenant, long user, bool superAdmin = false) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => user;
        public long TenantId => tenant;
        public string? FullName => "Payment CI";
        public string? Email => "payment@example.test";
        public bool IsTenantAdmin => !superAdmin;
        public bool IsSuperAdmin => superAdmin;
        public IReadOnlyList<string> Roles => [superAdmin ? "SuperAdmin" : "TenantAdmin"];
        public bool IsInRole(string role) => role == (superAdmin ? "SuperAdmin" : "TenantAdmin");
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL Server CI";
    }
}
