using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IAcademicTrackRepository : IGenericRepository<AcademicTrack>
{
    Task<List<AcademicTrack>> GetActiveTracksAsync(long tenantId);
    Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId = null);
}
