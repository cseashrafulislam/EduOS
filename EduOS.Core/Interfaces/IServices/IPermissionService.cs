using System.Threading.Tasks;

namespace EduOS.Core.Interfaces.IServices
{
    public interface IPermissionService
    {
        Task<bool> HasPermissionAsync(long userId, string pageCode, string action);
    }
}