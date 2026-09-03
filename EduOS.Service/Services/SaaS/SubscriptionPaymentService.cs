using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Settings;
using EduOS.Service.Helpers.Payment;
using EduOS.Service.Helpers.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Cryptography;

namespace EduOS.Service.Services.SaaS
{
    public class SubscriptionPaymentService : ISubscriptionPaymentService
    {
        private readonly ISubscriptionPaymentRepository _paymentRepo;
        private readonly ISubscriptionInvoiceRepository _invoiceRepo;
        private readonly ITenantSubscriptionRepository _subscriptionRepo;
        private readonly IGenericRepository<EduOS.Core.Entities.Tenants.Tenant> _tenantRepo;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly IAamarPayClient _aamarPay;
        private readonly IFileUploadService _fileStorage;
        private readonly ISubscriptionService _subscriptionService;
        private readonly FileUploadSettings _fileSettings;
        private readonly ILogger<SubscriptionPaymentService> _logger;

        public SubscriptionPaymentService(
            ISubscriptionPaymentRepository paymentRepo,
            ISubscriptionInvoiceRepository invoiceRepo,
            ITenantSubscriptionRepository subscriptionRepo,
            IGenericRepository<EduOS.Core.Entities.Tenants.Tenant> tenantRepo,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            IAamarPayClient aamarPay,
            IFileUploadService fileStorage,
            ISubscriptionService subscriptionService,
            IOptions<FileUploadSettings> fileSettings,
            ILogger<SubscriptionPaymentService> logger)
        {
            _paymentRepo = paymentRepo;
            _invoiceRepo = invoiceRepo;
            _subscriptionRepo = subscriptionRepo;
            _tenantRepo = tenantRepo;
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _aamarPay = aamarPay;
            _fileStorage = fileStorage;
            _subscriptionService = subscriptionService;
            _fileSettings = fileSettings.Value;
            _logger = logger;
        }

