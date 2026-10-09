using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class AcademicBatchRepository : GenericRepository<AcademicBatch>, IAcademicBatchRepository
{
    public AcademicBatchRepository(EduOSDbContext context) : base(context) { }

    public Task<List<AcademicBatch>> GetByAcademicLevelIdAsync(long academicLevelId) =>
        _dbSet.AsNoTracking().Where(x => x.AcademicLevelId == academicLevelId && x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync();

    public async Task<bool> IsBatchNameExistsAsync(string name, long academicLevelId, long? excludeId = null)
    {
        var normalized = name.Trim();
        var query = _dbSet.Where(x => x.AcademicLevelId == academicLevelId && x.Name == normalized);
        if (excludeId.HasValue) query = query.Where(x => x.Id != excludeId.Value);
        return await query.AnyAsync();
    }

    public Task<int> GetTotalCapacityAsync(long academicLevelId) =>
        _dbSet.Where(x => x.AcademicLevelId == academicLevelId && x.IsActive).SumAsync(x => x.Capacity);
}
