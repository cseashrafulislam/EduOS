using EduOS.Core.Entities.Communication;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface INotificationRepository : IGenericRepository<Notification>
    {
        Task<List<Notification>> GetByUserAsync(int userId);
        Task<List<Notification>> GetUnreadAsync(int userId);
        Task<int> GetUnreadCountAsync(int userId);
        Task MarkAsReadAsync(int notificationId);
        Task MarkAllAsReadAsync(int userId);
    }
}
