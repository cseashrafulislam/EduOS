using EduOS.Core.Entities.Finance;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class FeeHeadRepository : GenericRepository<FeeHead>, IFeeHeadRepository
    {
        public FeeHeadRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<FeeHead>> GetActiveAsync(int tenantId)
        {
            return await _dbSet
                .Where(f => f.TenantId == tenantId && f.IsActive)
                .OrderBy(f => f.Name)
                .ToListAsync();
        }

        public async Task<List<FeeHead>> GetByTypeAsync(string type, int tenantId)
        {
            return await _dbSet
                .Where(f => f.Type == type && f.TenantId == tenantId && f.IsActive)
                .ToListAsync();
        }
    }
}
