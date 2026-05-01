using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class DepartmentRepository : GenericRepository<Department>, IDepartmentRepository
    {
        public DepartmentRepository(EduOSDbContext context) : base(context) { }

        public async Task<bool> IsCodeExistsAsync(string code, int tenantId, int? excludeId = null)
        {
            var query = _dbSet.Where(d => 
                d.Code.ToLower() == code.ToLower() && d.TenantId == tenantId);
            if (excludeId.HasValue)
                query = query.Where(d => d.Id != excludeId.Value);
            return await query.AnyAsync();
        }
    }
}
