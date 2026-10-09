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

    public Task<List<FeeStructure>> GetApplicableAsync(long campusId, long academicProgramId, long academicLevelId, long academicYearId,
        long? academicBatchId, DateOnly effectiveOn, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().Where(x => x.CampusId == campusId && x.AcademicYearId == academicYearId
            && (x.AcademicProgramId == null || x.AcademicProgramId == academicProgramId)
            && (x.AcademicLevelId == null || x.AcademicLevelId == academicLevelId)
            && (x.AcademicBatchId == null || (academicBatchId.HasValue && x.AcademicBatchId == academicBatchId.Value))
            && x.IsActive && x.EffectiveFrom <= effectiveOn && (x.EffectiveTo == null || x.EffectiveTo >= effectiveOn))
            .OrderByDescending(x => x.AcademicBatchId != null).ThenByDescending(x => x.AcademicLevelId != null)
            .ThenByDescending(x => x.AcademicProgramId != null).ThenByDescending(x => x.EffectiveFrom)
            .ThenBy(x => x.Id).ToListAsync(cancellationToken);
}
