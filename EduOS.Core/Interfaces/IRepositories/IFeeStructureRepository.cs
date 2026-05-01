using EduOS.Core.Entities.Finance;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IFeeStructureRepository : IGenericRepository<FeeStructure>
    {
        Task<List<FeeStructure>> GetByClassAsync(int classId, int academicYearId);
        Task<decimal> GetTotalMonthlyFeeAsync(int classId, int academicYearId);
    }
}
