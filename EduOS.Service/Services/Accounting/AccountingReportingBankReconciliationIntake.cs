using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    private static string BankStatementFingerprint(BankStatementPreviewRequestDto statement)
    {
        var b = new StringBuilder();
        static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
        b.Append(statement.BankAccountId).Append('|').Append(statement.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append('|').Append(statement.ToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append('|').Append(Money(statement.OpeningBalance))
            .Append('|').Append(Money(statement.ClosingBalance)).Append('\n');
        foreach (var row in statement.Movements)
            {
            var reference = (row.Reference ?? string.Empty).Trim();
            b.Append(row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('|')
                .Append(reference.Length).Append(':').Append(reference).Append('|')
                .Append(Money(row.Deposit)).Append('|').Append(Money(row.Withdrawal)).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(b.ToString())));
    }

    private static BankReconciliationBatchDto IntakeDto(BankReconciliationBatch batch) => new()
    {
        Id = batch.Id, BankAccountId = batch.BankAccountId,
        ClientRequestId = batch.ClientRequestId, StatementIdentifier = batch.StatementIdentifier,
        SourceFingerprint = batch.SourceFingerprint, FromDate = batch.FromDate,
        ToDate = batch.ToDate, OpeningBalance = batch.OpeningBalance,
        ClosingBalance = batch.ClosingBalance, MovementCount = batch.MovementCount,
        StateCode = batch.StateCode, PreparedByUserId = batch.PreparedByUserId,
        PreparedAt = batch.PreparedAt, RowVersion = Convert.ToBase64String(batch.RowVersion)
    };

    // Durable intake only: no clearance, posting, approval, fuzzy matching or journal edits.
    // The SQL uniqueness constraints and serializable insert serialize concurrent retries.
    public async Task<ApiResponse<BankReconciliationBatchDto>> SaveBankReconciliationDraftAsync(
        BankReconciliationSaveRequestDto request, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<BankReconciliationBatchDto>.ErrorResponse("Tenant administrator access required.", 403);
        var statement = request?.Statement;
        var identifier = request?.StatementIdentifier?.Trim();
        if (request == null || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(identifier) || identifier.Length > 100 ||
            identifier.Any(char.IsControl) || statement == null ||
            statement.BankAccountId <= 0 || !Dates(statement.FromDate, statement.ToDate) ||
            statement.Movements == null || statement.Movements.Count is < 1 or > 500 ||
            statement.Movements.Any(x => x == null || x.Reference?.Length > 150 ||
                x.Reference?.Any(char.IsControl) == true))
            return ApiResponse<BankReconciliationBatchDto>.ErrorResponse(
                "Valid request ID, statement identity and 1-500 printable movements required.", 400);

        static bool ValidMoney(decimal x) => x >= -999999999999m &&
            x <= 999999999999m && decimal.Round(x, 2) == x;
        if (!ValidMoney(statement.OpeningBalance) || !ValidMoney(statement.ClosingBalance) ||
            statement.Movements.Any(x => x.Date < statement.FromDate || x.Date > statement.ToDate ||
                !ValidMoney(x.Deposit) || !ValidMoney(x.Withdrawal) ||
                x.Deposit < 0m || x.Withdrawal < 0m ||
                (x.Deposit > 0m) == (x.Withdrawal > 0m)))
            return ApiResponse<BankReconciliationBatchDto>.ErrorResponse(
                "Invalid statement dates, directions or amounts.", 400);
        if (statement.OpeningBalance + statement.Movements.Sum(x => x.Deposit) -
            statement.Movements.Sum(x => x.Withdrawal) != statement.ClosingBalance)
            return ApiResponse<BankReconciliationBatchDto>.ErrorResponse(
                "Statement opening and closing balances do not reconcile.", 422);

        var fingerprint = BankStatementFingerprint(statement);
        // An exact replay remains idempotent even if a later journal posting or period
        // closure changes the live preview. Existing intake evidence is never rewritten.
        var replay = await db.BankReconciliationBatches.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                (x.ClientRequestId == request.ClientRequestId ||
                 (x.BankAccountId == statement.BankAccountId && x.SourceFingerprint == fingerprint)), ct);
        if (replay != null)
        {
            if (replay.ClientRequestId != request.ClientRequestId ||
                replay.BankAccountId != statement.BankAccountId ||
                replay.SourceFingerprint != fingerprint ||
                replay.StatementIdentifier != identifier)
                return ApiResponse<BankReconciliationBatchDto>.ErrorResponse(
                    "Statement already imported or request key reused with different contents.", 409);
            return ApiResponse<BankReconciliationBatchDto>.SuccessResponse(IntakeDto(replay),
                "Existing immutable statement draft returned.");
        }

        // New imports must pass the canonical live bank-book validation. This is not
        // approval: future clearance must revalidate all source journal versions.
        var preview = await PreviewBankStatementAsync(statement, ct);
        if (!preview.Success)
            return ApiResponse<BankReconciliationBatchDto>.ErrorResponse(preview.Message, preview.StatusCode);
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            var existing = await db.BankReconciliationBatches.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                    (x.ClientRequestId == request.ClientRequestId ||
                     (x.BankAccountId == statement.BankAccountId && x.SourceFingerprint == fingerprint)), ct);
            if (existing != null)
            {
                if (existing.ClientRequestId != request.ClientRequestId ||
                    existing.BankAccountId != statement.BankAccountId ||
                    existing.SourceFingerprint != fingerprint ||
                    existing.StatementIdentifier != identifier)
                    return ApiResponse<BankReconciliationBatchDto>.ErrorResponse(
                        "Statement already imported or request key reused with different contents.", 409);
                return ApiResponse<BankReconciliationBatchDto>.SuccessResponse(IntakeDto(existing),
                    "Existing immutable statement draft returned.");
            }

            if (await db.AccountingPeriods.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && !x.IsDeleted && x.IsClosed &&
                x.StartDate <= statement.ToDate && x.EndDate >= statement.FromDate, ct))
                return ApiResponse<BankReconciliationBatchDto>.ErrorResponse(
                    "A closed accounting period overlaps the statement; intake is blocked.", 409);

            var batch = new BankReconciliationBatch
            {
                TenantId = user.TenantId, BankAccountId = statement.BankAccountId,
                ClientRequestId = request.ClientRequestId, StatementIdentifier = identifier,
                SourceFingerprint = fingerprint, FromDate = statement.FromDate,
                ToDate = statement.ToDate, OpeningBalance = statement.OpeningBalance,
                ClosingBalance = statement.ClosingBalance,
                MovementCount = statement.Movements.Count,
                PreparedByUserId = user.UserId, PreparedAt = DateTime.UtcNow, StateCode = 1
            };
            db.BankReconciliationBatches.Add(batch);
            await db.SaveChangesAsync(ct);
            db.BankReconciliationStatementLines.AddRange(statement.Movements.Select((x, i) =>
                new BankReconciliationStatementLine
                {
                    TenantId = user.TenantId, BankReconciliationBatchId = batch.Id,
                    RowNumber = i + 1, Date = x.Date,
                    Reference = x.Reference?.Trim(), Deposit = x.Deposit, Withdrawal = x.Withdrawal
                }));
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return ApiResponse<BankReconciliationBatchDto>.SuccessResponse(IntakeDto(batch),
                "Immutable statement draft saved; not approved or cleared.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            if (transaction != null) await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            // A racing request must not become a duplicate or a false success.
            return ApiResponse<BankReconciliationBatchDto>.ErrorResponse(
                "Statement intake conflict; reload the register before retrying.", 409);
        }
    }

    public async Task<ApiResponse<PagedResult<BankReconciliationBatchDto>>> GetBankReconciliationDraftsAsync(
        long bankAccountId, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<PagedResult<BankReconciliationBatchDto>>.ErrorResponse(
                "Tenant administrator access required.", 403);
        if (bankAccountId <= 0 || page < 1 || page > 10000 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > 1000000)
            return ApiResponse<PagedResult<BankReconciliationBatchDto>>.ErrorResponse(
                "Valid bank and bounded page required.", 400);
        if (!await db.BankAccounts.AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && x.Id == bankAccountId, ct))
            return ApiResponse<PagedResult<BankReconciliationBatchDto>>.ErrorResponse(
                "Bank account not found.", 404);

        var query = db.BankReconciliationBatches.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && x.BankAccountId == bankAccountId);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.PreparedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new BankReconciliationBatchDto
            {
                Id = x.Id, BankAccountId = x.BankAccountId, ClientRequestId = x.ClientRequestId,
                StatementIdentifier = x.StatementIdentifier, SourceFingerprint = x.SourceFingerprint,
                FromDate = x.FromDate, ToDate = x.ToDate, OpeningBalance = x.OpeningBalance,
                ClosingBalance = x.ClosingBalance, MovementCount = x.MovementCount,
                StateCode = x.StateCode, PreparedByUserId = x.PreparedByUserId,
                PreparedAt = x.PreparedAt, RowVersion = ""
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<BankReconciliationBatchDto>>.SuccessResponse(
            new PagedResult<BankReconciliationBatchDto>
            { Items = rows, TotalCount = total, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<BankReconciliationBatchDto>> GetBankReconciliationDraftAsync(
        long batchId, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<BankReconciliationBatchDto>.ErrorResponse("Tenant administrator access required.", 403);
        if (batchId <= 0)
            return ApiResponse<BankReconciliationBatchDto>.ErrorResponse("Invalid statement ID.", 400);
        var batch = await db.BankReconciliationBatches.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == user.TenantId && !x.IsDeleted && x.Id == batchId, ct);
        if (batch == null)
            return ApiResponse<BankReconciliationBatchDto>.ErrorResponse("Statement not found.", 404);
        var rows = await db.BankReconciliationStatementLines.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                x.BankReconciliationBatchId == batchId)
            .OrderBy(x => x.RowNumber).Take(501)
            .Select(x => new BankReconciliationStatementLineDto
            {
                RowNumber = x.RowNumber, Date = x.Date, Reference = x.Reference,
                Deposit = x.Deposit, Withdrawal = x.Withdrawal
            }).ToArrayAsync(ct);
        if (rows.Length != batch.MovementCount || rows.Length is < 1 or > 500 ||
            rows.Where((x, i) => x.RowNumber != i + 1).Any() ||
            batch.OpeningBalance + rows.Sum(x => x.Deposit) -
                rows.Sum(x => x.Withdrawal) != batch.ClosingBalance)
            return ApiResponse<BankReconciliationBatchDto>.ErrorResponse(
                "Statement snapshot integrity failed; investigate before approval.", 409);
        var persisted = new BankStatementPreviewRequestDto
        {
            BankAccountId = batch.BankAccountId, FromDate = batch.FromDate, ToDate = batch.ToDate,
            OpeningBalance = batch.OpeningBalance, ClosingBalance = batch.ClosingBalance,
            Movements = rows.Select(x => new BankStatementMovementDto
            {
                Date = x.Date, Reference = x.Reference, Deposit = x.Deposit,
                Withdrawal = x.Withdrawal
            }).ToArray()
        };
        if (BankStatementFingerprint(persisted) != batch.SourceFingerprint)
            return ApiResponse<BankReconciliationBatchDto>.ErrorResponse(
                "Statement fingerprint does not match the immutable snapshot.", 409);
        var dto = IntakeDto(batch);
        dto.Movements = rows;
        return ApiResponse<BankReconciliationBatchDto>.SuccessResponse(dto);
    }
}
