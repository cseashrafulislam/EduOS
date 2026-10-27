using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Only Active/Suspended operational transitions; exit/readmission use their own workflows.</summary>
public interface IStudentStatusTransitionService
{
    Task<ApiResponse<StudentStatusTransitionDto>> GetAsync(Guid studentReference, CancellationToken ct = default);
    Task<ApiResponse<StudentStatusTransitionDto>> ChangeAsync(Guid studentReference,
        ChangeStudentOperationalStatusRequestDto request, CancellationToken ct = default);
}
