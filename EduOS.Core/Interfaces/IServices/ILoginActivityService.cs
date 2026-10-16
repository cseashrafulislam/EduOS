using EduOS.Core.Common;
using EduOS.Core.DTOs.Auth;
namespace EduOS.Core.Interfaces.IServices;
public interface ILoginActivityService
{
    Task<ApiResponse<PagedResult<LoginHistoryDto>>> GetMyHistoryAsync(int page, int pageSize, bool? success, CancellationToken cancellationToken = default);
}
