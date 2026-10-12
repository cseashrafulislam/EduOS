using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Read-only, tenant-administrator elective registration workspace query.</summary>
public interface IStudentOptionalSubjectQueryService
{
    Task<ApiResponse<StudentOptionalSubjectWorkspaceDto>> GetAsync(Guid studentReference, CancellationToken ct = default);
}
