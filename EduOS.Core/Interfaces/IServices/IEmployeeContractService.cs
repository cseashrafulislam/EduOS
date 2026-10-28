using EduOS.Core.Common;
using EduOS.Core.DTOs.HR;
using EduOS.Core.DTOs.Files;

namespace EduOS.Core.Interfaces.IServices;

public interface IEmployeeContractService
{
    Task<ApiResponse<PagedResult<EmployeeContractDto>>> ListAsync(Guid employeeReference, int page,
        int pageSize, CancellationToken ct = default);
    Task<ApiResponse<EmployeeContractDto>> CreateAsync(CreateEmployeeContractRequestDto request,
        CancellationToken ct = default);
    Task<ApiResponse<EmployeeContractDto>> ReviewAsync(Guid reference,
        ReviewEmployeeContractRequestDto request, bool approve, CancellationToken ct = default);
    Task<ApiResponse<SignedEmployeeContractPdfDto>> AttachSignedPdfAsync(Guid contractReference,
        AttachSignedEmployeeContractPdfRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<SignedEmployeeContractPdfDto>> VerifySignedPdfAsync(Guid contractReference,
        VerifySignedEmployeeContractPdfRequestDto request, bool approve, CancellationToken ct = default);
    Task<ApiResponse<DocumentFileDownloadDto>> DownloadSignedPdfAsync(Guid contractReference,
        bool reviewPreview, CancellationToken ct = default);
}
