using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices
{
    public interface IClassService
    {
        Task<ApiResponse<PagedResult<ClassDto>>> GetAllAsync(ClassListFilterDto filter);
        Task<ApiResponse<ClassDto>> GetByIdAsync(long id);
        Task<ApiResponse<ClassDto>> CreateAsync(ClassCreateDto dto);
        Task<ApiResponse<ClassDto>> UpdateAsync(long id, ClassUpdateDto dto);
        Task<ApiResponse<bool>> DeleteAsync(long id);
        Task<ApiResponse<List<ClassDto>>> GetActiveClassesAsync();
    }
}
