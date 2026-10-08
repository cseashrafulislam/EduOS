using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;

namespace EduOS.Core.Interfaces.IServices;

public interface IAccountingPostingService
{
    Task<ApiResponse<PagedResult<JournalDto>>> GetJournalsAsync(DateOnly fromDate, DateOnly toDate, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<JournalDto>> GetJournalAsync(Guid journalReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<JournalDto>> CreateJournalAsync(CreateJournalRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<JournalDto>> SubmitJournalAsync(Guid journalReference, SubmitJournalRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<JournalDto>> ReviewJournalAsync(Guid journalReference, ReviewJournalRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<JournalDto>> PostJournalAsync(Guid journalReference, PostJournalRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<JournalDto>> ReverseJournalAsync(Guid journalReference, ReverseJournalRequestDto request, CancellationToken cancellationToken = default);
}
