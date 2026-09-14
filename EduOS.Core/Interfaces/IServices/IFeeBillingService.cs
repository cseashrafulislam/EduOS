using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;

namespace EduOS.Core.Interfaces.IServices;

public interface IFeeBillingService
{
    Task<ApiResponse<bool>> SaveFeeStructureAsync(SaveFeeStructureDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InvoiceBatchResultDto>> GenerateInvoicesAsync(GenerateStudentInvoicesDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentPaymentDto>> CollectPaymentAsync(CollectStudentPaymentDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentInvoiceDto>> SetFineAsync(SetInvoiceFineDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentLedgerDto>> GetStudentLedgerAsync(Guid studentReference, CancellationToken cancellationToken = default);
}
