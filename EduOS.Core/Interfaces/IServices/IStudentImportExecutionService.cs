using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentImportExecutionService
{
    Task<ApiResponse<StudentImportExecutionDto>> ExecuteAsync(Stream content, string extension,
        IReadOnlyDictionary<string, string> mapping, Guid clientRequestId, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentImportExecutionDto>> ExecuteSliceAsync(Stream content, string extension,
        IReadOnlyDictionary<string, string> mapping, Guid clientRequestId, int rowOffset,
        CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentImportReconciliationDto>> GetAsync(Guid importReference, int page, int pageSize,
        CancellationToken cancellationToken = default);
}
