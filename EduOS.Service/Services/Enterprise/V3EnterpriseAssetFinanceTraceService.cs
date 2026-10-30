using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Enterprise;

// A read-only bridge from the asset owner's explicit depreciation-run journal IDs
// to V1 Finance. No stock-to-asset inference, journal posting or mutable totals.
public sealed class V3EnterpriseAssetFinanceTraceService(EduOSDbContext db, ICurrentUserService user)
{
    public async Task<ApiResponse<V3EnterpriseAssetFinanceTraceDto>> GetAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || !user.IsTenantAdmin || user.TenantId <= 0 ||
            db.CurrentTenantId != user.TenantId)
            return ApiResponse<V3EnterpriseAssetFinanceTraceDto>.ErrorResponse("Access denied.", 403);
        if (reference == Guid.Empty)
            return ApiResponse<V3EnterpriseAssetFinanceTraceDto>.ErrorResponse("Run reference required.", 400);

        var run = await db.AssetDepreciationRuns.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted && x.ClientRequestId == reference)
            .Select(x => new {
                x.Id, x.ClientRequestId, x.CampusId, x.PeriodEnd, x.StateCode,
                x.TotalAmount, x.AccountingPeriodId, x.JournalId, x.ReversalJournalId
            }).SingleOrDefaultAsync(ct);
        if (run == null)
            return ApiResponse<V3EnterpriseAssetFinanceTraceDto>.ErrorResponse("Run not found.", 404);

        var lines = db.AssetDepreciationLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.AssetDepreciationRunId == run.Id);
        if (await lines.AnyAsync(x => x.IsDeleted, ct))
            return ApiResponse<V3EnterpriseAssetFinanceTraceDto>.ErrorResponse(
                "Deleted depreciation evidence requires reconciliation.", 409);

        // A run cannot silently incorporate a book from a different campus or
        // a different period, even when all rows share the same tenant.
        if (await lines.AnyAsync(x => x.PeriodEnd != run.PeriodEnd ||
            !db.AssetBooks.IgnoreQueryFilters().Any(b => b.TenantId == user.TenantId &&
                b.Id == x.AssetBookId && !b.IsDeleted && b.CampusId == run.CampusId &&
                db.Assets.IgnoreQueryFilters().Any(a => a.TenantId == user.TenantId &&
                    a.Id == b.AssetId && !a.IsDeleted && a.CampusId == run.CampusId)), ct))
            return ApiResponse<V3EnterpriseAssetFinanceTraceDto>.ErrorResponse(
                "Asset book, campus or period evidence requires reconciliation.", 409);

        var aggregate = await lines.GroupBy(x => 1).Select(g => new {
            Count = g.Count(), Amount = g.Sum(x => x.Amount)
        }).FirstOrDefaultAsync(ct);

        if (run.ReversalJournalId.HasValue && !run.JournalId.HasValue)
            return ApiResponse<V3EnterpriseAssetFinanceTraceDto>.ErrorResponse(
                "Reversal has no original journal reference.", 409);

        var journalIds = new[] { run.JournalId, run.ReversalJournalId }
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var journals = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && journalIds.Contains(x.Id))
            .Select(x => new {
                x.Id, x.PublicId, x.JournalNumber, x.State, x.CampusId,
                x.IsDeleted, x.AccountingPeriodId, x.ReversalOfJournalId
            }).ToListAsync(ct);
        if (journals.Count != journalIds.Length ||
            journals.Any(x => x.IsDeleted || (x.CampusId.HasValue && x.CampusId != run.CampusId)))
            return ApiResponse<V3EnterpriseAssetFinanceTraceDto>.ErrorResponse(
                "Missing, deleted or cross-campus journal evidence requires reconciliation.", 409);
        if (run.JournalId.HasValue &&
            journals.Any(x => x.Id == run.JournalId.Value &&
                x.AccountingPeriodId != run.AccountingPeriodId))
            return ApiResponse<V3EnterpriseAssetFinanceTraceDto>.ErrorResponse(
                "Original journal accounting period requires reconciliation.", 409);
        if (run.ReversalJournalId.HasValue &&
            journals.Any(x => x.Id == run.ReversalJournalId.Value &&
                x.ReversalOfJournalId != run.JournalId))
            return ApiResponse<V3EnterpriseAssetFinanceTraceDto>.ErrorResponse(
                "Reversal journal lineage requires reconciliation.", 409);

        var journalLines = db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && journalIds.Contains(x.JournalId));
        if (await journalLines.AnyAsync(x => x.IsDeleted, ct))
            return ApiResponse<V3EnterpriseAssetFinanceTraceDto>.ErrorResponse(
                "Deleted journal line requires reconciliation.", 409);
        var totals = await journalLines.GroupBy(x => x.JournalId)
            .Select(g => new { Id = g.Key, Debit = g.Sum(x => x.DebitAmount),
                Credit = g.Sum(x => x.CreditAmount) }).ToListAsync(ct);
        var byId = totals.ToDictionary(x => x.Id);
        var evidence = journals.OrderBy(x => x.Id).Select(j => {
            byId.TryGetValue(j.Id, out var t);
            var debit = t?.Debit ?? 0m;
            var credit = t?.Credit ?? 0m;
            return new V3EnterpriseJournalEvidenceDto {
                Reference = j.PublicId, Number = j.JournalNumber, State = j.State.ToString(),
                CampusId = j.CampusId, Debit = debit, Credit = credit,
                Balanced = debit > 0m && debit == credit
            };
        }).ToArray();

        // A balanced original journal with the wrong debit total is not a
        // reconciled depreciation posting. Reversal dates may legitimately differ.
        var originalAmountMismatch = run.JournalId.HasValue &&
            byId.TryGetValue(run.JournalId.Value, out var originalTotal) &&
            originalTotal.Debit != run.TotalAmount;
        var status = aggregate?.Amount != run.TotalAmount ? "RunLineMismatch" :
            evidence.Length == 0 ? "NoExplicitReference" :
            evidence.Any(x => !x.Balanced) ? "JournalImbalance" :
            journals.Any(x => x.State != JournalState.Posted && x.State != JournalState.Reversed)
                ? "UnpostedJournal" :
            originalAmountMismatch ? "JournalAmountMismatch" : "ExplicitBalancedReference";

        return ApiResponse<V3EnterpriseAssetFinanceTraceDto>.SuccessResponse(
            new V3EnterpriseAssetFinanceTraceDto {
                RunReference = run.ClientRequestId, CampusId = run.CampusId,
                PeriodEnd = run.PeriodEnd, State = run.StateCode,
                RunTotalAmount = run.TotalAmount,
                DepreciationLinesAmount = aggregate?.Amount ?? 0m,
                DepreciationLineCount = aggregate?.Count ?? 0,
                FinanceEvidenceStatus = status, Journals = evidence
            });
    }
}
