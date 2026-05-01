using EduOS.Core.Entities.Finance;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class FeeStructureRepository : GenericRepository<FeeStructure>, IFeeStructureRepository
    {
        public FeeStructureRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<FeeStructure>> GetByClassAsync(int classId, int academicYearId)
        {
            return await _dbSet
                .Include(f => f.FeeHead)
                .Where(f => f.ClassId == classId && f.AcademicYearId == academicYearId)
                .ToListAsync();
        }

        public async Task<decimal> GetTotalMonthlyFeeAsync(int classId, int academicYearId)
        {
            return await _dbSet
                .Include(f => f.FeeHead)
                .Where(f => f.ClassId == classId 
                    && f.AcademicYearId == academicYearId 
                    && f.FeeHead!.Type == "Monthly")
                .SumAsync(f => f.Amount);
        }
    }
}
