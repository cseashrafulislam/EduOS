using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class TenantRepository : GenericRepository<Tenant>, ITenantRepository
    {
        public TenantRepository(EduOSDbContext context) : base(context) { }

        public async Task<Tenant?> GetBySubdomainAsync(string subdomain)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(subdomain);
            var normalized = subdomain.Trim().ToLowerInvariant();
            return await _dbSet
                .FirstOrDefaultAsync(t => t.Subdomain != null && t.Subdomain.ToLower() == normalized);
        }

        public async Task<Tenant?> GetByCodeAsync(string code)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            var normalized = code.Trim().ToLowerInvariant();
            return await _dbSet
                .FirstOrDefaultAsync(t => t.Code != null && t.Code.ToLower() == normalized);
        }

        public async Task<bool> IsSubdomainExistsAsync(string subdomain, long? excludeId = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(subdomain);
            var normalized = subdomain.Trim().ToLowerInvariant();
            var query = _dbSet.Where(t => t.Subdomain != null && t.Subdomain.ToLower() == normalized);
            if (excludeId.HasValue)
                query = query.Where(t => t.Id != excludeId.Value);
            return await query.AnyAsync();
        }

        public async Task<bool> IsCodeExistsAsync(string code, long? excludeId = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            var normalized = code.Trim().ToLowerInvariant();
            var query = _dbSet.Where(t => t.Code != null && t.Code.ToLower() == normalized);
            if (excludeId.HasValue)
                query = query.Where(t => t.Id != excludeId.Value);
            return await query.AnyAsync();
        }

        public async Task<List<Tenant>> GetActiveTenantsAsync()
        {
            return await _dbSet
                .Where(t => t.IsActive)
                .OrderBy(t => t.Name)
                .ToListAsync();
        }
    }
}
