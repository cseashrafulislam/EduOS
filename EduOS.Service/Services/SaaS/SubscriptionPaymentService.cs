using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Settings;
using EduOS.Service.Helpers.Payment;
using EduOS.Service.Helpers.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Cryptography;
using System.Transactions;

namespace EduOS.Service.Services.SaaS;

public sealed class SubscriptionPaymentService : ISubscriptionPaymentService
{
    private readonly ISubscriptionPaymentRepository _payments;
    private readonly ISubscriptionInvoiceRepository _invoices;
    private readonly IGenericRepository<Tenant> _tenants;
    private readonly IGenericRepository<FileAsset> _files;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly IAamarPayClient _gateway;
    private readonly IFileUploadService _storage;
    private readonly IGenericRepository<TenantSubscription> _subscriptionRows;
    private readonly AamarPaySettings _gatewaySettings;
    private readonly ManualPaymentSettings _manualSettings;
    private readonly ILogger<SubscriptionPaymentService> _logger;

    public SubscriptionPaymentService(ISubscriptionPaymentRepository paymentRepo,
        ISubscriptionInvoiceRepository invoiceRepo, IGenericRepository<Tenant> tenantRepo,
        IGenericRepository<FileAsset> fileRepo, IUnitOfWork unitOfWork,
        ICurrentUserService currentUser, IAamarPayClient aamarPay, IFileUploadService fileStorage,
        IGenericRepository<TenantSubscription> subscriptionRows, IOptions<AamarPaySettings> aamarPaySettings,
        IOptions<ManualPaymentSettings> manualSettings, ILogger<SubscriptionPaymentService> logger)
    {
        _payments = paymentRepo; _invoices = invoiceRepo; _tenants = tenantRepo; _files = fileRepo;
        _uow = unitOfWork; _user = currentUser; _gateway = aamarPay; _storage = fileStorage;
        _subscriptionRows = subscriptionRows; _gatewaySettings = aamarPaySettings.Value;
        _manualSettings = manualSettings.Value; _logger = logger;
    }

    public async Task<ApiResponse<InitiatePaymentResponseDto>> InitiateAamarPayAsync(InitiatePaymentRequestDto dto)
    {
        if (!CanRead()) return ApiResponse<InitiatePaymentResponseDto>.ErrorResponse("Tenant access is required.", 403);
        if (dto == null || dto.InvoiceId <= 0 || dto.PaymentMethod != PaymentMethodType.Gateway)
            return ApiResponse<InitiatePaymentResponseDto>.ErrorResponse("Select a valid invoice and AamarPay.");
        if (!TryGetTrustedCallbackBaseUrl(out var origin))
            return ApiResponse<InitiatePaymentResponseDto>.ErrorResponse("Online payment is not configured.", 503);
        var tenant = _user.TenantId;
        SubscriptionPayment? created = null;
        try
        {
            using (var tx = SerializableScope())
            {
                var invoice = await _invoices.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == dto.InvoiceId);
                if (invoice == null) return ApiResponse<InitiatePaymentResponseDto>.ErrorResponse("Invoice not found.", 404);
                if (invoice.State is InvoiceState.Paid or InvoiceState.Cancelled or InvoiceState.Refunded || invoice.DueAmount <= 0)
                    return ApiResponse<InitiatePaymentResponseDto>.ErrorResponse("Invoice is not payable.", 409);
                if (!string.Equals(invoice.CurrencyCode, _gatewaySettings.Currency, StringComparison.OrdinalIgnoreCase))
                    return ApiResponse<InitiatePaymentResponseDto>.ErrorResponse("Gateway currency does not match the invoice.", 409);
                if (await _payments.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    x.SubscriptionInvoiceId == invoice.Id && (x.State == PaymentState.Initiated ||
                    x.State == PaymentState.AwaitingVerification)))
                    return ApiResponse<InitiatePaymentResponseDto>.ErrorResponse("An invoice payment is already pending.", 409);
                created = new SubscriptionPayment
                {
                    TenantId = tenant, ClientRequestId = Guid.NewGuid(), PublicId = Guid.NewGuid(),
                    SubscriptionInvoiceId = invoice.Id,
                    TransactionId = TransactionId("EDU", tenant, invoice.Id), GatewayCode = "AamarPay",
                    PaymentMethod = PaymentMethodType.Gateway, Amount = invoice.DueAmount,
                    CurrencyCode = invoice.CurrencyCode, State = PaymentState.Initiated,
                    InitiatedAt = DateTime.UtcNow, CreatedBy = _user.UserId
                };
                await _payments.AddAsync(created);
                await _uow.SaveChangesAsync();
                tx.Complete();
            }

