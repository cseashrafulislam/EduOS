using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;

namespace EduOS.Core.Interfaces.IServices;

public interface IDocumentService
{
    Task<ApiResponse<PagedResult<DocumentDto>>> GetDocumentsAsync(string? entityType, long? entityId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<DocumentDto>> GetDocumentAsync(Guid documentReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<DocumentDto>> SaveDocumentAsync(Guid? documentReference, SaveDocumentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<DocumentDto>> DeactivateDocumentAsync(Guid documentReference, string rowVersion, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<DocumentTemplateDto>>> GetTemplatesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<DocumentTemplateDto>> SaveTemplateAsync(long? templateId, SaveDocumentTemplateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<DocumentTypeDefinitionDto>>> GetDocumentTypesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<DocumentTypeDefinitionDto>> SaveDocumentTypeAsync(long? documentTypeId, SaveDocumentTypeDefinitionRequestDto request, CancellationToken cancellationToken = default);
}
