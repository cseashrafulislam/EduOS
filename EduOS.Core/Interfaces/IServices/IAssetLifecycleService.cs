using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;

namespace EduOS.Core.Interfaces.IServices;

public interface IAssetLifecycleService
{
    Task<ApiResponse<IReadOnlyList<AssetCategoryDto>>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<AssetCategoryDto>> SaveCategoryAsync(long? categoryId, SaveAssetCategoryRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<AssetDto>>> GetAssetsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssetDto>> SaveAssetAsync(Guid? assetReference, SaveAssetRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssetAssignmentDto>> AssignAssetAsync(AssignAssetRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssetAssignmentDto>> ReturnAssetAsync(long assignmentId, ReturnAssetRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AssetMaintenanceDto>> RecordMaintenanceAsync(RecordAssetMaintenanceRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<AssetAssignmentDto>>> GetAssignmentHistoryAsync(Guid assetReference, CancellationToken cancellationToken = default);
}
