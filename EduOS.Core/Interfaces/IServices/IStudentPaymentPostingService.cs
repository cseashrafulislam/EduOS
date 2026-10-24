using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentPaymentPostingService
{
    Task<ApiResponse<StudentPaymentPostingRegisterDto>> GetRegisterAsync(
        long? campusId, string? search, bool unpostedOnly, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PostStudentPaymentJournalResultDto>> PostAsync(
        Guid paymentReference, PostStudentPaymentJournalRequestDto request, CancellationToken ct = default);
}