            var info = await _invoices.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && x.Id == dto.InvoiceId)
                .Select(x => new { x.InvoiceNumber }).FirstAsync();
            var customer = await _tenants.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenant);
            var request = new AamarPayRequest
            {
                TransactionId = created.TransactionId, Amount = created.Amount,
                Description = "Subscription invoice " + info.InvoiceNumber,
                CustomerName = customer?.Name ?? "Institution",
                CustomerEmail = customer?.Email ?? "noreply@eduos.com",
                CustomerPhone = customer?.Phone ?? "01700000000",
                CustomerAddress = customer?.Address, CustomerCity = null, CustomerCountry = customer?.CountryCode,
                SuccessUrl = origin + "/api/subscription-payment/callback/success",
                FailUrl = origin + "/api/subscription-payment/callback/fail",
                CancelUrl = origin + "/api/subscription-payment/callback/cancel"
            };
            var result = await _gateway.InitiatePaymentAsync(request);
            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.PaymentUrl))
            {
                await FailPendingGatewayAsync(created.Id, tenant, result.ErrorMessage ?? "Gateway checkout could not be created.");
                return ApiResponse<InitiatePaymentResponseDto>.ErrorResponse("Payment gateway is unavailable.", 503);
            }
            return ApiResponse<InitiatePaymentResponseDto>.SuccessResponse(new InitiatePaymentResponseDto
            {
                TransactionId = created.TransactionId, PaymentUrl = result.PaymentUrl,
                State = PaymentState.Initiated, Message = "Continue to secure payment checkout."
            });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Concurrent online subscription payment initiation for tenant {TenantId}", tenant);
            return ApiResponse<InitiatePaymentResponseDto>.ErrorResponse("Payment initiation conflicts with another request.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Online subscription payment initiation failed for tenant {TenantId}", tenant);
            return ApiResponse<InitiatePaymentResponseDto>.ErrorResponse("Payment initiation failed.", 500);
        }
    }

    public async Task<ApiResponse<bool>> HandleAamarPayCallbackAsync(AamarPayCallbackDto callback)
    {
        if (string.IsNullOrWhiteSpace(callback?.MerTxnid))
            return ApiResponse<bool>.ErrorResponse("Transaction reference is required.");
        try
        {
            // Provider verification happens outside the financial transaction.
            // Never credit an invoice using the unauthenticated callback payload alone.
            var verified = await _gateway.VerifyTransactionAsync(callback.MerTxnid);
            var matchedAmount = decimal.TryParse(verified.Amount, NumberStyles.Number,
                CultureInfo.InvariantCulture, out var amount);
            using var tx = SerializableScope();
            var payment = await _payments.GetByTransactionIdForCallbackAsync(callback.MerTxnid);
            if (payment == null || payment.GatewayCode != "AamarPay")
                return ApiResponse<bool>.ErrorResponse("Payment not found.", 404);
            if (payment.State == PaymentState.Successful)
            {
                tx.Complete();
                return ApiResponse<bool>.SuccessResponse(true, "Payment was already applied.");
            }
            var invoice = await _invoices.GetByIdForSystemAsync(payment.SubscriptionInvoiceId, payment.TenantId);
            if (invoice == null) return ApiResponse<bool>.ErrorResponse("Invoice not found.", 404);
            if (!verified.IsSuccess || !matchedAmount || amount != payment.Amount ||
                !string.Equals(payment.CurrencyCode, invoice.CurrencyCode, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(verified.Currency) &&
                !string.Equals(verified.Currency, payment.CurrencyCode, StringComparison.OrdinalIgnoreCase)))
            {
                // Leave this transaction unreconciled; a false or forged failure callback
                // must not destroy the ability to receive a subsequent verified success.
                _logger.LogWarning("Provider verification mismatch for payment {PaymentId}", payment.Id);
                return ApiResponse<bool>.ErrorResponse("Payment verification did not succeed.", 409);
            }
            if (invoice.State is InvoiceState.Cancelled or InvoiceState.Refunded or InvoiceState.Paid ||
                invoice.DueAmount < payment.Amount || payment.Amount <= 0)
                return ApiResponse<bool>.ErrorResponse("Invoice balance cannot accept this payment.", 409);
            var now = DateTime.UtcNow;
            payment.State = PaymentState.Successful;
            payment.CompletedAt = now;
            payment.ProviderTransactionId = callback.PgTxnid;
            payment.ExternalReference = callback.BankTxnid;
            payment.FailureReason = null;
            payment.UpdatedAt = now;
            invoice.PaidAmount += payment.Amount;
            invoice.DueAmount = invoice.TotalAmount - invoice.PaidAmount;
            invoice.State = invoice.DueAmount == 0m ? InvoiceState.Paid : InvoiceState.PartiallyPaid;
            if (invoice.State == InvoiceState.Paid) invoice.PaidAt = now;
            invoice.UpdatedAt = now;
            // Invoice repository returns a no-tracking system DTO: explicitly attach it.
            _payments.Update(payment);
            _invoices.Update(invoice);
            await _uow.SaveChangesAsync();
            if (invoice.State == InvoiceState.Paid)
                await ActivatePaidSubscriptionAsync(invoice);
            tx.Complete();
            return ApiResponse<bool>.SuccessResponse(true, "Verified payment applied.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<bool>.ErrorResponse("Concurrent payment processing detected. Retry callback.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subscription payment callback failed for {TransactionId}", callback.MerTxnid);
            return ApiResponse<bool>.ErrorResponse("Payment verification is pending reconciliation.", 500);
        }
    }

    public Task<ApiResponse<SubscriptionPaymentDto>> SubmitManualPaymentAsync(
        ManualPaymentSubmitDto dto, PrivateFileUploadDto? depositSlip)
    {
        if (depositSlip == null) return SubmitManualPaymentAsync(dto, (IFormFile?)null);
        var form = new FormFile(depositSlip.Content, 0, depositSlip.Length, "DepositSlip", depositSlip.FileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = depositSlip.ContentType
        };
        return SubmitManualPaymentAsync(dto, form);
    }

    public async Task<ApiResponse<SubscriptionPaymentDto>> SubmitManualPaymentAsync(
        ManualPaymentSubmitDto dto, IFormFile? depositSlip)
    {
        if (!CanRead()) return ApiResponse<SubscriptionPaymentDto>.ErrorResponse("Tenant access is required.", 403);
        if (!_manualSettings.Enabled || string.IsNullOrWhiteSpace(_manualSettings.BankName) ||
            string.IsNullOrWhiteSpace(_manualSettings.AccountName) ||
            string.IsNullOrWhiteSpace(_manualSettings.AccountNumber))
            return ApiResponse<SubscriptionPaymentDto>.ErrorResponse("Manual payment is not configured.", 503);
        if (dto == null || dto.InvoiceId <= 0 || dto.Amount <= 0 ||
            string.IsNullOrWhiteSpace(dto.PayerBankName) ||
            string.IsNullOrWhiteSpace(dto.PayerAccountNumber) ||
            string.IsNullOrWhiteSpace(dto.DepositSlipNumber) ||
            dto.DepositDate == default || dto.DepositDate.Date > DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(6)).Date)
            return ApiResponse<SubscriptionPaymentDto>.ErrorResponse("Valid deposit information is required.");
        if (depositSlip == null || depositSlip.Length <= 0 || depositSlip.Length > 5 * 1024L * 1024L ||
            !new[] { ".pdf", ".jpg", ".jpeg", ".png" }.Contains(Path.GetExtension(depositSlip.FileName).ToLowerInvariant()) ||
            !new[] { "application/pdf", "image/jpeg", "image/png" }.Contains(depositSlip.ContentType.ToLowerInvariant()) ||
            !_storage.ValidateFile(depositSlip))
            return ApiResponse<SubscriptionPaymentDto>.ErrorResponse("Valid PDF/JPG/PNG deposit slip (max 5MB) is required.");
        var tenant = _user.TenantId;
        var original = await _invoices.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == dto.InvoiceId);
        if (original == null) return ApiResponse<SubscriptionPaymentDto>.ErrorResponse("Invoice not found.", 404);
        if (original.DueAmount != dto.Amount || original.State is InvoiceState.Paid or InvoiceState.Cancelled or InvoiceState.Refunded)
            return ApiResponse<SubscriptionPaymentDto>.ErrorResponse("Submitted amount must match the payable invoice balance.", 409);
        string? storageKey = null;
        try
        {
            var upload = await _storage.UploadPrivateForTenantAsync(depositSlip, "deposit-slips", tenant);
            if (!upload.Success || string.IsNullOrWhiteSpace(upload.FileUrl))
                return ApiResponse<SubscriptionPaymentDto>.ErrorResponse(upload.ErrorMessage ?? "Deposit slip upload failed.");
            storageKey = upload.FileUrl;
            using var tx = SerializableScope();
            var invoice = await _invoices.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == dto.InvoiceId);
            if (invoice == null || invoice.DueAmount != dto.Amount ||
                invoice.State is InvoiceState.Paid or InvoiceState.Cancelled or InvoiceState.Refunded)
                return ApiResponse<SubscriptionPaymentDto>.ErrorResponse("Invoice balance changed. Reload and retry.", 409);
            if (await _payments.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.SubscriptionInvoiceId == dto.InvoiceId &&
                (x.State == PaymentState.Initiated || x.State == PaymentState.AwaitingVerification)))
                return ApiResponse<SubscriptionPaymentDto>.ErrorResponse("A payment is already pending.", 409);
            var now = DateTime.UtcNow;
            var file = new FileAsset
            {
                TenantId = tenant, StorageProvider = "local-private", StorageKey = storageKey,
                OriginalFileName = Path.GetFileName(depositSlip.FileName), ContentType = depositSlip.ContentType,
                SizeBytes = depositSlip.Length, Visibility = FileVisibility.Private,
                UploadedByUserId = _user.UserId, UploadedAt = now,
                MalwareScanStateCode = "Pending", IsVerifiedSafe = false, CreatedAt = now
            };
            await _files.AddAsync(file);
            await _uow.SaveChangesAsync();
            var payment = new SubscriptionPayment
            {
                TenantId = tenant, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(),
                SubscriptionInvoiceId = invoice.Id, TransactionId = TransactionId("MAN", tenant, invoice.Id),
                PaymentMethod = PaymentMethodType.BankTransfer, Amount = dto.Amount, CurrencyCode = invoice.CurrencyCode,
                State = PaymentState.AwaitingVerification, InitiatedAt = now,
                PayerBankName = dto.PayerBankName.Trim(), DepositSlipNumber = dto.DepositSlipNumber.Trim(),
                DepositDate = DateOnly.FromDateTime(dto.DepositDate),
                DepositSlipFileId = file.Id, VerificationNote = dto.Note?.Trim(),
                CreatedAt = now, CreatedBy = _user.UserId
            };
            await _payments.AddAsync(payment);
            await _uow.SaveChangesAsync();
            var result = Map(payment, invoice.InvoiceNumber);
            tx.Complete();
            storageKey = null;
            return ApiResponse<SubscriptionPaymentDto>.SuccessResponse(result, "Payment submitted for verification.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Concurrent manual subscription payment for tenant {TenantId}", tenant);
            return ApiResponse<SubscriptionPaymentDto>.ErrorResponse("Payment conflicts with another submission.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual subscription payment failed for tenant {TenantId}", tenant);
            return ApiResponse<SubscriptionPaymentDto>.ErrorResponse("Payment submission failed.", 500);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(storageKey))
                await _storage.DeletePrivateAsync(storageKey);
        }
    }

    public async Task<ApiResponse<ManualPaymentInstructionsDto>> GetManualPaymentInstructionsAsync(long invoiceId)
    {
        if (!CanRead()) return ApiResponse<ManualPaymentInstructionsDto>.ErrorResponse("Tenant access required.", 403);
        if (!_manualSettings.Enabled) return ApiResponse<ManualPaymentInstructionsDto>.ErrorResponse("Manual payment unavailable.", 503);
        var invoice = await _invoices.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == _user.TenantId && x.Id == invoiceId);
        if (invoice == null) return ApiResponse<ManualPaymentInstructionsDto>.ErrorResponse("Invoice not found.", 404);
        if (string.IsNullOrWhiteSpace(_manualSettings.BankName) || string.IsNullOrWhiteSpace(_manualSettings.AccountName) ||
            string.IsNullOrWhiteSpace(_manualSettings.AccountNumber))
            return ApiResponse<ManualPaymentInstructionsDto>.ErrorResponse("Manual payment configuration is incomplete.", 503);
        return ApiResponse<ManualPaymentInstructionsDto>.SuccessResponse(new ManualPaymentInstructionsDto
        {
            BankName = _manualSettings.BankName, AccountName = _manualSettings.AccountName,
            AccountNumber = _manualSettings.AccountNumber, RoutingNumber = _manualSettings.RoutingNumber,
            BranchName = _manualSettings.BranchName, Reference = invoice.InvoiceNumber,
            Instructions = _manualSettings.Instructions
        });
    }

    public async Task<ApiResponse<PrivateFileDownloadDto>> GetDepositSlipAsync(long paymentId)
    {
        if (!_user.IsSuperAdmin) return ApiResponse<PrivateFileDownloadDto>.ErrorResponse("Platform administrator access required.", 403);
        var payment = await _payments.GetByIdForPlatformAsync(paymentId);
        if (payment?.DepositSlipFileId == null)
            return ApiResponse<PrivateFileDownloadDto>.ErrorResponse("Deposit slip not found.", 404);
        var file = await _files.GetQueryable().IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            !x.IsDeleted && x.TenantId == payment.TenantId &&
            x.Id == payment.DepositSlipFileId && x.Visibility == FileVisibility.Private);
        if (file == null) return ApiResponse<PrivateFileDownloadDto>.ErrorResponse("Deposit slip not found.", 404);
        if (!file.IsVerifiedSafe)
            return ApiResponse<PrivateFileDownloadDto>.ErrorResponse("Deposit slip is pending file safety verification.", 409);
        var download = await _storage.GetPrivateFileAsync(file.StorageKey);
        if (download == null) return ApiResponse<PrivateFileDownloadDto>.ErrorResponse("Deposit slip not found.", 404);
        return ApiResponse<PrivateFileDownloadDto>.SuccessResponse(new PrivateFileDownloadDto
        {
            Content = download.Content, ContentType = download.ContentType, FileName = download.FileName
        });
    }

    public async Task<ApiResponse<bool>> VerifyManualPaymentAsync(VerifyManualPaymentDto dto)
    {
        if (!_user.IsSuperAdmin) return ApiResponse<bool>.ErrorResponse("Platform administrator access required.", 403);
        if (dto == null || dto.PaymentId <= 0)
            return ApiResponse<bool>.ErrorResponse("Valid payment reference required.");
        if (!dto.Approve && string.IsNullOrWhiteSpace(dto.VerificationNote))
            return ApiResponse<bool>.ErrorResponse("A rejection reason is required.");
        try
        {
            using var tx = SerializableScope();
            var payment = await _payments.GetByIdForPlatformAsync(dto.PaymentId);
            if (payment == null) return ApiResponse<bool>.ErrorResponse("Payment not found.", 404);
            if (payment.PaymentMethod != PaymentMethodType.BankTransfer || payment.State != PaymentState.AwaitingVerification)
                return ApiResponse<bool>.ErrorResponse("Payment cannot be verified in its current state.", 409);
            var invoice = await _invoices.GetByIdForSystemAsync(payment.SubscriptionInvoiceId, payment.TenantId);
            if (invoice == null) return ApiResponse<bool>.ErrorResponse("Invoice not found.", 404);
            if (payment.CurrencyCode != invoice.CurrencyCode)
                return ApiResponse<bool>.ErrorResponse("Payment currency and invoice currency differ.", 409);
            if (dto.Approve && payment.DepositSlipFileId.HasValue)
            {
                var file = await _files.GetQueryable().IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
                    !x.IsDeleted && x.TenantId == payment.TenantId && x.Id == payment.DepositSlipFileId.Value);
                if (file?.IsVerifiedSafe != true)
                    return ApiResponse<bool>.ErrorResponse("Deposit slip must pass file safety verification first.", 409);
            }
            if (dto.Approve && (invoice.State is InvoiceState.Cancelled or InvoiceState.Refunded or InvoiceState.Paid ||
                payment.Amount <= 0 || payment.Amount > invoice.DueAmount))
                return ApiResponse<bool>.ErrorResponse("Invoice balance cannot accept the payment.", 409);
            var now = DateTime.UtcNow;
            payment.VerifiedAt = now; payment.VerifiedByUserId = _user.UserId;
            payment.VerificationNote = dto.VerificationNote?.Trim(); payment.UpdatedAt = now;
            if (dto.Approve)
            {
                payment.State = PaymentState.Successful; payment.CompletedAt = now; payment.FailureReason = null;
                invoice.PaidAmount += payment.Amount;
                invoice.DueAmount = invoice.TotalAmount - invoice.PaidAmount;
                invoice.State = invoice.DueAmount == 0m ? InvoiceState.Paid : InvoiceState.PartiallyPaid;
                if (invoice.State == InvoiceState.Paid) invoice.PaidAt = now;
                invoice.UpdatedAt = now;
            }
            else
            {
                payment.State = PaymentState.Failed;
                payment.FailureReason = payment.VerificationNote;
            }
            // Platform repository methods are deliberately AsNoTracking.
            _payments.Update(payment);
            if (dto.Approve) _invoices.Update(invoice);
            await _uow.SaveChangesAsync();
            if (dto.Approve && invoice.State == InvoiceState.Paid)
                await ActivatePaidSubscriptionAsync(invoice);
            tx.Complete();
            return ApiResponse<bool>.SuccessResponse(true, dto.Approve ? "Payment approved." : "Payment rejected.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<bool>.ErrorResponse("Payment was verified concurrently.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual subscription payment verification failed for {PaymentId}", dto.PaymentId);
            return ApiResponse<bool>.ErrorResponse("Payment verification failed.", 500);
        }
    }

    public async Task<ApiResponse<List<SubscriptionPaymentDto>>> GetByInvoiceAsync(long invoiceId)
    {
        if (!_user.IsSuperAdmin && !CanRead())
            return ApiResponse<List<SubscriptionPaymentDto>>.ErrorResponse("Tenant access required.", 403);
        var invoice = _user.IsSuperAdmin
            ? await _invoices.GetByIdForPlatformAsync(invoiceId)
            : await _invoices.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.Id == invoiceId && x.TenantId == _user.TenantId);
        if (invoice == null) return ApiResponse<List<SubscriptionPaymentDto>>.ErrorResponse("Invoice not found.", 404);
        var payments = _user.IsSuperAdmin
            ? await _payments.GetByInvoiceForPlatformAsync(invoiceId, invoice.TenantId)
            : await _payments.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
                x.SubscriptionInvoiceId == invoiceId).OrderByDescending(x => x.InitiatedAt).Take(200).ToListAsync();
        return ApiResponse<List<SubscriptionPaymentDto>>.SuccessResponse(
            payments.Select(x => Map(x, invoice.InvoiceNumber)).ToList());
    }

    public async Task<ApiResponse<List<SubscriptionPaymentDto>>> GetPendingManualVerificationsAsync()
    {
        if (!_user.IsSuperAdmin)
            return ApiResponse<List<SubscriptionPaymentDto>>.ErrorResponse("Platform administrator access required.", 403);
        var payments = await _payments.GetPendingManualVerificationForPlatformAsync();
        var ids = payments.Select(x => x.SubscriptionInvoiceId).Distinct().ToArray();
        var invoiceNos = await _invoices.GetQueryable().IgnoreQueryFilters().AsNoTracking()
            .Where(x => !x.IsDeleted && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.InvoiceNumber);
        return ApiResponse<List<SubscriptionPaymentDto>>.SuccessResponse(
            payments.Select(x => Map(x, invoiceNos.GetValueOrDefault(x.SubscriptionInvoiceId) ?? string.Empty)).ToList());
    }

    private async Task ActivatePaidSubscriptionAsync(SubscriptionInvoice invoice)
    {
        if (invoice.State != InvoiceState.Paid || invoice.DueAmount != 0m ||
            invoice.PaidAmount != invoice.TotalAmount)
            throw new InvalidOperationException("Subscription invoice must be fully reconciled before activation.");
        var subscription = await _subscriptionRows.GetQueryable().IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == invoice.TenantId &&
                x.Id == invoice.TenantSubscriptionId && !x.IsDeleted);
        if (subscription == null)
            throw new InvalidOperationException("Subscription is not available for the settled invoice.");
        if (subscription.State == SubscriptionState.Active) return;
        if (subscription.State != SubscriptionState.PendingPayment)
            throw new InvalidOperationException("Subscription cannot be activated from this state.");
        subscription.State = SubscriptionState.Active;
        subscription.UpdatedAt = DateTime.UtcNow;
        _subscriptionRows.Update(subscription);
        await _uow.SaveChangesAsync();
    }

    private async Task FailPendingGatewayAsync(long paymentId, long tenantId, string reason)
    {
        using var scope = SerializableScope();
        var payment = await _payments.GetQueryable().FirstOrDefaultAsync(x =>
            x.Id == paymentId && x.TenantId == tenantId);
        if (payment?.State == PaymentState.Initiated)
        {
            payment.State = PaymentState.Failed;
            payment.FailureReason = reason.Length > 1000 ? reason[..1000] : reason;
            await _uow.SaveChangesAsync();
        }
        scope.Complete();
    }

    private static SubscriptionPaymentDto Map(SubscriptionPayment row, string invoiceNumber) => new()
    {
        Id = row.Id, Reference = row.PublicId, SubscriptionInvoiceId = row.SubscriptionInvoiceId,
        InvoiceNumber = invoiceNumber, TransactionId = row.TransactionId,
        ProviderTransactionId = row.ProviderTransactionId, PaymentMethod = row.PaymentMethod,
        Amount = row.Amount, CurrencyCode = row.CurrencyCode, State = row.State,
        InitiatedAt = row.InitiatedAt, CompletedAt = row.CompletedAt,
        PayerBankName = row.PayerBankName, DepositSlipNumber = row.DepositSlipNumber,
        DepositDate = row.DepositDate, DepositSlipFileId = row.DepositSlipFileId,
        VerifiedAt = row.VerifiedAt, VerificationNote = row.VerificationNote,
        FailureReason = row.FailureReason, RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    private static string TransactionId(string prefix, long tenant, long invoice) =>
        prefix + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) +
        "-" + tenant + "-" + invoice + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

    private static TransactionScope SerializableScope() =>
        new(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable },
            TransactionScopeAsyncFlowOption.Enabled);

    private bool TryGetTrustedCallbackBaseUrl(out string origin)
    {
        origin = string.Empty;
        if (!Uri.TryCreate(_gatewaySettings.CallbackBaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return false;
        origin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }
    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0;
}
