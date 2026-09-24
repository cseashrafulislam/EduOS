using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices;

public interface IAcademicRoutineService
{
    Task<ApiResponse<IReadOnlyList<RoutineTimeSlotDto>>> GetTimeSlotsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<RoutineTimeSlotDto>> CreateTimeSlotAsync(CreateRoutineTimeSlotDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<InstructorAssignmentDto>>> GetAssignmentsAsync(long academicBatchId, long? academicTermId, CancellationToken cancellationToken = default);
    Task<ApiResponse<InstructorAssignmentDto>> AssignInstructorAsync(AssignInstructorDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<RoutineEntryDto>>> GetBatchTimetableAsync(long academicBatchId, long? academicTermId, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<RoutineEntryDto>>> GetTeacherTimetableAsync(long employeeId, long academicYearId, long? academicTermId, CancellationToken cancellationToken = default);
    Task<ApiResponse<RoutineEntryDto>> CreateEntryAsync(CreateRoutineEntryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RoutineEntryDto>> DeactivateEntryAsync(long id, CancellationToken cancellationToken = default);
}
