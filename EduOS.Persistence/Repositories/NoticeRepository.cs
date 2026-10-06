using EduOS.Core.Entities.Communication;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class NoticeRepository : GenericRepository<Notice>, INoticeRepository
{
    public NoticeRepository(EduOSDbContext context) : base(context) { }

    public Task<List<Notice>> GetActiveAsync(long tenantId)
    {
        var now = DateTime.UtcNow;
        return _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsPublished
                && x.PublishAt <= now && (!x.ExpiresAt.HasValue || x.ExpiresAt.Value >= now))
            .OrderByDescending(x => x.PublishAt).ToListAsync();
    }

    public async Task<List<Notice>> GetByAudienceAsync(string audience, long tenantId)
    {
        var now = DateTime.UtcNow;
        var noticeIds = _context.Set<NoticeAudience>()
            .Where(x => x.TenantId == tenantId
                && (x.AudienceTypeCode == "All" || x.AudienceTypeCode == audience))
            .Select(x => x.NoticeId);
        return await _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsPublished
                && x.PublishAt <= now && (!x.ExpiresAt.HasValue || x.ExpiresAt.Value >= now)
                && noticeIds.Contains(x.Id))
            .OrderByDescending(x => x.PublishAt).ToListAsync();
    }

    public Task<List<Notice>> GetRecentAsync(long tenantId, int count = 10)
    {
        var take = Math.Clamp(count, 1, 100);
        return _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsPublished)
            .OrderByDescending(x => x.PublishAt).Take(take).ToListAsync();
    }
}
