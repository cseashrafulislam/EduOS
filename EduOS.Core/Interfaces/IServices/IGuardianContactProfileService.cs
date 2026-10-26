using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Manage only a tenant's Guardian contact snapshot, through an active student link.</summary>
public interface IGuardianContactProfileService
{
    Task<ApiResponse<GuardianContactProfileDto>> GetAsync(Guid studentReference,
        Guid guardianReference, CancellationToken ct = default);
    Task<ApiResponse<GuardianContactProfileDto>> UpdateAsync(Guid studentReference,
        Guid guardianReference, UpdateGuardianContactProfileRequestDto request,
        CancellationToken ct = default);
}
