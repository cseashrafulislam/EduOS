using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    // A saved draft is compared against the CURRENT posted bank book only after
    // the persisted statement fingerprint and row count pass integrity validation.
    // This is read-only review evidence, never clearance or approval.
    public async Task<ApiResponse<BankReconciliationSavedReviewDto>> ReviewSavedBankReconciliationAsync(
        long batchId, CancellationToken ct = default)
    {
        var stored = await GetBankReconciliationDraftAsync(batchId, ct);
        if (!stored.Success || stored.Data == null)
            return ApiResponse<BankReconciliationSavedReviewDto>.ErrorResponse(
                stored.Message, stored.StatusCode);

        var draft = stored.Data;
        var statement = new BankStatementPreviewRequestDto
        {
            BankAccountId = draft.BankAccountId,
            FromDate = draft.FromDate,
            ToDate = draft.ToDate,
            OpeningBalance = draft.OpeningBalance,
            ClosingBalance = draft.ClosingBalance,
            Movements = draft.Movements.Select(row => new BankStatementMovementDto
            {
                Date = row.Date, Reference = row.Reference,
                Deposit = row.Deposit, Withdrawal = row.Withdrawal
            }).ToArray()
        };
        // Canonical preview rechecks bank/tenant/campus/currency, balanced posted
        // journals and historical integrity. No caller-supplied statement is trusted.
        var live = await PreviewBankStatementAsync(statement, ct);
        if (!live.Success || live.Data == null)
            return ApiResponse<BankReconciliationSavedReviewDto>.ErrorResponse(
                live.Message, live.StatusCode);

        return ApiResponse<BankReconciliationSavedReviewDto>.SuccessResponse(
            new BankReconciliationSavedReviewDto
            {
                Draft = draft,
                Comparison = live.Data,
                ReviewedAtUtc = DateTime.UtcNow
            }, "Live comparison of the immutable saved statement; not approved or cleared.");
    }
}
