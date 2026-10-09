using EduOS.Core.Common;
using EduOS.Core.DTOs.System;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Custom-field definitions and values; implementations must authorize the referenced entity type and ID within the current tenant.</summary>
public interface ICustomFieldService
{
    Task<ApiResponse<PagedResult<CustomFieldDefinitionDto>>> GetDefinitionsAsync(string? entityType, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<CustomFieldDefinitionDto>> SaveDefinitionAsync(long? definitionId, SaveCustomFieldDefinitionRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<CustomFieldValueDto>>> GetValuesAsync(string entityType, long entityId, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<CustomFieldValueDto>>> SaveValuesAsync(string entityType, long entityId, IReadOnlyList<SaveCustomFieldValueRequestDto> requests, CancellationToken cancellationToken = default);
}
