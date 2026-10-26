using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Institution-local contact details only; never rewrites global Person identity or historic snapshots.</summary>
public interface IStudentContactProfileService
{
    Task<ApiResponse<StudentContactProfileDto>> GetAsync(Guid studentReference, CancellationToken ct = default);
    Task<ApiResponse<StudentContactProfileDto>> UpdateAsync(Guid studentReference,
        UpdateStudentContactProfileRequestDto request, CancellationToken ct = default);
}
