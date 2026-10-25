using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;

namespace EduOS.Core.Interfaces.IServices;

public interface IAdmissionCashReceiptService
{
    Task<ApiResponse<AdmissionCashReceiptWorkspaceDto>> GetWorkspaceAsync(string? search, CancellationToken ct = default);
    Task<ApiResponse<AdmissionCashReceiptDto>> CollectAsync(CollectAdmissionCashReceiptRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<AdmissionCashReceiptDto>> GetReceiptAsync(Guid receiptReference, CancellationToken ct = default);
}
