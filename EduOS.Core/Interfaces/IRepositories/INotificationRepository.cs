using EduOS.Core.Entities.Communication;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface INotificationRepository : IGenericRepository<Notification>
    {
        Task<List<Notification>> GetByUserAsync(long userId);
        Task<List<Notification>> GetUnreadAsync(long userId);
        Task<int> GetUnreadCountAsync(long userId);
        Task MarkAsReadAsync(long notificationId);
        Task MarkAllAsReadAsync(long userId);
    }
}
