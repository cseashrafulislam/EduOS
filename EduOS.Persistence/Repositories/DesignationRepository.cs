using EduOS.Core.Entities.Employees;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class DesignationRepository : GenericRepository<Designation>, IDesignationRepository
    {
        public DesignationRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<Designation>> GetActiveAsync(int tenantId)
        {
            return await _dbSet
                .Where(d => d.TenantId == tenantId && d.IsActive)
                .OrderBy(d => d.Rank)
                .ToListAsync();
        }
    }
}
