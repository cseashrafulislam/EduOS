using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Routine writes use canonical subject offerings, instructor assignments, slots and date ranges.</summary>
public interface IAcademicRoutineService
{
    Task<ApiResponse<IReadOnlyList<AcademicInstructorChoiceDto>>> GetInstructorChoicesAsync(string? search, int take = 50, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicInstructorChoicePageDto>> GetInstructorChoicesPageAsync(long academicBatchId, string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<RoutineTimeSlotDto>>> GetTimeSlotsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<RoutineTimeSlotDto>> CreateTimeSlotAsync(SaveRoutineTimeSlotRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<InstructorAssignmentDto>>> GetAssignmentsAsync(long academicBatchId, long? academicTermId, CancellationToken cancellationToken = default);
    Task<ApiResponse<InstructorAssignmentPageDto>> GetAssignmentsPageAsync(long academicBatchId, long? academicTermId, string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<InstructorAssignmentDto>> AssignInstructorAsync(SaveInstructorAssignmentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<RoutineEntryDto>>> GetBatchTimetableAsync(long academicBatchId, long? academicTermId, CancellationToken cancellationToken = default);
    Task<ApiResponse<RoutineTimetablePageDto>> GetBatchTimetablePageAsync(long academicBatchId, long? academicTermId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<RoutineEntryDto>>> GetTeacherTimetableAsync(long employeeId, long academicYearId, long? academicTermId, CancellationToken cancellationToken = default);
    Task<ApiResponse<RoutineTimetablePageDto>> GetTeacherTimetablePageAsync(long employeeId, long academicYearId, long? academicTermId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<RoutineEntryDto>> CreateEntryAsync(SaveRoutineEntryRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RoutineEntryDto>> DeactivateEntryAsync(long id, string rowVersion, CancellationToken cancellationToken = default);
}
