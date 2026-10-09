using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Enrollment identity is the student and selected academic batch; dependent context is resolved server-side.</summary>
public interface IAcademicEnrollmentService
{
    Task<ApiResponse<StudentEnrollmentDto>> GetCurrentAsync(Guid studentReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentEnrollmentDto>> EnrollAsync(CreateStudentEnrollmentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentSubjectRegistrationDto>> RequestOptionalSubjectAsync(RegisterStudentSubjectRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentSubjectRegistrationDto>> DecideSubjectAsync(long registrationId, ChangeSubjectRegistrationStateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<RoutineEntryDto>>> GetTimetableAsync(Guid studentReference, CancellationToken cancellationToken = default);
}