        // ============================================================
        // INITIATE AAMARPAY ONLINE PAYMENT
        // ============================================================
        public async Task<ApiResponse<InitiatePaymentResponseDto>> InitiateAamarPayAsync(
            InitiatePaymentRequestDto dto, string baseUrl)
        {
            var tenantId = _currentUser.TenantId;
            try
            {
                // 1. Load invoice
                var invoice = await _invoiceRepo.GetByIdAsync(dto.InvoiceId);
                if (invoice == null || invoice.TenantId != tenantId)
                    return ApiResponse<InitiatePaymentResponseDto>.ErrorResponse("Invoice not found", 404);

                if (invoice.PaymentStatus == PaymentStatus.Successful)
                    return ApiResponse<InitiatePaymentResponseDto>.ErrorResponse("Invoice already paid", 400);

                if (invoice.DueAmount <= 0)
                    return ApiResponse<InitiatePaymentResponseDto>.ErrorResponse("Nothing due on this invoice", 400);

                // 2. Generate our internal transaction ID
                var transactionId = CreateTransactionId("EDU", tenantId, invoice.Id);

                // 3. Create payment record
                var payment = new SubscriptionPayment
                {
                    TenantId = tenantId,
                    SubscriptionInvoiceId = invoice.Id,
                    TransactionId = transactionId,
                    PaymentMethod = PaymentMethod.AamarPay,
                    Amount = invoice.DueAmount,
                    Currency = invoice.Currency,
                    Status = PaymentStatus.Processing,
                    InitiatedAt = DateTime.UtcNow
                };

                await _paymentRepo.AddAsync(payment);
                await _unitOfWork.SaveChangesAsync();

                // 4. Build AamarPay request
                var tenant = await _tenantRepo.GetByIdAsync(tenantId);

                var apRequest = new AamarPayRequest
                {
                    TransactionId = transactionId,
                    Amount = invoice.DueAmount,
                    Description = invoice.Description ?? $"Invoice {invoice.InvoiceNumber}",
                    CustomerName = invoice.CustomerName,
                    CustomerEmail = invoice.CustomerEmail ?? tenant?.Email ?? "noreply@eduos.com",
                    CustomerPhone = invoice.CustomerPhone ?? tenant?.Phone ?? "01700000000",
                    CustomerAddress = invoice.CustomerAddress,
                    CustomerCity = tenant?.City,
                    CustomerCountry = tenant?.Country,
                    SuccessUrl = $"{baseUrl}/api/subscription-payment/callback/success",
                    FailUrl = $"{baseUrl}/api/subscription-payment/callback/fail",
                    CancelUrl = $"{baseUrl}/api/subscription-payment/callback/cancel"
                };

                // 5. Call AamarPay
                var apResult = await _aamarPay.InitiatePaymentAsync(apRequest);

                if (!apResult.IsSuccess)
                {
                    payment.Status = PaymentStatus.Failed;
                    payment.FailureReason = apResult.ErrorMessage;
                    payment.FailedAt = DateTime.UtcNow;
                    payment.GatewayResponse = apResult.RawResponse;
                    _paymentRepo.Update(payment);
                    await _unitOfWork.SaveChangesAsync();

                    return ApiResponse<InitiatePaymentResponseDto>.ErrorResponse(
                        apResult.ErrorMessage ?? "Payment gateway error", 500);
                }

                payment.GatewayResponse = apResult.RawResponse;
                _paymentRepo.Update(payment);
                await _unitOfWork.SaveChangesAsync();

                return ApiResponse<InitiatePaymentResponseDto>.SuccessResponse(
                    new InitiatePaymentResponseDto
                    {
                        TransactionId = transactionId,
                        PaymentUrl = apResult.PaymentUrl,
                        Status = PaymentStatus.Processing,
                        Message = "Redirect user to PaymentUrl to complete payment"
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initiate AamarPay payment");
                return ApiResponse<InitiatePaymentResponseDto>.ErrorResponse("Payment initiation failed", 500);
            }
        }

        // ============================================================
        // HANDLE AAMARPAY CALLBACK
        // ============================================================
        public async Task<ApiResponse<bool>> HandleAamarPayCallbackAsync(AamarPayCallbackDto callback)
        {
            try
            {
                if (string.IsNullOrEmpty(callback.MerTxnid))
                    return ApiResponse<bool>.ErrorResponse("Missing transaction ID", 400);

                var payment = await _paymentRepo.GetByTransactionIdForCallbackAsync(callback.MerTxnid);
                if (payment == null)
                {
                    _logger.LogWarning("Callback for unknown txn {TxnId}", callback.MerTxnid);
                    return ApiResponse<bool>.ErrorResponse("Transaction not found", 404);
                }

                // Already processed - idempotent
                if (payment.Status == PaymentStatus.Successful)
                    return ApiResponse<bool>.SuccessResponse(true, "Already processed");

                await _unitOfWork.BeginTransactionAsync();

                payment.GatewayTransactionId = callback.PgTxnid;
                payment.GatewayReference = callback.BankTxnid;

                var isSuccess = string.Equals(callback.PayStatus, "Successful", StringComparison.OrdinalIgnoreCase);

                if (isSuccess)
                {
                    // Verify with AamarPay before trusting the callback
                    var verify = await _aamarPay.VerifyTransactionAsync(callback.MerTxnid);
                    var amountMatches = decimal.TryParse(
                        verify.Amount,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var verifiedAmount)
                        && verifiedAmount == payment.Amount;
                    var currencyMatches = string.IsNullOrWhiteSpace(verify.Currency)
                        || string.Equals(
                            verify.Currency,
                            payment.Currency,
                            StringComparison.OrdinalIgnoreCase);

                    if (!verify.IsSuccess || !amountMatches || !currencyMatches)
                    {
                        payment.Status = PaymentStatus.Failed;
                        payment.FailureReason = "Gateway verification failed or payment details did not match.";
                        payment.FailedAt = DateTime.UtcNow;
                        payment.GatewayResponse = verify.RawResponse;
                        _paymentRepo.Update(payment);
                        await _unitOfWork.SaveChangesAsync();
                        await _unitOfWork.CommitTransactionAsync();
                        return ApiResponse<bool>.ErrorResponse("Payment verification failed", 400);
                    }

                    payment.Status = PaymentStatus.Successful;
                    payment.CompletedAt = DateTime.UtcNow;

                    // Update invoice
                    var invoice = await _invoiceRepo.GetByIdForSystemAsync(
                        payment.SubscriptionInvoiceId, payment.TenantId);
                    if (invoice != null)
                    {
                        invoice.PaidAmount += payment.Amount;
                        invoice.DueAmount = invoice.TotalAmount - invoice.PaidAmount;
                        if (invoice.DueAmount <= 0)
                        {
                            invoice.PaymentStatus = PaymentStatus.Successful;
                            invoice.PaidAt = DateTime.UtcNow;
                        }
                        _invoiceRepo.Update(invoice);

                        // Activate subscription if invoice is fully paid
                        if (invoice.PaymentStatus == PaymentStatus.Successful)
                        {
                            await _subscriptionService.ActivateAfterPaymentAsync(
                                invoice.TenantSubscriptionId, payment.TenantId);
                        }
                    }
                }
                else
                {
                    payment.Status = PaymentStatus.Failed;
                    payment.FailureReason = callback.PayStatus ?? "Unknown failure";
                    payment.FailedAt = DateTime.UtcNow;
                }

                _paymentRepo.Update(payment);
                await _unitOfWork.SaveChangesAsync();
                await _unitOfWork.CommitTransactionAsync();

                _logger.LogInformation("AamarPay callback processed for {TxnId}, status={Status}",
                    callback.MerTxnid, payment.Status);

                return ApiResponse<bool>.SuccessResponse(true);
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync();
                _logger.LogError(ex, "Failed to process AamarPay callback");
                return ApiResponse<bool>.ErrorResponse("Callback processing failed", 500);
            }
        }

        // ============================================================
        // SUBMIT MANUAL PAYMENT
        // ============================================================
        public async Task<ApiResponse<SubscriptionPaymentDto>> SubmitManualPaymentAsync(
            ManualPaymentSubmitDto dto, IFormFile? depositSlip)
        {
            var tenantId = _currentUser.TenantId;
            try
            {
                var invoice = await _invoiceRepo.GetByIdAsync(dto.InvoiceId);
                if (invoice == null || invoice.TenantId != tenantId)
                    return ApiResponse<SubscriptionPaymentDto>.ErrorResponse("Invoice not found", 404);

                if (invoice.PaymentStatus == PaymentStatus.Successful)
                    return ApiResponse<SubscriptionPaymentDto>.ErrorResponse("Invoice already paid", 400);

                if (dto.Amount <= 0)
                    return ApiResponse<SubscriptionPaymentDto>.ErrorResponse("Invalid amount", 400);

                // Upload deposit slip if provided
                string? slipUrl = null;
                if (depositSlip != null && depositSlip.Length > 0)
                {
                    if (!_fileStorage.ValidateFile(depositSlip))
                    {
                        return ApiResponse<SubscriptionPaymentDto>.ErrorResponse(
                            "Invalid file type. Allowed: PDF, JPG, JPEG, PNG", 400);
                    }

                    var upload = await _fileStorage.UploadAsync(depositSlip, "deposit-slips");
                    if (!upload.Success)
                        return ApiResponse<SubscriptionPaymentDto>.ErrorResponse(
                            upload.ErrorMessage ?? "File upload failed", 400);

                    slipUrl = upload.FileUrl;
                }

                var transactionId = CreateTransactionId("MAN", tenantId, invoice.Id);

                var payment = new SubscriptionPayment
                {
                    TenantId = tenantId,
                    SubscriptionInvoiceId = invoice.Id,
                    TransactionId = transactionId,
                    PaymentMethod = PaymentMethod.ManualBankTransfer,
                    Amount = dto.Amount,
                    Currency = invoice.Currency,
                    Status = PaymentStatus.AwaitingVerification,
                    InitiatedAt = DateTime.UtcNow,
                    PayerBankName = dto.PayerBankName,
                    PayerAccountNumber = dto.PayerAccountNumber,
                    DepositSlipNumber = dto.DepositSlipNumber,
                    DepositDate = dto.DepositDate,
                    DepositSlipUrl = slipUrl,
                    VerificationNote = dto.Note
                };

                await _paymentRepo.AddAsync(payment);

                // Mark invoice as awaiting verification
                invoice.PaymentStatus = PaymentStatus.AwaitingVerification;
                _invoiceRepo.Update(invoice);

                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("Manual payment submitted for invoice {InvoiceId}, txn {TxnId}",
                    invoice.Id, transactionId);

                var resultDto = MapToDto(payment, invoice.InvoiceNumber);
                return ApiResponse<SubscriptionPaymentDto>.SuccessResponse(resultDto,
                    "Payment submitted. Awaiting admin verification.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to submit manual payment");
                return ApiResponse<SubscriptionPaymentDto>.ErrorResponse("Submission failed", 500);
            }
        }

        // ============================================================
        // VERIFY MANUAL PAYMENT (SuperAdmin only)
        // ============================================================
        public async Task<ApiResponse<bool>> VerifyManualPaymentAsync(VerifyManualPaymentDto dto)
        {
            try
            {
                var payment = await _paymentRepo.GetByIdForPlatformAsync(dto.PaymentId);
                if (payment == null)
                    return ApiResponse<bool>.ErrorResponse("Payment not found", 404);

                if (payment.Status != PaymentStatus.AwaitingVerification)
                    return ApiResponse<bool>.ErrorResponse("Payment is not awaiting verification", 400);

                await _unitOfWork.BeginTransactionAsync();

                payment.VerifiedByUserId = _currentUser.UserId;
                payment.VerifiedAt = DateTime.UtcNow;
                payment.VerificationNote = dto.VerificationNote;

                var invoice = await _invoiceRepo.GetByIdForSystemAsync(
                    payment.SubscriptionInvoiceId, payment.TenantId);

                if (dto.Approve)
                {
                    payment.Status = PaymentStatus.Successful;
                    payment.CompletedAt = DateTime.UtcNow;

                    if (invoice != null)
                    {
                        invoice.PaidAmount += payment.Amount;
                        invoice.DueAmount = invoice.TotalAmount - invoice.PaidAmount;
                        if (invoice.DueAmount <= 0)
                        {
                            invoice.PaymentStatus = PaymentStatus.Successful;
                            invoice.PaidAt = DateTime.UtcNow;
                        }
                        else
                        {
                            invoice.PaymentStatus = PaymentStatus.Pending;
                        }
                        _invoiceRepo.Update(invoice);

                        if (invoice.PaymentStatus == PaymentStatus.Successful)
                        {
                            await _subscriptionService.ActivateAfterPaymentAsync(
                                invoice.TenantSubscriptionId, payment.TenantId);
                        }
                    }
                }
                else
                {
                    payment.Status = PaymentStatus.Failed;
                    payment.FailedAt = DateTime.UtcNow;
                    payment.FailureReason = dto.VerificationNote ?? "Rejected by admin";

                    // Revert invoice status to Pending if no other successful payment
                    if (invoice != null && invoice.PaymentStatus == PaymentStatus.AwaitingVerification)
                    {
                        invoice.PaymentStatus = PaymentStatus.Pending;
                        _invoiceRepo.Update(invoice);
                    }
                }

                _paymentRepo.Update(payment);
                await _unitOfWork.SaveChangesAsync();
                await _unitOfWork.CommitTransactionAsync();

                _logger.LogInformation("Manual payment {Id} {Action} by user {UserId}",
                    payment.Id, dto.Approve ? "approved" : "rejected", _currentUser.UserId);

                return ApiResponse<bool>.SuccessResponse(true,
                    dto.Approve ? "Payment approved" : "Payment rejected");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync();
                _logger.LogError(ex, "Failed to verify manual payment {Id}", dto.PaymentId);
                return ApiResponse<bool>.ErrorResponse("Verification failed", 500);
            }
        }

        // ============================================================
        // GET PAYMENTS BY INVOICE
        // ============================================================
        public async Task<ApiResponse<List<SubscriptionPaymentDto>>> GetByInvoiceAsync(long invoiceId)
        {
            var tenantId = _currentUser.TenantId;
            try
            {
                var invoice = _currentUser.IsSuperAdmin
                    ? await _invoiceRepo.GetByIdForPlatformAsync(invoiceId)
                    : await _invoiceRepo.GetByIdAsync(invoiceId);
                if (invoice == null || (invoice.TenantId != tenantId && !_currentUser.IsSuperAdmin))
                    return ApiResponse<List<SubscriptionPaymentDto>>.ErrorResponse("Invoice not found", 404);

                var payments = _currentUser.IsSuperAdmin
                    ? await _paymentRepo.GetByInvoiceForPlatformAsync(invoiceId, invoice.TenantId)
                    : await _paymentRepo.GetByInvoiceAsync(invoiceId);
                var dtos = payments.Select(p => MapToDto(p, invoice.InvoiceNumber)).ToList();

                return ApiResponse<List<SubscriptionPaymentDto>>.SuccessResponse(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load payments for invoice {Id}", invoiceId);
                return ApiResponse<List<SubscriptionPaymentDto>>.ErrorResponse("Failed to load payments", 500);
            }
        }

        // ============================================================
        // PENDING MANUAL VERIFICATIONS (SuperAdmin)
        // ============================================================
        public async Task<ApiResponse<List<SubscriptionPaymentDto>>> GetPendingManualVerificationsAsync()
        {
            try
            {
                if (!_currentUser.IsSuperAdmin)
                    return ApiResponse<List<SubscriptionPaymentDto>>.ErrorResponse("Forbidden", 403);

                var payments = await _paymentRepo.GetPendingManualVerificationForPlatformAsync();

                // Load invoice numbers
                var invoiceIds = payments.Select(p => p.SubscriptionInvoiceId).Distinct().ToList();
                var invoices = new Dictionary<long, string>();
                foreach (var id in invoiceIds)
                {
                    var payment = payments.First(p => p.SubscriptionInvoiceId == id);
                    var inv = await _invoiceRepo.GetByIdForSystemAsync(id, payment.TenantId);
                    if (inv != null) invoices[id] = inv.InvoiceNumber;
                }

                var dtos = payments.Select(p => MapToDto(p,
                    invoices.GetValueOrDefault(p.SubscriptionInvoiceId, ""))).ToList();

                return ApiResponse<List<SubscriptionPaymentDto>>.SuccessResponse(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load pending verifications");
                return ApiResponse<List<SubscriptionPaymentDto>>.ErrorResponse("Failed", 500);
            }
        }

        // ============================================================
        // MAPPING HELPER
        // ============================================================
        private static SubscriptionPaymentDto MapToDto(SubscriptionPayment p, string invoiceNumber)
        {
            return new SubscriptionPaymentDto
            {
                Id = p.Id,
                InvoiceId = p.SubscriptionInvoiceId,
                InvoiceNumber = invoiceNumber,
                TransactionId = p.TransactionId,
                GatewayTransactionId = p.GatewayTransactionId,
                PaymentMethod = p.PaymentMethod,
                Amount = p.Amount,
                Currency = p.Currency,
                Status = p.Status,
                InitiatedAt = p.InitiatedAt,
                CompletedAt = p.CompletedAt,
                PayerBankName = p.PayerBankName,
                PayerAccountNumber = p.PayerAccountNumber,
                DepositSlipNumber = p.DepositSlipNumber,
                DepositDate = p.DepositDate,
                DepositSlipUrl = p.DepositSlipUrl,
                VerificationNote = p.VerificationNote,
                VerifiedAt = p.VerifiedAt,
                FailureReason = p.FailureReason
            };
        }

        private static string CreateTransactionId(string prefix, long tenantId, long invoiceId)
        {
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
            return $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{tenantId}-{invoiceId}-{nonce}";
        }
    }
}
