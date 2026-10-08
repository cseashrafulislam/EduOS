using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;

namespace EduOS.Core.Interfaces.IServices;

public interface ILmsLiveClassService
{
    Task<ApiResponse<LiveClassDto>> SaveLiveClassAsync(Guid? liveClassReference, SaveLiveClassRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LiveClassDto>> GetLiveClassAsync(Guid liveClassReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<LiveClassDto>>> GetCourseLiveClassesAsync(Guid courseReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<LiveClassDto>> CancelLiveClassAsync(Guid liveClassReference, string rowVersion, CancellationToken cancellationToken = default);
}
