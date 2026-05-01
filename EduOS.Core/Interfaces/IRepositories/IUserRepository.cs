using EduOS.Core.Entities.Auth;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IUserRepository : IGenericRepository<User>
    {
        Task<User?> GetByEmailAsync(string email);
        Task<User?> GetByEmailWithRolesAsync(string email);
        Task<User?> GetByPhoneAsync(string phone);
        Task<User?> GetByRefreshTokenAsync(string refreshToken);
        Task<bool> IsEmailExistsAsync(string email, int? excludeId = null);
        Task<bool> IsPhoneExistsAsync(string phone, int? excludeId = null);
        Task<List<string>> GetUserPermissionsAsync(int userId);
        Task<List<User>> GetUsersByTypeAsync(string userType);
    }
}
