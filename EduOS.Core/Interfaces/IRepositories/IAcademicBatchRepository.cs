using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IAcademicBatchRepository : IGenericRepository<AcademicBatch>
{
    Task<List<AcademicBatch>> GetByAcademicLevelIdAsync(long academicLevelId);
    Task<bool> IsBatchNameExistsAsync(string name, long academicLevelId, long? excludeId = null);
    Task<int> GetTotalCapacityAsync(long academicLevelId);
}
