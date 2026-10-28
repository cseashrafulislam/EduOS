using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;

namespace EduOS.Core.Interfaces.IServices;

public interface IAccountingReportingService
{
    Task<ApiResponse<LedgerActivityPageDto>> GetLedgerActivityAsync(long accountId, DateOnly fromDate, DateOnly toDate, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<LedgerStatementDto>> GetLedgerStatementAsync(long accountId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<TrialBalanceRowDto>>> GetTrialBalanceAsync(DateOnly asOfDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<IncomeStatementDto>> GetIncomeStatementAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<BalanceSheetDto>> GetBalanceSheetAsync(DateOnly asOfDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankLiquidityDto>> GetBankLiquidityAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankStatementPreviewDto>> PreviewBankStatementAsync(BankStatementPreviewRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankReconciliationBatchDto>> SaveBankReconciliationDraftAsync(
        BankReconciliationSaveRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<BankReconciliationBatchDto>>> GetBankReconciliationDraftsAsync(
        long bankAccountId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankReconciliationBatchDto>> GetBankReconciliationDraftAsync(
        long batchId, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankReconciliationCandidatePageDto>> GetBankReconciliationCandidatesAsync(
        long bankAccountId, DateOnly fromDate, DateOnly toDate, string? referencePrefix,
        int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankBookDto>> GetBankBookAsync(long bankAccountId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
}
