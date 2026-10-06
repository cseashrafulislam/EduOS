using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories;

public interface ISectionRepository : IGenericRepository<AcademicBatch>
{
    Task<List<AcademicBatch>> GetByClassIdAsync(long classId);
    Task<bool> IsSectionNameExistsAsync(string name, long classId, long? excludeId = null);
    Task<int> GetTotalCapacityAsync(long classId);
}
