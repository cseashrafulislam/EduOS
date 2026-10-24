using EduOS.Core.Common;
using EduOS.Core.DTOs.Student;
using EduOS.Core.DTOs.Files;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentDocumentService
{
    Task<ApiResponse<IReadOnlyList<StudentDocumentDto>>> GetAsync(Guid studentReference, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<StudentDocumentTypeDto>>> GetTypesAsync(CancellationToken ct = default);
    Task<ApiResponse<StudentDocumentDto>> AttachAsync(Guid studentReference, AttachStudentDocumentDto request, CancellationToken ct = default);
    Task<ApiResponse<StudentDocumentDto>> DeactivateAsync(Guid studentReference, Guid documentReference, DeactivateStudentDocumentDto request, CancellationToken ct = default);
    Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(Guid studentReference, Guid documentReference, CancellationToken ct = default);
}
