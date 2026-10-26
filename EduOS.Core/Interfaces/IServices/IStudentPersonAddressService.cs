using EduOS.Core.Common;
using EduOS.Core.DTOs.Learners;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentPersonAddressService
{
    Task<ApiResponse<List<StudentPersonAddressDto>>> GetAsync(Guid studentReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentPersonAddressDto>> SaveAsync(Guid studentReference, string addressType, SaveStudentPersonAddressRequestDto request, CancellationToken cancellationToken = default);
}
