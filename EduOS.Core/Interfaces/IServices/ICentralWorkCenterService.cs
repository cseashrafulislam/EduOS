using EduOS.Core.Common;
using EduOS.Core.DTOs.Platform;

namespace EduOS.Core.Interfaces.IServices;

public interface ICentralWorkCenterService
{
    Task<ApiResponse<CentralWorkCenterPageDto>> GetPendingAsync(
        int page = 1, int pageSize = 25, long? campusId = null, CancellationToken ct = default);
}
