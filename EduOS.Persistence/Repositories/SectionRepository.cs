using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class SectionRepository : GenericRepository<Section>, ISectionRepository
    {
        public SectionRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<Section>> GetByClassIdAsync(int classId)
        {
            return await _dbSet
                .Where(s => s.ClassId == classId && s.IsActive)
                .OrderBy(s => s.Name)
                .ToListAsync();
        }

        public async Task<bool> IsSectionNameExistsAsync(string name, int classId, int? excludeId = null)
        {
            var query = _dbSet.Where(s => 
                s.Name.ToLower() == name.ToLower() && s.ClassId == classId);
            if (excludeId.HasValue)
                query = query.Where(s => s.Id != excludeId.Value);
            return await query.AnyAsync();
        }

        public async Task<int> GetTotalCapacityAsync(int classId)
        {
            return await _dbSet
                .Where(s => s.ClassId == classId && s.IsActive)
                .SumAsync(s => s.Capacity);
        }
    }
}
