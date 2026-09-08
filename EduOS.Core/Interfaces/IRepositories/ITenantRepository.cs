using EduOS.Core.Entities.SaaS;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface ITenantRepository : IGenericRepository<Tenant>
    {
        Task<Tenant?> GetBySubdomainAsync(string subdomain);
        Task<Tenant?> GetByCodeAsync(string code);
        Task<bool> IsSubdomainExistsAsync(string subdomain, int? excludeId = null);
        Task<bool> IsCodeExistsAsync(string code, int? excludeId = null);
        Task<List<Tenant>> GetActiveTenantsAsync();
    }
}
