using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class FeeStructureRepository : GenericRepository<FeeStructure>, IFeeStructureRepository
{
    public FeeStructureRepository(EduOSDbContext context) : base(context) { }

    public Task<List<FeeStructure>> GetByClassAsync(long classId, long academicYearId) =>
        _dbSet.AsNoTracking().Where(x => x.AcademicLevelId == classId && x.AcademicYearId == academicYearId && x.IsActive)
            .OrderByDescending(x => x.EffectiveFrom).ToListAsync();

    public async Task<decimal> GetTotalMonthlyFeeAsync(long classId, long academicYearId)
    {
        var structureIds = _dbSet.Where(x => x.AcademicLevelId == classId
                && x.AcademicYearId == academicYearId && x.IsActive)
            .Select(x => x.Id);
        return await _context.Set<FeeStructureLine>()
            .Where(x => structureIds.Contains(x.FeeStructureId)
                && x.Frequency == FeeFrequencyType.Monthly && x.IsMandatory)
            .SumAsync(x => x.Amount);
    }
}
