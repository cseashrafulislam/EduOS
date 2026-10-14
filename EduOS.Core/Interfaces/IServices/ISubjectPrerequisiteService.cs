using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices;

public interface ISubjectPrerequisiteService
{
    Task<ApiResponse<IReadOnlyList<SubjectDto>>> OptionsAsync(long curriculumId, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<SubjectPrerequisiteDto>>> SearchAsync(long curriculumId, string? search, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<ApiResponse<SubjectPrerequisiteDto>> CreateAsync(SaveSubjectPrerequisiteRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<SubjectPrerequisiteDto>> UpdateAsync(long id, SaveSubjectPrerequisiteRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<bool>> ArchiveAsync(long id, string rowVersion, CancellationToken ct = default);
}
