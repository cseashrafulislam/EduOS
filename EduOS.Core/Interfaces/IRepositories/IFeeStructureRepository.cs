using EduOS.Core.Entities.Finance;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IFeeStructureRepository : IGenericRepository<FeeStructure>
    {
        Task<List<FeeStructure>> GetByClassAsync(long classId, long academicYearId);
        Task<decimal> GetTotalMonthlyFeeAsync(long classId, long academicYearId);
    }
}
