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
            return await _dbSet
                .FirstOrDefaultAsync(t => t.Subdomain.ToLower() == subdomain.ToLower());
        }

        public async Task<Tenant?> GetByCodeAsync(string code)
        {
            return await _dbSet
                .FirstOrDefaultAsync(t => t.Code.ToLower() == code.ToLower());
        }

        public async Task<bool> IsSubdomainExistsAsync(string subdomain, int? excludeId = null)
        {
            var query = _dbSet.Where(t => t.Subdomain.ToLower() == subdomain.ToLower());
            if (excludeId.HasValue)
                query = query.Where(t => t.Id != excludeId.Value);
            return await query.AnyAsync();
        }

        public async Task<bool> IsCodeExistsAsync(string code, int? excludeId = null)
        {
            var query = _dbSet.Where(t => t.Code.ToLower() == code.ToLower());
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
