using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EduOS.Service.Services.Caches
{
    public class MemoryCacheService : ICacheService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<MemoryCacheService> _logger;

        public MemoryCacheService(IMemoryCache cache, ILogger<MemoryCacheService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public Task<T?> GetAsync<T>(string key)
        {
            _cache.TryGetValue(key, out T? value);
            return Task.FromResult(value);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            var options = new MemoryCacheEntryOptions();

            if (expiry.HasValue)
                options.AbsoluteExpirationRelativeToNow = expiry;
            else
                options.SetSlidingExpiration(TimeSpan.FromMinutes(15));

            _cache.Set(key, value, options);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key)
        {
            _cache.Remove(key);
            return Task.CompletedTask;
        }

        public async Task RemoveByPatternAsync(string pattern)
        {
            // MemoryCache doesn't support pattern removal directly
            // You'd need to maintain a key list or use a different approach
            _logger.LogWarning("Pattern removal not fully supported in MemoryCache");
            await Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key)
        {
            return Task.FromResult(_cache.TryGetValue(key, out _));
        }

        public async Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null)
        {
            return await _cache.GetOrCreateAsync(key, async entry =>
            {
                if (expiry.HasValue)
                    entry.AbsoluteExpirationRelativeToNow = expiry;
                else
                    entry.SetSlidingExpiration(TimeSpan.FromMinutes(15));

                return await factory();
            });
        }
    }


}