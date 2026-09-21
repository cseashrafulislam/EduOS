using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices;

public interface IAcademicCalendarService
{
    Task<ApiResponse<AcademicCalendarPolicyDto>> GetPolicyAsync(long academicYearId, long? campusId, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicCalendarPolicyDto>> SavePolicyAsync(SaveAcademicCalendarPolicyDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<AcademicCalendarEventDto>>> GetEventsAsync(long academicYearId, long? campusId, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicCalendarEventDto>> CreateEventAsync(CreateAcademicCalendarEventDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicCalendarEventDto>> UpdateEventAsync(long id, UpdateAcademicCalendarEventDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<AcademicWorkingDayDto>>> GetWorkingDaysAsync(long academicYearId, long? campusId, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
}
