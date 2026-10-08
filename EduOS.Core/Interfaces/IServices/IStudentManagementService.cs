using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentManagementService
{
    Task<ApiResponse<StudentDto>> CreateStudentAsync(CreateStudentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentDto>> UpdateStudentAsync(Guid studentReference, UpdateStudentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentDto>> ChangeStudentStatusAsync(Guid studentReference, ChangeStudentStatusRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GuardianDto>> SaveGuardianAsync(Guid? guardianReference, SaveGuardianRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentGuardianDto>> LinkGuardianAsync(LinkStudentGuardianRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentGuardianDto>> UpdateGuardianLinkAsync(long studentGuardianId, UpdateStudentGuardianRequestDto request, CancellationToken cancellationToken = default);
}
