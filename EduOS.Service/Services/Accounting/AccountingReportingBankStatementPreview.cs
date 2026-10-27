using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    private readonly record struct BankMovementKey(DateOnly Date, decimal Deposit, decimal Withdrawal, string Reference);
    private static BankMovementKey Key(DateOnly date, decimal deposit, decimal withdrawal, string? reference) =>
        new(date, deposit, withdrawal, (reference ?? string.Empty).Trim().ToUpperInvariant());

    // Unique, exact reference/date/direction/amount only. No fuzzy matching or posting.
    // Uses the canonical bank book's tenant, campus, currency and posting-integrity checks.
    public async Task<ApiResponse<BankStatementPreviewDto>> PreviewBankStatementAsync(
        BankStatementPreviewRequestDto request, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<BankStatementPreviewDto>.ErrorResponse("Tenant administrator access required.", 403);
        if (request == null || request.BankAccountId <= 0 || !Dates(request.FromDate, request.ToDate) ||
            request.Movements == null || request.Movements.Count is < 1 or > 500)
            return ApiResponse<BankStatementPreviewDto>.ErrorResponse("Valid bank, period and 1–500 statement rows required.", 400);
        static bool Amount(decimal x) => x >= -999999999999m && x <= 999999999999m &&
            decimal.Round(x, 2) == x;
        if (!Amount(request.OpeningBalance) || !Amount(request.ClosingBalance) ||
            request.Movements.Any(x => x == null || x.Date < request.FromDate || x.Date > request.ToDate ||
                x.Reference?.Length > 150 || !Amount(x.Deposit) || !Amount(x.Withdrawal) ||
                x.Deposit < 0m || x.Withdrawal < 0m ||
                (x.Deposit > 0m) == (x.Withdrawal > 0m)))
            return ApiResponse<BankStatementPreviewDto>.ErrorResponse("Invalid date, direction or BDT amount in statement.", 400);
        if (request.OpeningBalance + request.Movements.Sum(x => x.Deposit) -
            request.Movements.Sum(x => x.Withdrawal) != request.ClosingBalance)
            return ApiResponse<BankStatementPreviewDto>.ErrorResponse("Statement opening, movements and closing do not balance.", 422);
        var book = await GetBankBookAsync(request.BankAccountId, request.FromDate, request.ToDate, ct);
        if (!book.Success || book.Data == null)
            return ApiResponse<BankStatementPreviewDto>.ErrorResponse(book.Message, book.StatusCode);
        var ledger = book.Data.Entries.GroupBy(x => Key(x.Date, x.DebitAmount, x.CreditAmount, x.ReferenceNumber))
            .ToDictionary(x => x.Key, x => x.ToArray());
        var statement = request.Movements.GroupBy(x => Key(x.Date, x.Deposit, x.Withdrawal, x.Reference))
            .ToDictionary(x => x.Key, x => x.ToArray());
        var exceptions = new List<BankStatementExceptionDto>();
        int matched = 0, missingLedger = 0, missingStatement = 0;
        var matches = new List<BankStatementMatchDto>();
        foreach (var key in ledger.Keys.Concat(statement.Keys).Distinct().OrderBy(x => x.Date).ThenBy(x => x.Reference))
        {
            ledger.TryGetValue(key, out var a);
            statement.TryGetValue(key, out var b);
            a ??= Array.Empty<BankBookLineDto>();
            b ??= Array.Empty<BankStatementMovementDto>();
            if (key.Reference.Length > 0 && a.Length == 1 && b.Length == 1)
            {
                matched++;
                matches.Add(new BankStatementMatchDto
                {
                    Date = a[0].Date, JournalNumber = a[0].JournalNumber,
                    Reference = a[0].ReferenceNumber,
                    Deposit = a[0].DebitAmount, Withdrawal = a[0].CreditAmount
                });
                continue;
            }
            var reason = key.Reference.Length == 0 ? "Missing reference: manual review" :
                a.Length > 1 || b.Length > 1 ? "Duplicate key: ambiguous" : "No exact counterpart";
            missingLedger += a.Length;
            missingStatement += b.Length;
            foreach (var x in a)
                exceptions.Add(new BankStatementExceptionDto { Source = "Ledger", Date = x.Date,
                    JournalNumber = x.JournalNumber, Reference = x.ReferenceNumber,
                    Deposit = x.DebitAmount, Withdrawal = x.CreditAmount, Reason = reason });
            foreach (var x in b)
                exceptions.Add(new BankStatementExceptionDto { Source = "Statement", Date = x.Date,
                    Reference = x.Reference, Deposit = x.Deposit, Withdrawal = x.Withdrawal, Reason = reason });
        }
        return ApiResponse<BankStatementPreviewDto>.SuccessResponse(new BankStatementPreviewDto
        {
            BankName = book.Data.BankName, LedgerOpeningBalance = book.Data.OpeningBalance,
            LedgerClosingBalance = book.Data.ClosingBalance,
            StatementOpeningBalance = request.OpeningBalance, StatementClosingBalance = request.ClosingBalance,
            ClosingVariance = book.Data.ClosingBalance - request.ClosingBalance,
            MatchedCount = matched, Matches = matches, UnmatchedLedgerCount = missingLedger,
            UnmatchedStatementCount = missingStatement, Exceptions = exceptions
        });
    }
}
