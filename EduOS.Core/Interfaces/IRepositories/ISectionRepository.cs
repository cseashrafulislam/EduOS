using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface ISectionRepository : IGenericRepository<Section>
    {
        Task<List<Section>> GetByClassIdAsync(int classId);
        Task<bool> IsSectionNameExistsAsync(string name, int classId, int? excludeId = null);
        Task<int> GetTotalCapacityAsync(int classId);
    }
}
