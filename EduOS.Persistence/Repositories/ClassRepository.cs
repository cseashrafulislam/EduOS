using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class ClassRepository : GenericRepository<Class>, IClassRepository
    {
        public ClassRepository(EduOSDbContext context) : base(context) { }

        public async Task<bool> IsClassNameExistsAsync(string name, int tenantId, int? excludeId = null)
        {
            var query = _dbSet.Where(c => 
                c.Name.ToLower() == name.ToLower() && c.TenantId == tenantId);
            if (excludeId.HasValue)
                query = query.Where(c => c.Id != excludeId.Value);
            return await query.AnyAsync();
        }

        public async Task<List<Class>> GetActiveClassesAsync(int tenantId)
        {
            return await _dbSet
                .Where(c => c.TenantId == tenantId && c.IsActive)
                .OrderBy(c => c.NumericValue)
                .ToListAsync();
        }

        public async Task<Class?> GetWithSectionsAsync(int id)
        {
            return await _dbSet
                .Include(c => c.Sections)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<Class?> GetWithSubjectsAsync(int id)
        {
            return await _dbSet
                .Include(c => c.Subjects)
                .FirstOrDefaultAsync(c => c.Id == id);
        }
    }
}
