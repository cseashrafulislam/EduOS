using EduOS.Core.DTOs.Admission;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Admission;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AdmissionCashReceiptWorkflowContractTests
{
    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var file = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(file)) return File.ReadAllText(file);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }

    [Fact]
    public async Task Admission_officer_cannot_collect_cash()
    {
        await using var db = Database();
        var service = new AdmissionCashReceiptService(db, new TestUser("AdmissionOfficer"), TimeProvider.System);
        var result = await service.CollectAsync(new CollectAdmissionCashReceiptRequestDto());
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Administrator_cannot_collect_without_exact_fee_and_idempotency_key()
    {
        await using var db = Database();
        var service = new AdmissionCashReceiptService(db, new TestUser("TenantAdmin"), TimeProvider.System);
        var result = await service.CollectAsync(new CollectAdmissionCashReceiptRequestDto
        {
            ApplicantReference = Guid.NewGuid(), PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            AccountingPeriodId = 1, CashAccountId = 2, IncomeAccountId = 3,
            ExpectedAmount = 0, ApplicantRowVersion = "AQ=="
        });
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public void Cash_collection_is_atomic_audited_and_never_misuses_student_payments()
    {
        var source = Read("EduOS.Service", "Services", "Admission", "AdmissionCashReceiptService.cs");
        source.Should().Contain("IsolationLevel.Serializable");
        source.Should().Contain("PaymentState.Initiated");
        source.Should().Contain("PaymentState.Successful");
        source.Should().Contain("JournalState.Posted");
        source.Should().Contain("SourceType = \"AdmissionPayment\"");
        source.Should().Contain("DebitAmount = payment.Amount");
        source.Should().Contain("CreditAmount = payment.Amount");
        source.Should().Contain("await tx.CommitAsync(ct)");
        source.Should().Contain("request.ClientRequestId");
        source.Should().Contain("Version(applicant.RowVersion, request.ApplicantRowVersion)");
        source.Should().Contain("db.AdmissionPayments.Add(payment)");
        source.Should().NotContain("db.StudentPayments.Add");
        source.Should().NotContain("db.SubscriptionPayments.Add");
    }

    [Fact]
    public void Endpoint_and_workspace_are_protected_and_have_real_receipt_actions()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "AdmissionCashReceiptsController.cs");
        var mvc = Read("EduOS.App", "Controllers", "AdmissionCashReceiptsController.cs");
        var view = Read("EduOS.App", "Views", "AdmissionCashReceipts", "Index.cshtml");
        var js = Read("EduOS.App", "wwwroot", "js", "admissions", "cash-receipts.js");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        api.Should().Contain("[RequireModule(\"ADMISSION\")]");
        api.Should().Contain("[RequireModule(\"FINANCE\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        mvc.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        view.Should().Contain("@Html.AntiForgeryToken()");
        view.Should().Contain("admissionCashCollectForm");
        js.Should().Contain("RequestVerificationToken = token");
        js.Should().Contain("credentials: 'same-origin'");
        js.Should().Contain("textContent");
        js.Should().NotContain("innerHTML");
    }

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("admission-cash-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private sealed class TestUser(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
