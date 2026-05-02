using EduOS.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace EduOS.Service.Helpers.Payment
{
    /// <summary>
    /// HTTP client wrapper for AamarPay gateway integration.
    /// Handles initiating transactions and verifying callbacks.
    /// </summary>
    public interface IAamarPayClient
    {
        Task<AamarPayInitiateResult> InitiatePaymentAsync(AamarPayRequest request, CancellationToken ct = default);
        Task<AamarPayVerifyResult> VerifyTransactionAsync(string transactionId, CancellationToken ct = default);
    }

    public class AamarPayClient : IAamarPayClient
    {
        private readonly HttpClient _httpClient;
        private readonly AamarPaySettings _settings;
        private readonly ILogger<AamarPayClient> _logger;

        public AamarPayClient(
            HttpClient httpClient,
            IOptions<AamarPaySettings> settings,
            ILogger<AamarPayClient> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<AamarPayInitiateResult> InitiatePaymentAsync(
            AamarPayRequest request,
            CancellationToken ct = default)
        {
            try
            {
                var payload = new
                {
                    store_id = _settings.StoreId,
                    signature_key = _settings.SignatureKey,
                    cus_name = request.CustomerName,
                    cus_email = request.CustomerEmail,
                    cus_phone = request.CustomerPhone,
                    cus_add1 = request.CustomerAddress ?? "N/A",
                    cus_city = request.CustomerCity ?? "Dhaka",
                    cus_country = request.CustomerCountry ?? "Bangladesh",
                    amount = request.Amount.ToString("0.00"),
                    tran_id = request.TransactionId,
                    currency = _settings.Currency,
                    desc = request.Description,
                    success_url = request.SuccessUrl,
                    fail_url = request.FailUrl,
                    cancel_url = request.CancelUrl,
                    type = "json"
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogInformation("Initiating AamarPay payment for txn {TxnId}", request.TransactionId);

                var response = await _httpClient.PostAsync(_settings.PaymentUrl, content, ct);
                var responseBody = await response.Content.ReadAsStringAsync(ct);

                _logger.LogDebug("AamarPay response: {Body}", responseBody);

                if (!response.IsSuccessStatusCode)
                {
                    return new AamarPayInitiateResult
                    {
                        IsSuccess = false,
                        ErrorMessage = $"Gateway returned {response.StatusCode}",
                        RawResponse = responseBody
                    };
                }

                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                // AamarPay returns: { "result": "true", "payment_url": "..." }
                if (root.TryGetProperty("result", out var resultProp))
                {
                    var result = resultProp.GetString();
                    if (string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        var paymentUrl = root.TryGetProperty("payment_url", out var urlProp)
                            ? urlProp.GetString()
                            : null;

                        return new AamarPayInitiateResult
                        {
                            IsSuccess = true,
                            PaymentUrl = paymentUrl,
                            RawResponse = responseBody
                        };
                    }
                }

                return new AamarPayInitiateResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Gateway rejected the request",
                    RawResponse = responseBody
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AamarPay initiate failed");
                return new AamarPayInitiateResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<AamarPayVerifyResult> VerifyTransactionAsync(
            string transactionId,
            CancellationToken ct = default)
        {
            try
            {
                var url = $"{_settings.VerifyUrl}?request_id={transactionId}" +
                          $"&store_id={_settings.StoreId}" +
                          $"&signature_key={_settings.SignatureKey}" +
                          $"&type=json";

                var response = await _httpClient.GetAsync(url, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                _logger.LogDebug("AamarPay verify response: {Body}", body);

                if (!response.IsSuccessStatusCode)
                {
                    return new AamarPayVerifyResult
                    {
                        IsSuccess = false,
                        ErrorMessage = $"Verify returned {response.StatusCode}",
                        RawResponse = body
                    };
                }

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                var payStatus = root.TryGetProperty("pay_status", out var psProp)
                    ? psProp.GetString()
                    : null;

                var amount = root.TryGetProperty("amount", out var amtProp)
                    ? amtProp.GetString()
                    : null;

                return new AamarPayVerifyResult
                {
                    IsSuccess = string.Equals(payStatus, "Successful", StringComparison.OrdinalIgnoreCase),
                    PayStatus = payStatus,
                    Amount = amount,
                    RawResponse = body
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AamarPay verify failed for {TxnId}", transactionId);
                return new AamarPayVerifyResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }

    // ==================== Request / Response Models ====================

    public class AamarPayRequest
    {
        public string TransactionId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;

        public string CustomerName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string? CustomerAddress { get; set; }
        public string? CustomerCity { get; set; }
        public string? CustomerCountry { get; set; }

        public string SuccessUrl { get; set; } = string.Empty;
        public string FailUrl { get; set; } = string.Empty;
        public string CancelUrl { get; set; } = string.Empty;
    }

    public class AamarPayInitiateResult
    {
        public bool IsSuccess { get; set; }
        public string? PaymentUrl { get; set; }
        public string? ErrorMessage { get; set; }
        public string? RawResponse { get; set; }
    }

    public class AamarPayVerifyResult
    {
        public bool IsSuccess { get; set; }
        public string? PayStatus { get; set; }
        public string? Amount { get; set; }
        public string? ErrorMessage { get; set; }
        public string? RawResponse { get; set; }
    }
}
