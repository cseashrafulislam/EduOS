using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;

namespace EduOS.Core.Interfaces.IServices;

public interface IPublicAdmissionService
{
    Task<ApiResponse<AdmissionApplicationOptionsDto>> GetOptionsAsync(string tenantKey, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionApplicationCreatedDto>> CreateAsync(string tenantKey, CreateAdmissionApplicationDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PublicAdmissionStatusDto>> GetStatusAsync(string tenantKey, Guid reference, string mobile, CancellationToken cancellationToken = default);
}
