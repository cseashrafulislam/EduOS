using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    private readonly record struct BankMovementKey(DateOnly Date, decimal Deposit, decimal Withdrawal, string Reference);
    private readonly record struct IndexedStatementMovement(int RowNumber, BankStatementMovementDto Movement);
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
        var indexedStatement = request.Movements.Select((movement, index) =>
            new IndexedStatementMovement(index + 1, movement)).ToArray();
        var statement = indexedStatement.GroupBy(x => Key(x.Movement.Date, x.Movement.Deposit,
                x.Movement.Withdrawal, x.Movement.Reference))
            .ToDictionary(x => x.Key, x => x.ToArray());
        // Reference-only candidates explain exceptions, but NEVER become automatic matches.
        // Reused references cannot identify a unique counterparty even when amounts agree.
        var ledgerByReference = book.Data.Entries
            .Where(x => !string.IsNullOrWhiteSpace(x.ReferenceNumber))
            .GroupBy(x => Key(x.Date, x.DebitAmount, x.CreditAmount, x.ReferenceNumber).Reference)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var statementByReference = indexedStatement
            .Where(x => !string.IsNullOrWhiteSpace(x.Movement.Reference))
            .GroupBy(x => Key(x.Movement.Date, x.Movement.Deposit,
                x.Movement.Withdrawal, x.Movement.Reference).Reference)
            .ToDictionary(x => x.Key, x => x.ToArray());
        // A reference reused on different dates or amounts is not an unambiguous bank identifier.
        // Check frequency across the WHOLE period, not only within an exact-match key.
        var reusedReferences = ledgerByReference.Keys.Concat(statementByReference.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(reference =>
                (ledgerByReference.TryGetValue(reference, out var ledgerOccurrences) && ledgerOccurrences.Length > 1) ||
                (statementByReference.TryGetValue(reference, out var statementOccurrences) && statementOccurrences.Length > 1))
            .ToHashSet(StringComparer.Ordinal);
        var exceptions = new List<BankStatementExceptionDto>();
        int matched = 0, missingLedger = 0, missingStatement = 0;
        var matches = new List<BankStatementMatchDto>();
        foreach (var key in ledger.Keys.Concat(statement.Keys).Distinct().OrderBy(x => x.Date).ThenBy(x => x.Reference))
        {
            ledger.TryGetValue(key, out var a);
            statement.TryGetValue(key, out var b);
            a ??= Array.Empty<BankBookLineDto>();
            b ??= Array.Empty<IndexedStatementMovement>();
            if (key.Reference.Length > 0 && !reusedReferences.Contains(key.Reference) && a.Length == 1 && b.Length == 1)
            {
                matched++;
                matches.Add(new BankStatementMatchDto
                {
                    JournalLineId = a[0].JournalLineId,
                    JournalLineVersion = a[0].JournalLineVersion,
                    StatementRowNumber = b[0].RowNumber,
                    Date = a[0].Date, JournalNumber = a[0].JournalNumber,
                    Reference = a[0].ReferenceNumber,
                    Deposit = a[0].DebitAmount, Withdrawal = a[0].CreditAmount
                });
                continue;
            }
            string reason;
            if (key.Reference.Length == 0)
                reason = "Missing reference: manual review";
            else if (a.Length > 1 || b.Length > 1)
                reason = "Duplicate key: ambiguous";
            else if (reusedReferences.Contains(key.Reference))
                reason = "Reference reused: ambiguous";
            else if (ledgerByReference.TryGetValue(key.Reference, out var ledgerCandidates) &&
                statementByReference.TryGetValue(key.Reference, out var statementCandidates))
            {
                if (ledgerCandidates.Length != 1 || statementCandidates.Length != 1)
                    reason = "Reference reused: ambiguous";
                else
                {
                    var l = ledgerCandidates[0];
                    var s = statementCandidates[0];
                    bool differentDate = l.Date != s.Movement.Date;
                    bool differentAmount = l.DebitAmount != s.Movement.Deposit ||
                        l.CreditAmount != s.Movement.Withdrawal;
                    reason = differentDate && differentAmount ? "Date and amount/direction differ: manual review" :
                        differentDate ? "Date differs: manual review" :
                        differentAmount ? "Amount/direction differs: manual review" : "No exact counterpart";
                }
            }
            else
                reason = "No exact counterpart";
            missingLedger += a.Length;
            missingStatement += b.Length;
            foreach (var x in a)
                exceptions.Add(new BankStatementExceptionDto { Source = "Ledger",
                    JournalLineId = x.JournalLineId, JournalLineVersion = x.JournalLineVersion,
                    Date = x.Date,
                    JournalNumber = x.JournalNumber, Reference = x.ReferenceNumber,
                    Deposit = x.DebitAmount, Withdrawal = x.CreditAmount, Reason = reason });
            foreach (var x in b)
                exceptions.Add(new BankStatementExceptionDto { Source = "Statement",
                    StatementRowNumber = x.RowNumber, Date = x.Movement.Date,
                    Reference = x.Movement.Reference, Deposit = x.Movement.Deposit,
                    Withdrawal = x.Movement.Withdrawal, Reason = reason });
        }
        var openingVariance = book.Data.OpeningBalance - request.OpeningBalance;
        var movementVariance = (book.Data.TotalInflows - book.Data.TotalOutflows) -
            (request.ClosingBalance - request.OpeningBalance);
        var closingVariance = book.Data.ClosingBalance - request.ClosingBalance;
        // Both balances and every movement must agree; offsetting differences cannot pass.
        var isExactMatch = openingVariance == 0m && movementVariance == 0m &&
            closingVariance == 0m && missingLedger == 0 && missingStatement == 0;
        return ApiResponse<BankStatementPreviewDto>.SuccessResponse(new BankStatementPreviewDto
        {
            BankName = book.Data.BankName, LedgerOpeningBalance = book.Data.OpeningBalance,
            LedgerClosingBalance = book.Data.ClosingBalance,
            StatementOpeningBalance = request.OpeningBalance, StatementClosingBalance = request.ClosingBalance,
            ClosingVariance = closingVariance, OpeningVariance = openingVariance,
            MovementVariance = movementVariance, IsExactMatch = isExactMatch,
            MatchedCount = matched, ReferenceCollisionCount = reusedReferences.Count, Matches = matches, UnmatchedLedgerCount = missingLedger,
            UnmatchedStatementCount = missingStatement, Exceptions = exceptions
        });
    }
}
