using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class FeeHeadRepository : GenericRepository<FeeHead>, IFeeHeadRepository
{
    public FeeHeadRepository(EduOSDbContext context) : base(context) { }

    public Task<List<FeeHead>> GetActiveAsync(long tenantId) =>
        _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.Name).ToListAsync();

    public Task<List<FeeHead>> GetByTypeAsync(string type, long tenantId)
    {
        if (!Enum.TryParse<FeeFrequencyType>(type, true, out var frequency))
            return Task.FromResult(new List<FeeHead>());
        return _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && x.DefaultFrequency == frequency)
            .OrderBy(x => x.Name).ToListAsync();
    }

    public Task<List<FeeHead>> GetActiveAsync(long tenantId, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Name)
            .ThenBy(x => x.Id).ToListAsync(cancellationToken);
    public Task<List<FeeHead>> GetByDefaultFrequencyAsync(long tenantId, FeeFrequencyType frequency, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && x.DefaultFrequency == frequency)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(cancellationToken);
}
