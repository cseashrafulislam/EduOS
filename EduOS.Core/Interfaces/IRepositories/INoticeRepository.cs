using EduOS.Core.Entities.Communication;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface INoticeRepository : IGenericRepository<Notice>
    {
        Task<List<Notice>> GetActiveAsync(int tenantId);
        Task<List<Notice>> GetByAudienceAsync(string audience, int tenantId);
        Task<List<Notice>> GetRecentAsync(int tenantId, int count = 10);
    }
}
