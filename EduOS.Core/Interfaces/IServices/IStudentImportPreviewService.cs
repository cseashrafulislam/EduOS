using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Tenant-scoped CSV/XLSX header inspection and read-only admission/student import preflight.</summary>
public interface IStudentImportPreviewService
{
    Task<ApiResponse<IReadOnlyList<string>>> GetHeadersAsync(
        Stream content, string extension, CancellationToken cancellationToken = default);

    Task<ApiResponse<StudentImportPreviewDto>> PreviewAsync(
        Stream content, string extension, IReadOnlyDictionary<string, string> mapping,
        CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentImportPreviewDto>> PreviewAllErrorsAsync(
        Stream content, string extension, IReadOnlyDictionary<string, string> mapping,
        CancellationToken cancellationToken = default);
}
