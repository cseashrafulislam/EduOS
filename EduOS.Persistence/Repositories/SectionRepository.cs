using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class SectionRepository : GenericRepository<AcademicBatch>, ISectionRepository
{
    public SectionRepository(EduOSDbContext context) : base(context) { }

    public Task<List<AcademicBatch>> GetByClassIdAsync(long classId) =>
        _dbSet.AsNoTracking().Where(x => x.AcademicLevelId == classId && x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync();

    public async Task<bool> IsSectionNameExistsAsync(string name, long classId, long? excludeId = null)
    {
        var normalized = name.Trim();
        var query = _dbSet.Where(x => x.AcademicLevelId == classId && x.Name == normalized);
        if (excludeId.HasValue) query = query.Where(x => x.Id != excludeId.Value);
        return await query.AnyAsync();
    }

    public Task<int> GetTotalCapacityAsync(long classId) =>
        _dbSet.Where(x => x.AcademicLevelId == classId && x.IsActive).SumAsync(x => x.Capacity);
}
