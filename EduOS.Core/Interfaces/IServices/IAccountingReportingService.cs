using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;

namespace EduOS.Core.Interfaces.IServices;

public interface IAccountingReportingService
{
    Task<ApiResponse<PostedDayBookDto>> GetPostedDayBookAsync(DateOnly fromDate, DateOnly toDate, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<ComparativeIncomeStatementDto>> GetComparativeIncomeStatementAsync(DateOnly fromDate, DateOnly toDate, DateOnly comparisonFrom, DateOnly comparisonTo, CancellationToken cancellationToken = default);
    Task<ApiResponse<ComparativeBalanceSheetDto>> GetComparativeBalanceSheetAsync(DateOnly asOfDate, DateOnly comparisonAsOfDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<IncomeExpenseBreakdownDto>> GetIncomeExpenseBreakdownAsync(DateOnly fromDate, DateOnly toDate, string groupBy, CancellationToken cancellationToken = default);
    Task<ApiResponse<InstitutionalIncomeExpenseDetailPageDto>> GetInstitutionalIncomeExpenseDetailsAsync(DateOnly fromDate, DateOnly toDate, string category = "all", long? accountId = null, string? sourceType = null, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<FinanceSourcePostingAuditPageDto>> GetFinanceSourcePostingAuditAsync(DateOnly fromDate, DateOnly toDate, string sourceKind, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<LedgerActivityPageDto>> GetLedgerActivityAsync(long accountId, DateOnly fromDate, DateOnly toDate, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<LedgerStatementDto>> GetLedgerStatementAsync(long accountId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<TrialBalanceRowDto>>> GetTrialBalanceAsync(DateOnly asOfDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<TrialBalancePageDto>> GetTrialBalancePageAsync(DateOnly asOfDate, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<IncomeStatementDto>> GetIncomeStatementAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<IncomeStatementPageDto>> GetIncomeStatementPageAsync(DateOnly fromDate, DateOnly toDate, string category = "all", int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<InstitutionalIncomeExpenseTotalsDto>> GetInstitutionalIncomeExpenseTotalsAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<BalanceSheetDto>> GetBalanceSheetAsync(DateOnly asOfDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<BalanceSheetPageDto>> GetBalanceSheetPageAsync(DateOnly asOfDate, string category = "all", int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankLiquidityDto>> GetBankLiquidityAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<LedgerStatementDto>> GetCashBookAsync(long accountId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<InstitutionalCashMovementDto>> GetInstitutionalCashMovementsAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<InstitutionalCashFlowStatementDto>> GetInstitutionalCashFlowStatementAsync(DateOnly fromDate, DateOnly toDate, DateTime? policyApprovedThroughUtc = null, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankStatementPreviewDto>> PreviewBankStatementAsync(BankStatementPreviewRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankReconciliationBatchDto>> SaveBankReconciliationDraftAsync(
        BankReconciliationSaveRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<BankReconciliationBatchDto>>> GetBankReconciliationDraftsAsync(
        long bankAccountId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankReconciliationBatchDto>> GetBankReconciliationDraftAsync(
        long batchId, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankReconciliationSavedReviewDto>> ReviewSavedBankReconciliationAsync(
        long batchId, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<BankReconciliationMatchEventDto>>> GetBankReconciliationMatchEventsAsync(
        long batchId, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankReconciliationEvidenceReportDto>> GetBankReconciliationEvidenceAsync(
        long batchId, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankReconciliationApprovalStatusDto>> GetBankReconciliationApprovalStatusAsync(
        long batchId, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankReconciliationApprovalEventDto>> RecordBankReconciliationApprovalAsync(
        BankReconciliationApprovalCommandDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankReconciliationMatchEventDto>> RecordBankReconciliationMatchEventAsync(
        BankReconciliationMatchCommandDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankReconciliationCandidatePageDto>> GetBankReconciliationCandidatesAsync(
        long bankAccountId, DateOnly fromDate, DateOnly toDate, string? referencePrefix,
        int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<BankBookDto>> GetBankBookAsync(long bankAccountId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
}
