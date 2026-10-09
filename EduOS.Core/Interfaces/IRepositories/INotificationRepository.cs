using EduOS.Core.Entities.Communication;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>All user notifications are tenant/user scoped; update methods must verify ownership rather than trusting notification IDs.</summary>
public interface INotificationRepository : IGenericRepository<Notification>
{
    Task<(List<Notification> Items, int TotalCount)> GetByUserAsync(long tenantId, long userId, int page, int pageSize, bool? unreadOnly = null, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(long tenantId, long userId, CancellationToken cancellationToken = default);
    Task<bool> MarkAsReadAsync(long tenantId, long userId, long notificationId, DateTime readAtUtc, CancellationToken cancellationToken = default);
    Task<int> MarkAllAsReadAsync(long tenantId, long userId, DateTime readAtUtc, CancellationToken cancellationToken = default);
}
