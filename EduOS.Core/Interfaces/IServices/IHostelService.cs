using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.Interfaces.IServices;

public interface IHostelService
{
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
    Task<ApiResponse<HostelRoomInventoryDto>> SetRoomCapacityAsync(long roomId, SetHostelRoomCapacityRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<HostelRoomInventoryDto>> SetRoomStateAsync(long roomId, SetHostelRoomStateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HostelCapacityRowDto>>> GetCapacityAsync(long? campusId, int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> ExportCapacityCsvAsync(long? campusId, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<HostelOccupancyInsightsDto>> GetOccupancyInsightsAsync(long? campusId, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<HostelBedInventoryDto>> CreateBedAsync(CreateHostelBedRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<HostelBedInventoryDto>>> GetBedInventoryAsync(long? campusId, int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<HostelBedInventoryDto>> SetBedStateAsync(long bedId, SetHostelBedStateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> ExportBedInventoryCsvAsync(long? campusId, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<HostelRoomDto>>> GetRoomsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HostelStudentOptionDto>>> GetEligibleStudentsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HostelBedOptionDto>>> GetAvailableBedsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HostelAllocationRowDto>>> GetActiveAllocationsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HostelAllocationHistoryRowDto>>> GetAllocationHistoryAsync(long? campusId, int page, int pageSize, string? search, HostelAllocationState? state, DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> ExportAllocationHistoryCsvAsync(long? campusId, string? search, HostelAllocationState? state, DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentHostelAllocationDto?>> GetMyAllocationAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentHostelAllocationDto>> AllocateAsync(AllocateStudentHostelRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentHostelAllocationDto>> CloseAsync(long id, CloseStudentHostelAllocationRequestDto request, CancellationToken cancellationToken = default);
}
