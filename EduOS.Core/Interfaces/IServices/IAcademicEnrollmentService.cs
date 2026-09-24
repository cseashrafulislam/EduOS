using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices;

public interface IAcademicEnrollmentService
{
    Task<ApiResponse<AcademicStudentEnrollmentDto>> GetCurrentAsync(Guid studentReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicStudentEnrollmentDto>> EnrollAsync(CreateAcademicStudentEnrollmentDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentSubjectRegistrationDto>> RequestOptionalSubjectAsync(long studentEnrollmentId, RequestOptionalSubjectDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentSubjectRegistrationDto>> DecideSubjectAsync(long registrationId, DecideSubjectRegistrationDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<RoutineEntryDto>>> GetTimetableAsync(Guid studentReference, CancellationToken cancellationToken = default);
}
