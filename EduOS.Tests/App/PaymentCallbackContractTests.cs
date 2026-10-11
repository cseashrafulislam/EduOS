using FluentAssertions;
using Xunit;

namespace EduOS.Tests.App;

public class PaymentCallbackContractTests
{
    [Fact]
    public void Anonymous_failure_and_cancel_callbacks_do_not_mutate_payment_state()
    {
        var controller = ReadController();
        var fail = Slice(controller, "public IActionResult FailCallback", "public IActionResult CancelCallback");
        var cancel = Slice(controller, "public IActionResult CancelCallback", "public async Task<IActionResult> IpnCallback");

        fail.Should().NotContain("HandleAamarPayCallbackAsync");
        cancel.Should().NotContain("HandleAamarPayCallbackAsync");
        fail.Should().Contain("PaymentFailed");
        cancel.Should().Contain("PaymentCancelled");
    }

    [Fact]
    public void Anonymous_ipn_only_processes_success_that_service_verifies_with_gateway()
    {
        var controller = ReadController();
        var ipn = Slice(controller, "public async Task<IActionResult> IpnCallback", "// MANUAL PAYMENT");

        ipn.Should().Contain("string.Equals(dto.PayStatus, \"Successful\", StringComparison.OrdinalIgnoreCase)");
        ipn.Should().Contain("HandleAamarPayCallbackAsync(dto)");
        ipn.Should().Contain("Non-success notification acknowledged");
    }

    [Fact]
    public void Verified_gateway_callback_updates_payment_and_invoice_atomically_before_activation()
    {
        var callback = Slice(ReadService(),
            "public async Task<ApiResponse<bool>> HandleAamarPayCallbackAsync",
            "public Task<ApiResponse<SubscriptionPaymentDto>> SubmitManualPaymentAsync");

        var verify = callback.IndexOf("await _gateway.VerifyTransactionAsync(callback.MerTxnid)", StringComparison.Ordinal);
        var transaction = callback.IndexOf("using var tx = SerializableScope();", StringComparison.Ordinal);
        var state = callback.IndexOf("payment.State = PaymentState.Successful;", StringComparison.Ordinal);
        var invoice = callback.IndexOf("invoice.PaidAmount += payment.Amount;", StringComparison.Ordinal);
        var paymentUpdate = callback.IndexOf("_payments.Update(payment);", StringComparison.Ordinal);
        var invoiceUpdate = callback.IndexOf("_invoices.Update(invoice);", StringComparison.Ordinal);
        var save = callback.IndexOf("await _uow.SaveChangesAsync();", StringComparison.Ordinal);
        var activation = callback.IndexOf("await ActivatePaidSubscriptionAsync(invoice);", StringComparison.Ordinal);
        var commit = callback.IndexOf("tx.Complete();", activation, StringComparison.Ordinal);

        verify.Should().BeGreaterThanOrEqualTo(0);
        transaction.Should().BeGreaterThan(verify);
        state.Should().BeGreaterThan(transaction);
        invoice.Should().BeGreaterThan(state);
        paymentUpdate.Should().BeGreaterThan(invoice);
        invoiceUpdate.Should().BeGreaterThan(paymentUpdate);
        save.Should().BeGreaterThan(invoiceUpdate);
        activation.Should().BeGreaterThan(save);
        commit.Should().BeGreaterThan(activation);
        callback.Should().Contain("payment.State == PaymentState.Successful");
        callback.Should().Contain("payment.Amount <= 0");
        callback.Should().Contain("catch (DbUpdateConcurrencyException)");
    }

    [Fact]
    public void Manual_payment_review_updates_payment_and_invoice_atomically_before_activation()
    {
        var verify = Slice(ReadService(),
            "public async Task<ApiResponse<bool>> VerifyManualPaymentAsync",
            "public async Task<ApiResponse<List<SubscriptionPaymentDto>>> GetByInvoiceAsync");
        verify.Should().Contain("return await _uow.ExecuteInTransactionAsync(async token =>");
        verify.Should().Contain("System.Data.IsolationLevel.Serializable");
        verify.Should().Contain("payment.State != PaymentState.AwaitingVerification");
        verify.Should().Contain("file?.IsVerifiedSafe != true");
        verify.Should().Contain("payment.State = PaymentState.Successful;");
        verify.Should().Contain("invoice.PaidAmount += payment.Amount;");
        verify.Should().Contain("_payments.Update(payment);");
        verify.Should().Contain("_invoices.Update(invoice);");
        verify.Should().Contain("await _uow.SaveChangesAsync(token);");
        verify.Should().Contain("await ActivatePaidSubscriptionAsync(invoice);");
        verify.Should().Contain("CryptographicOperations.FixedTimeEquals");
        verify.Should().NotContain("using var tx = SerializableScope();");
        verify.Should().Contain("if (!_user.IsSuperAdmin)");
        verify.Should().Contain("catch (DbUpdateConcurrencyException)");
    }

