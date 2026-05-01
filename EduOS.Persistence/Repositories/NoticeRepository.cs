using EduOS.Core.Entities.Communication;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class NoticeRepository : GenericRepository<Notice>, INoticeRepository
    {
        public NoticeRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<Notice>> GetActiveAsync(int tenantId)
        {
            return await _dbSet
                .Include(n => n.Category)
                .Where(n => n.TenantId == tenantId 
                    && n.IsActive 
                    && n.PublishDate <= DateTime.UtcNow
                    && (n.ExpireDate == null || n.ExpireDate >= DateTime.UtcNow))
                .OrderByDescending(n => n.PublishDate)
                .ToListAsync();
        }

        public async Task<List<Notice>> GetByAudienceAsync(string audience, int tenantId)
        {
            return await _dbSet
                .Where(n => n.TenantId == tenantId 
                    && n.IsActive
                    && (n.TargetAudience == "All" || n.TargetAudience == audience)
                    && n.PublishDate <= DateTime.UtcNow)
                .OrderByDescending(n => n.PublishDate)
                .ToListAsync();
        }

        public async Task<List<Notice>> GetRecentAsync(int tenantId, int count = 10)
        {
            return await _dbSet
                .Where(n => n.TenantId == tenantId && n.IsActive)
                .OrderByDescending(n => n.PublishDate)
                .Take(count)
                .ToListAsync();
        }
    }
}
