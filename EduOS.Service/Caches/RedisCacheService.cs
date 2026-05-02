using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace EduOS.Service.Caches
{
    public class RedisCacheService : ICacheService
    {
        private readonly IDistributedCache _cache;
        private readonly ILogger<RedisCacheService> _logger;
        private readonly IConnectionMultiplexer? _redisConnection;
        private readonly JsonSerializerOptions _jsonOptions;

        public RedisCacheService(
            IDistributedCache cache,
            ILogger<RedisCacheService> logger,
            IConnectionMultiplexer? redisConnection = null)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _redisConnection = redisConnection;

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            try
            {
                var json = await _cache.GetStringAsync(key);

                if (string.IsNullOrEmpty(json))
                {
                    _logger.LogDebug("Cache miss for key: {Key}", key);
                    return default;
                }

                _logger.LogDebug("Cache hit for key: {Key}", key);
                return JsonSerializer.Deserialize<T>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cache for key: {Key}", key);
                return default;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            if (value == null)
            {
                await RemoveAsync(key);
                return;
            }

            try
            {
                var options = new DistributedCacheEntryOptions();

                if (expiry.HasValue)
                {
                    options.AbsoluteExpirationRelativeToNow = expiry;
                }
                else
                {
                    // Default: 30 minutes absolute + 15 minutes sliding
                    options.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
                    options.SlidingExpiration = TimeSpan.FromMinutes(15);
                }

                var json = JsonSerializer.Serialize(value, _jsonOptions);
                await _cache.SetStringAsync(key, json, options);

                _logger.LogDebug("Cache set for key: {Key}, expiry: {Expiry}",
                    key, expiry ?? TimeSpan.FromMinutes(30));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting cache for key: {Key}", key);
            }
        }

        public async Task RemoveAsync(string key)
        {
            try
            {
                await _cache.RemoveAsync(key);
                _logger.LogDebug("Cache removed for key: {Key}", key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cache for key: {Key}", key);
            }
        }

        public async Task RemoveByPatternAsync(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return;

            try
            {
                // Method 1: If we have Redis connection, use SCAN/LUA for pattern matching
                if (_redisConnection != null)
                {
                    await RemoveByPatternUsingRedisAsync(pattern);
                }
                else
                {
                    // Method 2: Log warning - pattern removal requires Redis connection
                    _logger.LogWarning(
                        "Redis connection not available for pattern removal: {Pattern}. " +
                        "Consider using In-Memory cache or providing IConnectionMultiplexer.",
                        pattern);

                    // Option 3: For basic patterns, you'd need to implement key enumeration
                    // This is NOT recommended for production with many keys
                    await RemoveByPatternFallbackAsync(pattern);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cache by pattern: {Pattern}", pattern);
            }
        }

        private async Task RemoveByPatternUsingRedisAsync(string pattern)
        {
            var redisDb = _redisConnection!.GetDatabase();
            var server = _redisConnection.GetServer(_redisConnection.GetEndPoints().First());

            // Convert pattern to Redis format (replace * with *)
            var redisPattern = pattern.Replace("*", "*");

            var keys = server.Keys(pattern: redisPattern).ToList();

            if (keys.Any())
            {
                var batch = redisDb.CreateBatch();
                foreach (var key in keys)
                {
                    batch.KeyDeleteAsync(key);
                }
                batch.Execute();

                _logger.LogInformation("Removed {Count} cache keys matching pattern: {Pattern}",
                    keys.Count, pattern);
            }
        }

        private async Task RemoveByPatternFallbackAsync(string pattern)
        {
            // This is a simplified fallback - in production, you'd need a way to list keys
            // Consider using a naming convention and storing key indexes
            _logger.LogWarning("Pattern removal fallback used for: {Pattern}. Keys may not be fully removed.", pattern);
            await Task.CompletedTask;
        }

        public async Task<bool> ExistsAsync(string key)
        {
            try
            {
                var value = await _cache.GetStringAsync(key);
                return !string.IsNullOrEmpty(value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking existence for key: {Key}", key);
                return false;
            }
        }

        public async Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null)
        {
            var cached = await GetAsync<T>(key);

            if (cached != null)
                return cached;

            var result = await factory();

            if (result != null)
                await SetAsync(key, result, expiry);

            return result;
        }
    }
}