    [Fact]
    public void Unsafe_receipts_cannot_be_read_or_approved_without_real_scan()
    {
        var service = ReadService();
        var download = Slice(service, "public async Task<ApiResponse<PrivateFileDownloadDto>> GetDepositSlipAsync",
            "public async Task<ApiResponse<bool>> ScanDepositSlipAsync");
        var scan = Slice(service, "public async Task<ApiResponse<bool>> ScanDepositSlipAsync",
            "public async Task<ApiResponse<bool>> VerifyManualPaymentAsync");
        download.Should().Contain("file.MalwareScanStateCode != \"Clean\"");
        download.Should().Contain("SHA256.HashData(download.Content)");
        scan.Should().Contain("_storage.ScanPrivateForMalwareAsync(file.StorageKey)");
        scan.Should().Contain("if (string.IsNullOrWhiteSpace(scannedHash))");
        scan.Should().Contain("currentFile.IsVerifiedSafe = true;");
    }

    [Fact]
    public void Bank_transfer_form_generates_a_csrf_token_for_multipart_requests()
    {
        var view = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Payment.cshtml"));
        view.Should().Contain("id=\"manualForm\" method=\"post\"");
        view.Should().Contain("@Html.AntiForgeryToken()");
        view.Should().Contain("name=\"depositSlip\"");
    }

    [Fact]
    public void Bank_transfer_javascript_sends_csrf_and_does_not_hide_server_validation()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "payment.js"));
        script.Should().Contain("new FormData(form)");
        script.Should().Contain("'RequestVerificationToken': antiforgeryToken()");
        script.Should().Contain("responseError(payload, i18n.submitFailed)");
        script.Should().Contain("response.status === 409 && payload?.message === 'A payment is already pending.'");
    }

    [Fact]
    public void Manual_transfer_uses_ef_managed_atomic_transaction_and_stable_retry_identity()
    {
        var manual = Slice(ReadService(),
            "public async Task<ApiResponse<SubscriptionPaymentDto>> SubmitManualPaymentAsync",
            "public async Task<ApiResponse<ManualPaymentInstructionsDto>> GetManualPaymentInstructionsAsync");
        manual.Should().Contain("_uow.ExecuteInTransactionAsync(async token =>");
        manual.Should().Contain("System.Data.IsolationLevel.Serializable");
        manual.Should().Contain("var requestId = Guid.NewGuid();");
        manual.Should().Contain("x.ClientRequestId == requestId");
        manual.Should().Contain("ClientRequestId = requestId");
        manual.Should().Contain("if (response.Success) storageKey = null;");
        manual.Should().NotContain("using var tx = SerializableScope();");
        manual.Should().Contain("await _storage.DeletePrivateAsync(storageKey)");
    }

    [Fact]
    public void SuperAdmin_sidebar_and_review_page_enforce_safe_receipt_decisions()
    {
        var layout = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "_Layout.cshtml"));
        var view = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "PaymentVerification.cshtml"));
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "payment-verification.js"));
        layout.Should().Contain("User?.IsInRole(\"SuperAdmin\")");
        layout.Should().Contain("href=\"/Account/PaymentVerification\"");
        view.Should().Contain("id=\"btnScanReceipt\"");
        view.Should().Contain("id=\"bankCreditConfirmed\"");
        view.Should().Contain("id=\"btnNextPage\"");
        script.Should().Contain("rowVersion: selectedPayment.rowVersion");
        script.Should().Contain("bankCreditConfirmed:");
        script.Should().Contain("scan-receipt");
        script.Should().Contain("pageSize=${pageSize}&search=${encodeURIComponent(search)}");
        script.Should().Contain("updateReceiptSafety");
        script.Should().Contain("'RequestVerificationToken': getAntiForgeryToken()");
    }

    private static string ReadController()
    {
        return File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "SubscriptionPaymentController.cs"));
    }

    private static string ReadService()
    {
        return File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "SubscriptionPaymentService.cs"));
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }
}
