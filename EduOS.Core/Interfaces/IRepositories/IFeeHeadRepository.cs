using EduOS.Core.Entities.Finance;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IFeeHeadRepository : IGenericRepository<FeeHead>
    {
        Task<List<FeeHead>> GetActiveAsync(int tenantId);
        Task<List<FeeHead>> GetByTypeAsync(string type, int tenantId);
    }
}
