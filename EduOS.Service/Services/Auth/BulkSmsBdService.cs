using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduOS.Service.Services.Auth
{
    public interface ISmsService
    {
        Task<SmsResult> SendAsync(string phone, string message);
        Task<List<SmsResult>> SendBulkAsync(List<string> phones, string message);
        Task<bool> ValidatePhoneNumberAsync(string phone);
    }

    public class SmsSettings
    {
        public string ApiUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
        public string SenderId { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 30;
        public int MaxRetries { get; set; } = 3;
        public bool EnableLogging { get; set; } = true;
        public string CountryCode { get; set; } = "88"; // Bangladesh
    }

    public class SmsResult
    {
        public bool Success { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public string? ProviderMessageId { get; set; }
        public DateTime SentAt { get; set; }
        public int Attempts { get; set; }
    }

    public class BulkSmsBdService : ISmsService
    {
        private readonly SmsSettings _settings;
        private readonly HttpClient _httpClient;
        private readonly ILogger<BulkSmsBdService> _logger;
        private readonly SemaphoreSlim _rateLimiter;

        public BulkSmsBdService(
            IOptions<SmsSettings> settings,
            IHttpClientFactory httpClientFactory,
            ILogger<BulkSmsBdService> logger)
        {
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _httpClient = httpClientFactory.CreateClient("SmsClient");
            _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            // Rate limiting: 10 concurrent requests max
            _rateLimiter = new SemaphoreSlim(10);
        }

        public async Task<SmsResult> SendAsync(string phone, string message)
        {
            // Validate input
            if (!await ValidatePhoneNumberAsync(phone))
            {
                return new SmsResult
                {
                    Success = false,
                    PhoneNumber = phone,
                    Message = message,
                    ErrorMessage = "Invalid phone number format",
                    SentAt = DateTime.UtcNow
                };
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return new SmsResult
                {
                    Success = false,
                    PhoneNumber = phone,
                    Message = message,
                    ErrorMessage = "Message cannot be empty",
                    SentAt = DateTime.UtcNow
                };
            }

            // Apply rate limiting
            await _rateLimiter.WaitAsync();
            try
            {
                return await SendWithRetryAsync(phone, message);
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        private async Task<SmsResult> SendWithRetryAsync(string phone, string message, int attempt = 1)
        {
            var result = new SmsResult
            {
                PhoneNumber = phone,
                Message = message,
                SentAt = DateTime.UtcNow,
                Attempts = attempt
            };

            try
            {
                // Method 1: GET request (your original approach)
                var getUrl = $"{_settings.ApiUrl}?api_key={_settings.ApiKey}" +
                           $"&type=text&number={phone}&senderid={_settings.SenderId}" +
                           $"&message={Uri.EscapeDataString(message)}";

                if (_settings.EnableLogging)
                {
                    _logger.LogDebug("Sending SMS to {Phone} (Attempt {Attempt})", phone, attempt);
                }

                var response = await _httpClient.GetAsync(getUrl).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    result.Success = true;
                    result.ProviderMessageId = ExtractMessageId(content);

                    if (_settings.EnableLogging)
                    {
                        _logger.LogInformation("SMS sent successfully to {Phone}. Provider ID: {ProviderId}",
                            phone, result.ProviderMessageId);
                    }

                    return result;
                }

                var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                result.ErrorMessage = $"HTTP {response.StatusCode}: {errorContent}";

                // Retry logic for recoverable errors
                if (attempt < _settings.MaxRetries && IsRetryableError(response.StatusCode))
                {
                    await Task.Delay(1000 * attempt); // Exponential backoff
                    return await SendWithRetryAsync(phone, message, attempt + 1);
                }
            }
            catch (TaskCanceledException)
            {
                result.ErrorMessage = "Request timeout";
            }
            catch (HttpRequestException ex)
            {
                result.ErrorMessage = $"Network error: {ex.Message}";

                if (attempt < _settings.MaxRetries)
                {
                    await Task.Delay(1000 * attempt);
                    return await SendWithRetryAsync(phone, message, attempt + 1);
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Unexpected error: {ex.Message}";
                _logger.LogError(ex, "Unexpected error sending SMS to {Phone}", phone);
            }

            result.Success = false;

            if (_settings.EnableLogging)
            {
                _logger.LogWarning("Failed to send SMS to {Phone} after {Attempts} attempts. Error: {Error}",
                    phone, attempt, result.ErrorMessage);
            }

            return result;
        }

        public async Task<List<SmsResult>> SendBulkAsync(List<string> phones, string message)
        {
            if (phones == null || !phones.Any())
            {
                throw new ArgumentException("Phone list cannot be null or empty", nameof(phones));
            }

            var results = new List<SmsResult>();

            // Method 1: Parallel sending with controlled concurrency
            var batches = phones.Chunk(50); // Send in batches of 50

            foreach (var batch in batches)
            {
                var batchTasks = batch.Select(phone => SendAsync(phone, message)).ToList();
                var batchResults = await Task.WhenAll(batchTasks);
                results.AddRange(batchResults);

                // Add delay between batches to avoid rate limiting
                if (batches.Any() && batch != batches.Last())
                {
                    await Task.Delay(1000);
                }
            }

            // Log summary
            var successCount = results.Count(r => r.Success);
            _logger.LogInformation("Bulk SMS completed: {SuccessCount}/{TotalCount} successful",
                successCount, results.Count);

            return results;
        }

        // Alternative POST-based method (recommended for production)
        public async Task<SmsResult> SendPostAsync(string phone, string message)
        {
            var request = new
            {
                api_key = _settings.ApiKey,
                api_secret = _settings.ApiSecret,
                type = "text",
                number = phone,
                senderid = _settings.SenderId,
                message = message
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(_settings.ApiUrl, request).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                return new SmsResult
                {
                    Success = response.IsSuccessStatusCode,
                    PhoneNumber = phone,
                    Message = message,
                    ProviderMessageId = ExtractMessageId(content),
                    ErrorMessage = response.IsSuccessStatusCode ? null : content,
                    SentAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new SmsResult
                {
                    Success = false,
                    PhoneNumber = phone,
                    Message = message,
                    ErrorMessage = ex.Message,
                    SentAt = DateTime.UtcNow
                };
            }
        }

        public Task<bool> ValidatePhoneNumberAsync(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return Task.FromResult(false);

            // Remove any non-digit characters
            var cleanPhone = new string(phone.Where(char.IsDigit).ToArray());

            // Check length (Bangladesh numbers are 11 digits: 01XXXXXXXXX)
            if (cleanPhone.Length != 11 && cleanPhone.Length != 13)
                return Task.FromResult(false);

            // Check prefix (Bangladesh: 01, International: 8801)
            if (cleanPhone.StartsWith("01"))
            {
                // Valid prefixes in Bangladesh: 013, 014, 015, 016, 017, 018, 019
                var validPrefixes = new[] { "013", "014", "015", "016", "017", "018", "019" };
                var prefix = cleanPhone.Substring(0, 3);
                return Task.FromResult(validPrefixes.Contains(prefix));
            }

            if (cleanPhone.StartsWith("8801"))
            {
                var prefix = cleanPhone.Substring(3, 2);
                var validPrefixes = new[] { "13", "14", "15", "16", "17", "18", "19" };
                return Task.FromResult(validPrefixes.Contains(prefix));
            }

            return Task.FromResult(false);
        }

        private static bool IsRetryableError(System.Net.HttpStatusCode statusCode)
        {
            return statusCode == System.Net.HttpStatusCode.RequestTimeout ||
                   statusCode == System.Net.HttpStatusCode.TooManyRequests ||
                   statusCode == System.Net.HttpStatusCode.InternalServerError ||
                   statusCode == System.Net.HttpStatusCode.BadGateway ||
                   statusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                   statusCode == System.Net.HttpStatusCode.GatewayTimeout;
        }

        private static string? ExtractMessageId(string responseContent)
        {
            try
            {
                // Try to parse JSON response
                using var doc = JsonDocument.Parse(responseContent);
                if (doc.RootElement.TryGetProperty("message_id", out var id))
                    return id.GetString();
                if (doc.RootElement.TryGetProperty("id", out var id2))
                    return id2.GetString();
            }
            catch
            {
                // Not JSON or parsing failed
            }

            // Fallback: return first 50 chars as reference
            return responseContent.Length > 50 ? responseContent[..50] : responseContent;
        }
    }

    // Extension methods for chunking
    public static class EnumerableExtensions
    {
        public static IEnumerable<IEnumerable<T>> Chunk<T>(this IEnumerable<T> source, int chunkSize)
        {
            if (chunkSize <= 0)
                throw new ArgumentException("Chunk size must be positive", nameof(chunkSize));

            var chunk = new List<T>(chunkSize);

            foreach (var item in source)
            {
                chunk.Add(item);
                if (chunk.Count == chunkSize)
                {
                    yield return chunk;
                    chunk = new List<T>(chunkSize);
                }
            }

            if (chunk.Any())
                yield return chunk;
        }
    }
}