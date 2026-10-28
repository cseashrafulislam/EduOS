using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentImportExecutionService
{
    Task<ApiResponse<StudentImportCohortPreflightDto>> PreflightCohortAsync(
        IReadOnlyList<StudentImportCohortUploadDto>? files,
        IReadOnlyDictionary<string, string>? mapping,
        CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentImportExecutionDto>> ExecuteAsync(Stream content, string extension,
        IReadOnlyDictionary<string, string> mapping, Guid clientRequestId, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentImportExecutionDto>> ExecuteSliceAsync(Stream content, string extension,
        IReadOnlyDictionary<string, string> mapping, Guid clientRequestId, int rowOffset,
        CancellationToken cancellationToken = default);
    Task<ApiResponse<byte[]>> ExportPendingCorrectionsAsync(Stream content, string extension,
        IReadOnlyDictionary<string, string> mapping, string mappingJson,
        CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentImportJobStatusDto>> GetJobStatusAsync(
        string fileHash, string mappingHash, int totalRows, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentImportCohortStatusDto>> GetCohortStatusAsync(
        IReadOnlyList<StudentImportCohortFileRequestDto>? files,
        CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentImportIntegrityReportDto>> GetIntegrityAsync(
        Guid importReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentImportReconciliationDto>> GetAsync(Guid importReference, int page, int pageSize,
        CancellationToken cancellationToken = default);
}
