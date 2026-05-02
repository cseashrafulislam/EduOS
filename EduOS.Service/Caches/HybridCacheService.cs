using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.Service.Caches
{
    public class HybridCacheService : ICacheService
    {
        private readonly ICacheService _primaryCache;
        private readonly ICacheService _fallbackCache;
        private readonly ILogger<HybridCacheService> _logger;

        public HybridCacheService(
            ICacheService primaryCache,
            ICacheService fallbackCache,
            ILogger<HybridCacheService> logger)
        {
            _primaryCache = primaryCache;
            _fallbackCache = fallbackCache;
            _logger = logger;
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            var result = await _primaryCache.GetAsync<T>(key);

            if (result == null)
            {
                _logger.LogDebug("Cache miss in primary for {Key}, checking fallback", key);
                result = await _fallbackCache.GetAsync<T>(key);

                // Backfill primary cache if found in fallback
                if (result != null)
                    await _primaryCache.SetAsync(key, result);
            }

            return result;
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            await Task.WhenAll(
                _primaryCache.SetAsync(key, value, expiry),
                _fallbackCache.SetAsync(key, value, expiry)
            );
        }

        public async Task RemoveAsync(string key)
        {
            await Task.WhenAll(
                _primaryCache.RemoveAsync(key),
                _fallbackCache.RemoveAsync(key)
            );
        }

        public async Task RemoveByPatternAsync(string pattern)
        {
            await Task.WhenAll(
                _primaryCache.RemoveByPatternAsync(pattern),
                _fallbackCache.RemoveByPatternAsync(pattern)
            );
        }

        public async Task<bool> ExistsAsync(string key)
        {
            return await _primaryCache.ExistsAsync(key) || await _fallbackCache.ExistsAsync(key);
        }

        public async Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null)
        {
            var result = await GetAsync<T>(key);

            if (result != null)
                return result;

            result = await factory();

            if (result != null)
                await SetAsync(key, result, expiry);

            return result;
        }
    }
}
