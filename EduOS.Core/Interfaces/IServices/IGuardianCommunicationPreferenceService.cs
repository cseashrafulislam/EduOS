using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;

namespace EduOS.Core.Interfaces.IServices;

public interface IGuardianCommunicationPreferenceService
{
    Task<ApiResponse<IReadOnlyList<GuardianCommunicationPreferenceDto>>> GetMineAsync(CancellationToken ct = default);
    Task<ApiResponse<GuardianCommunicationPreferenceDto>> UpdateMineAsync(
        long linkId, UpdateGuardianCommunicationPreferenceDto request, CancellationToken ct = default);
}
