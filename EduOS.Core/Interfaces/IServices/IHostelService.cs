using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.Interfaces.IServices;

public interface IHostelService
{
    Task<ApiResponse<PagedResult<HostelBedAvailabilityRowDto>>> GetBedAvailabilityAsync(long? campusId, DateOnly fromDate, DateOnly toDate, int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> ExportBedAvailabilityCsvAsync(long? campusId, DateOnly fromDate, DateOnly toDate, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HostelCheckoutFollowUpRowDto>>> GetCheckoutFollowUpsAsync(long? campusId, int page, int pageSize, string? search, int daysAhead, bool overdueOnly, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> ExportCheckoutFollowUpsCsvAsync(long? campusId, string? search, int daysAhead, bool overdueOnly, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HostelAllocationReadinessRowDto>>> GetAllocationReadinessAsync(long? campusId, int page, int pageSize, string? search, bool attentionOnly, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> ExportAllocationReadinessCsvAsync(long? campusId, string? search, bool attentionOnly, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<HostelFacilityCampusDto>>> GetFacilityCampusesAsync(CancellationToken ct = default);
    Task<ApiResponse<PagedResult<HostelFacilityDto>>> GetFacilitiesAsync(long? campusId, int page, int pageSize, string? search, CancellationToken ct = default);
    Task<ApiResponse<HostelFacilityDto>> CreateFacilityAsync(CreateHostelFacilityRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<HostelFacilityDto>> UpdateFacilityAsync(Guid reference, UpdateHostelFacilityRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<HostelFacilityDto>> SetFacilityStateAsync(Guid reference, SetHostelFacilityStateRequestDto request, CancellationToken ct = default);

    Task<ApiResponse<HostelRoomInventoryDto>> CreateRoomAsync(CreateHostelRoomRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<HostelRoomInventoryDto>>> GetRoomInventoryAsync(long? campusId, int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> ExportRoomInventoryCsvAsync(long? campusId, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<HostelRoomInventoryDto>> RenameRoomAsync(long roomId, RenameHostelRoomRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<HostelRoomInventoryDto>> SetRoomCapacityAsync(long roomId, SetHostelRoomCapacityRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<HostelRoomInventoryDto>> SetRoomStateAsync(long roomId, SetHostelRoomStateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HostelCapacityRowDto>>> GetCapacityAsync(long? campusId, int page, int pageSize, string? search, CancellationToken cancellationToken = default, bool attentionOnly = false);
    Task<ApiResponse<string>> ExportCapacityCsvAsync(long? campusId, string? search, CancellationToken cancellationToken = default, bool attentionOnly = false);
    Task<ApiResponse<HostelTurnoverReportDto>> GetTurnoverReportAsync(long? campusId, DateOnly? fromMonth, DateOnly? toMonth, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> ExportTurnoverReportCsvAsync(long? campusId, DateOnly? fromMonth, DateOnly? toMonth, CancellationToken cancellationToken = default);
    Task<ApiResponse<HostelOccupancyInsightsDto>> GetOccupancyInsightsAsync(long? campusId, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<HostelBedInventoryDto>> CreateBedAsync(CreateHostelBedRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<HostelBedInventoryDto>>> GetBedInventoryAsync(long? campusId, int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<HostelBedInventoryDto>> SetBedStateAsync(long bedId, SetHostelBedStateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> ExportBedInventoryCsvAsync(long? campusId, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<HostelRoomDto>>> GetRoomsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HostelStudentOptionDto>>> GetEligibleStudentsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default, DateOnly? startDate = null, DateOnly? endDate = null);
    Task<ApiResponse<PagedResult<HostelBedOptionDto>>> GetAvailableBedsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default, DateOnly? startDate = null);
    Task<ApiResponse<PagedResult<HostelAllocationRowDto>>> GetActiveAllocationsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HostelAllocationHistoryRowDto>>> GetAllocationHistoryAsync(long? campusId, int page, int pageSize, string? search, HostelAllocationState? state, DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> ExportAllocationHistoryCsvAsync(long? campusId, string? search, HostelAllocationState? state, DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentHostelAllocationDto?>> GetMyAllocationAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentHostelAllocationDto>> AllocateAsync(AllocateStudentHostelRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentHostelAllocationDto>> CloseAsync(long id, CloseStudentHostelAllocationRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentHostelAllocationDto>> CancelUpcomingAllocationAsync(long id, CancelStudentHostelAllocationRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentHostelAllocationDto>> RescheduleUpcomingAllocationStartAsync(long id, RescheduleUpcomingHostelStartRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HostelBedOptionDto>>> GetUpcomingRoomTransferOptionsAsync(long id, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentHostelAllocationDto>> ReassignUpcomingRoomAsync(long id, ReassignUpcomingHostelRoomRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HostelBedOptionDto>>> GetUpcomingBedReassignmentOptionsAsync(long id, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentHostelAllocationDto>> ReassignUpcomingBedAsync(long id, ReassignUpcomingHostelBedRequestDto request, CancellationToken cancellationToken = default);
}
