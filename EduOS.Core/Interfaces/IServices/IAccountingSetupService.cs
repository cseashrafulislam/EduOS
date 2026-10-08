using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;

namespace EduOS.Core.Interfaces.IServices;

public interface IAccountingSetupService
{
    Task<ApiResponse<PagedResult<AccountDto>>> GetAccountsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<AccountDto>> GetAccountAsync(long accountId, CancellationToken cancellationToken = default);
    Task<ApiResponse<AccountDto>> SaveAccountAsync(long? accountId, SaveAccountRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<AccountingPeriodDto>>> GetPeriodsAsync(int? year, CancellationToken cancellationToken = default);
    Task<ApiResponse<AccountingPeriodDto>> SavePeriodAsync(long? periodId, SaveAccountingPeriodRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AccountingPeriodDto>> ClosePeriodAsync(long periodId, CloseAccountingPeriodRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<BankAccountDto>>> GetBankAccountsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<BankAccountDto>> SaveBankAccountAsync(long? bankAccountId, SaveBankAccountRequestDto request, CancellationToken cancellationToken = default);
}
