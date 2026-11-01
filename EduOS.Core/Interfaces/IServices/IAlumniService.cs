using EduOS.Core.Common;
using EduOS.Core.DTOs.Alumni;

namespace EduOS.Core.Interfaces.IServices;

public interface IAlumniService
{
    Task<ApiResponse<CareerOpportunityItemDto>> CreateOpportunityAsync(CareerOpportunityDraftDto request, CancellationToken ct = default);
    Task<ApiResponse<CareerOpportunityItemDto>> ChangeOpportunityAsync(Guid reference, CareerActionDto request, int action, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<CareerOpportunityItemDto>>> OpportunitiesAsync(CareerOpportunityQueryDto query, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<CareerEligibleStudentDto>>> CareerStudentsAsync(CancellationToken ct = default);
    Task<ApiResponse<CareerApplicationItemDto>> ApplyCareerAsync(Guid opportunity, CareerApplyDto request, CancellationToken ct = default);
    Task<ApiResponse<CareerApplicationItemDto>> ChangeApplicationAsync(Guid application, CareerActionDtoBase request, int action, int outcome, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<CareerApplicationItemDto>>> CareerApplicationsAsync(CareerApplicationQueryDto query, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<CareerPlacementReportItemDto>>> CareerReportAsync(CareerOpportunityQueryDto query, CancellationToken ct = default);
    Task<ApiResponse<CareerEmployerItemDto>> CreateEmployerAsync(CareerEmployerDraftDto request, CancellationToken ct = default);
    Task<ApiResponse<CareerEmployerItemDto>> VerifyEmployerAsync(Guid reference, CareerEmployerVerifyDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<CareerEmployerItemDto>>> EmployersAsync(CareerEmployerQueryDto query, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<AlumniEventCampusDto>>> EventCampusesAsync(CancellationToken ct = default);
    Task<ApiResponse<AlumniInvitationSummaryDto>> InvitationSummaryAsync(Guid reference, int channel, CancellationToken ct = default);
    Task<ApiResponse<AlumniInvitationBatchDto>> SendInvitationsAsync(Guid reference, AlumniInvitationSendDto request, CancellationToken ct = default);
    Task<ApiResponse<AlumniEventItemDto>> CreateEventAsync(AlumniEventDraftDto request, CancellationToken ct = default);
    Task<ApiResponse<AlumniEventItemDto>> ChangeEventStatusAsync(Guid reference, AlumniEventActionDto request, bool publish, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<AlumniEventItemDto>>> EventsAsync(AlumniEventQueryDto query, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<AlumniEventAttendanceItemDto>>> AttendanceAsync(Guid eventReference, AlumniEventAttendanceQueryDto query, CancellationToken ct = default);
    Task<ApiResponse<AlumniEventAttendanceItemDto>> CheckInAsync(Guid eventReference, AlumniEventCheckInDto request, CancellationToken ct = default);
    Task<ApiResponse<AlumniEventItemDto>> RsvpAsync(AlumniEventRsvpDto request, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<AlumniSelfDto>>> MineAsync(CancellationToken ct = default);
    Task<ApiResponse<AlumniSelfDto>> SaveConsentAsync(AlumniConsentRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<AlumniDirectoryItemDto>>> DirectoryAsync(AlumniDirectoryQueryDto query, CancellationToken ct = default);
}
