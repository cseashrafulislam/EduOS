using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;

namespace EduOS.Core.Interfaces.IServices;

public interface IAdmissionEnrollmentService
{
    Task<ApiResponse<AdmissionEnrollmentOptionsDto>> GetOptionsAsync(Guid applicationReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmittedStudentDto>> AdmitAsync(Guid applicationReference, AdmitAdmissionApplicationDto request, CancellationToken cancellationToken = default);
}
