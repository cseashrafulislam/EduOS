using System.Threading.Tasks;

namespace EduOS.Core.Enums.Interfaces.Auth
{
    public interface IPermissionService
    {
        Task<bool> HasPermissionAsync(int userId, string pageCode, string action);
    }
}