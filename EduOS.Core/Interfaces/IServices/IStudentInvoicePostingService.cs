using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
namespace EduOS.Core.Interfaces.IServices;
public interface IStudentInvoicePostingService
{
    Task<ApiResponse<StudentInvoicePostingRegisterDto>> GetRegisterAsync(
        long? campusId, string? search, bool unpostedOnly, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PostStudentInvoiceJournalResultDto>> PostAsync(
        Guid invoiceReference, PostStudentInvoiceJournalRequestDto request, CancellationToken ct = default);
}
