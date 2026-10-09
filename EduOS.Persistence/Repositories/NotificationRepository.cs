using EduOS.Core.Entities.Communication;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class NotificationRepository : GenericRepository<Notification>, INotificationRepository
{
    public NotificationRepository(EduOSDbContext context) : base(context) { }

    public Task<List<Notification>> GetByUserAsync(long userId) =>
        _dbSet.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync();

    public Task<List<Notification>> GetUnreadAsync(long userId) =>
        _dbSet.AsNoTracking().Where(x => x.UserId == userId && !x.IsRead)
            .OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync();

    public Task<int> GetUnreadCountAsync(long userId) =>
        _dbSet.CountAsync(x => x.UserId == userId && !x.IsRead);

    public async Task MarkAsReadAsync(long notificationId)
    {
        // FindAsync can return a tracked notification without enforcing tenant/soft-delete filters.
        var row = await _dbSet.FirstOrDefaultAsync(x => x.Id == notificationId);
        if (row == null || row.IsRead) return;
        row.IsRead = true;
        row.ReadAt = DateTime.UtcNow;
    }

    public async Task MarkAllAsReadAsync(long userId)
    {
        var rows = await _dbSet.Where(x => x.UserId == userId && !x.IsRead).Take(1000).ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.IsRead = true;
            row.ReadAt = now;
        }
    }

    public Task<(List<Notification> Items, int TotalCount)> GetByUserAsync(long tenantId, long userId, int page, int pageSize, bool? unreadOnly, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.UserId == userId
            && (!unreadOnly.HasValue || x.IsRead != unreadOnly.Value))
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id), page, pageSize, cancellationToken);
    public Task<int> GetUnreadCountAsync(long tenantId, long userId, CancellationToken cancellationToken) =>
        _dbSet.CountAsync(x => x.TenantId == tenantId && x.UserId == userId && !x.IsRead, cancellationToken);
    public async Task<bool> MarkAsReadAsync(long tenantId, long userId, long notificationId, DateTime readAtUtc, CancellationToken cancellationToken)
    {
        if (readAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Read timestamp must be UTC.", nameof(readAtUtc));
        var changed = await _dbSet.Where(x => x.TenantId == tenantId && x.UserId == userId
            && x.Id == notificationId && !x.IsRead).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.IsRead, true).SetProperty(x => x.ReadAt, readAtUtc), cancellationToken);
        return changed > 0;
    }
    public Task<int> MarkAllAsReadAsync(long tenantId, long userId, DateTime readAtUtc, CancellationToken cancellationToken)
    {
        if (readAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Read timestamp must be UTC.", nameof(readAtUtc));
        return _dbSet.Where(x => x.TenantId == tenantId && x.UserId == userId && !x.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsRead, true)
                .SetProperty(x => x.ReadAt, readAtUtc), cancellationToken);
    }
}
