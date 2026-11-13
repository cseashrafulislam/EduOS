using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Academic Batch is an optional cohort; students enroll in a class-owned Section and academic year.</summary>
public interface IAcademicEnrollmentService
{
    Task<ApiResponse<StudentEnrollmentDto>> GetCurrentAsync(Guid studentReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<AcademicCurriculumDto>>> GetEligibleCurriculaAsync(long academicBatchId, DateOnly enrollmentDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<AcademicCurriculumDto>>> GetEligibleSectionCurriculaAsync(long sectionId, long academicYearId, DateOnly date, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentEnrollmentDto>> EnrollAsync(CreateStudentEnrollmentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentSubjectRegistrationDto>> RequestOptionalSubjectAsync(RegisterStudentSubjectRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentSubjectRegistrationDto>> DecideSubjectAsync(long registrationId, ChangeSubjectRegistrationStateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<RoutineEntryDto>>> GetTimetableAsync(Guid studentReference, CancellationToken cancellationToken = default);
}
