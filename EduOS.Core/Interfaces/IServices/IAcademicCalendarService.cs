using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Calendar policy belongs to tenant/campus, while calendar events belong to academic year and term.</summary>
public interface IAcademicCalendarService
{
    Task<ApiResponse<AcademicCalendarPolicyDto>> GetPolicyAsync(long? campusId, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicCalendarPolicyDto>> SavePolicyAsync(SaveAcademicCalendarPolicyRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<AcademicCalendarEventDto>>> GetEventsAsync(long academicYearId, long? campusId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicCalendarEventDto>> CreateEventAsync(SaveAcademicCalendarEventRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcademicCalendarEventDto>> UpdateEventAsync(long id, SaveAcademicCalendarEventRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<AcademicWorkingDayDto>>> GetWorkingDaysAsync(long academicYearId, long? campusId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
}
