using EduOS.Core.Entities.Communication;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface INoticeRepository : IGenericRepository<Notice>
    {
        Task<List<Notice>> GetActiveAsync(long tenantId);
        Task<List<Notice>> GetByAudienceAsync(string audience, long tenantId);
        Task<List<Notice>> GetRecentAsync(long tenantId, int count = 10);
    }
}
