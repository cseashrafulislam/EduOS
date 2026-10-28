using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    // Historical evidence is derived exclusively from immutable statement, match and
    // approval records. A later reversal or journal amendment must not rewrite history.
    public async Task<ApiResponse<BankReconciliationEvidenceReportDto>> GetBankReconciliationEvidenceAsync(
        long batchId, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<BankReconciliationEvidenceReportDto>.ErrorResponse(
                "Tenant administrator access required.", 403);
        if (batchId <= 0)
            return ApiResponse<BankReconciliationEvidenceReportDto>.ErrorResponse("Invalid statement ID.", 400);

        // GetDraft verifies tenant ownership, contiguous 1-500 statement rows, the
        // original SHA-256 fingerprint and the opening/closing balance equation.
        var saved = await GetBankReconciliationDraftAsync(batchId, ct);
        if (!saved.Success || saved.Data == null)
            return ApiResponse<BankReconciliationEvidenceReportDto>.ErrorResponse(
                saved.Message, saved.StatusCode);
        var statement = saved.Data;

        // Server-side anti-join returns only active confirmations. Withdrawal history
        // remains accessible through the existing paged match-events endpoint.
        var active = await db.BankReconciliationMatchEvents.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                x.BankReconciliationBatchId == batchId && x.ActionCode == 1 &&
                !db.BankReconciliationMatchEvents.Any(w =>
                    w.TenantId == user.TenantId && !w.IsDeleted &&
                    w.BankReconciliationBatchId == batchId && w.ActionCode == 2 &&
                    w.OriginalMatchEventId == x.Id))
            .OrderBy(x => x.StatementRowNumber).ThenBy(x => x.Id)
            .Take(501).ToListAsync(ct);
        var approvals = await db.BankReconciliationApprovalEvents.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                x.BankReconciliationBatchId == batchId)
            .OrderBy(x => x.ActionCode).Take(5).ToListAsync(ct);
        if (active.Count > statement.MovementCount || approvals.Count > 4 ||
            approvals.Where((x, i) => x.ActionCode != i + 2).Any() ||
            active.Select(x => x.StatementRowNumber).Distinct().Count() != active.Count ||
            active.Select(x => x.JournalLineId).Distinct().Count() != active.Count)
            return ApiResponse<BankReconciliationEvidenceReportDto>.ErrorResponse(
                "Historical reconciliation evidence is inconsistent.", 409);

        var snapshot = statement.Movements.ToDictionary(x => x.RowNumber);
        foreach (var match in active)
        {
            if (!snapshot.TryGetValue(match.StatementRowNumber, out var row) ||
                match.JournalLineId <= 0 || string.IsNullOrWhiteSpace(match.JournalLineVersion) ||
                match.Date != row.Date || match.Deposit != row.Deposit ||
                match.Withdrawal != row.Withdrawal ||
                (match.Reference ?? string.Empty).Trim() != (row.Reference ?? string.Empty).Trim())
                return ApiResponse<BankReconciliationEvidenceReportDto>.ErrorResponse(
                    "A stored match conflicts with the immutable statement.", 409);
        }

        string? fingerprint = null;
        if (approvals.Count != 0)
        {
            if (active.Count != statement.MovementCount)
                return ApiResponse<BankReconciliationEvidenceReportDto>.ErrorResponse(
                    "Approved statement has incomplete historical matches.", 409);
            var buffer = new StringBuilder(statement.SourceFingerprint);
            foreach (var x in active)
                buffer.Append('|').Append(x.StatementRowNumber).Append(':').Append(x.JournalLineId)
                    .Append(':').Append(x.JournalLineVersion).Append(':')
                    .Append(x.Deposit.ToString("0.00", CultureInfo.InvariantCulture)).Append(':')
                    .Append(x.Withdrawal.ToString("0.00", CultureInfo.InvariantCulture));
            fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buffer.ToString())));
            if (approvals.Any(x => x.MatchedCount != active.Count ||
                x.EvidenceFingerprint != fingerprint))
                return ApiResponse<BankReconciliationEvidenceReportDto>.ErrorResponse(
                    "Historical approval fingerprint mismatch.", 409);
        }

        var matchByRow = active.ToDictionary(x => x.StatementRowNumber);
        var rows = statement.Movements.Select(x =>
        {
            matchByRow.TryGetValue(x.RowNumber, out var match);
            return new BankReconciliationEvidenceRowDto
            {
                RowNumber = x.RowNumber, Date = x.Date, Reference = x.Reference,
                Deposit = x.Deposit, Withdrawal = x.Withdrawal,
                MatchEventId = match?.Id, JournalLineId = match?.JournalLineId,
                JournalLineVersion = match?.JournalLineVersion
            };
        }).ToArray();
        return ApiResponse<BankReconciliationEvidenceReportDto>.SuccessResponse(new()
        {
            Statement = statement,
            StateCode = approvals.Count == 0 ? 1 : approvals[^1].ActionCode,
            ConfirmedCount = active.Count, UnmatchedCount = rows.Length - active.Count,
            TotalDeposits = rows.Sum(x => x.Deposit),
            TotalWithdrawals = rows.Sum(x => x.Withdrawal),
            ClearanceFingerprint = fingerprint,
            Rows = rows, ApprovalEvents = approvals.Select(ApprovalDto).ToArray()
        });
    }
}
