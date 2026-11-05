using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Enterprise;

/// <summary>
/// Read-only cross-module proof of the canonical Facility Parts Finance reversal.
/// A stock contra is linked to its approved GL contra by ClientRequestId ==
/// Journal.PublicId. Neither one alone establishes a completed reversal.
/// </summary>
public static class V3EnterpriseFacilityReversalEvidenceValidator
{
    public sealed record Issue(long MovementId, long? OriginalJournalId, long CampusId,
        long? FromLocationId, long? ToLocationId);

    // Only exceptional/changed states are returned; no entry means the original
    // Finance evidence is still governed by its existing reconciliation rules.
    public static async Task<Dictionary<long, string>> InspectAsync(
        EduOSDbContext db, long tenantId, IReadOnlyList<Issue> issues,
        CancellationToken ct = default)
    {
        var result = new Dictionary<long, string>();
        if (issues.Count == 0) return result;
        if (issues.Count > 50 || issues.Any(x => x.MovementId <= 0 || x.CampusId <= 0) ||
            issues.Select(x => x.MovementId).Distinct().Count() != issues.Count)
            throw new ArgumentException("Inspect at most 50 distinct facility issues.", nameof(issues));

        var movementIds = issues.Select(x => x.MovementId).ToArray();
        var journalIds = issues.Where(x => x.OriginalJournalId.HasValue)
            .Select(x => x.OriginalJournalId!.Value).Distinct().ToArray();

        // Includes tombstones and wrong-tenant references: a normal tenant filter
        // must not turn corrupted evidence into a clean 'not reversed' answer.
        var stockContras = await db.InventoryMovements.IgnoreQueryFilters().AsNoTracking()
            .Where(x => (x.ReversalOfMovementId.HasValue &&
                    movementIds.Contains(x.ReversalOfMovementId.Value)) ||
                (x.SourceType == "MovementReversal" && x.SourceId.HasValue &&
                    movementIds.Contains(x.SourceId.Value)))
            .OrderBy(x => x.Id).Take(201)
            .Select(x => new { x.Id, x.TenantId, x.IsDeleted, x.PublicId,
                x.ClientRequestId, x.SourceType, x.SourceId, x.ReversalOfMovementId,
                x.MovementTypeCode, x.StateCode, x.FromLocationId, x.ToLocationId })
            .ToListAsync(ct);
        var glContras = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.SourceType == "FacilityPartsExpenseReversal" &&
                x.State != JournalState.Rejected && x.State != JournalState.Cancelled &&
                ((x.ReversalOfJournalId.HasValue && journalIds.Contains(x.ReversalOfJournalId.Value)) ||
                 (x.SourceId.HasValue && journalIds.Contains(x.SourceId.Value))))
            .OrderBy(x => x.Id).Take(201)
            .Select(x => new { x.Id, x.TenantId, x.IsDeleted, x.PublicId,
                x.SourceId, x.ReversalOfJournalId, x.CampusId, x.State })
            .ToListAsync(ct);

        // The maximum valid active fan-out is one stock and one Finance contra
        // per issue. Never silently truncate an unexpected fan-out.
        if (stockContras.Count > 200 || glContras.Count > 200)
        {
            foreach (var issue in issues) result[issue.MovementId] = "EvidenceConflict";
            return result;
        }

        var stockLineIds = movementIds.Concat(stockContras.Select(x => x.Id)).Distinct().ToArray();
        var stockLines = await db.InventoryMovementLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => stockLineIds.Contains(x.InventoryMovementId))
            .OrderBy(x => x.InventoryMovementId).ThenBy(x => x.LineNo).Take(20001)
            .Select(x => new { x.InventoryMovementId, x.TenantId, x.IsDeleted,
                x.LineNo, x.InventoryItemId, x.Quantity, x.UnitCost, x.LotOrBatchNo })
            .ToListAsync(ct);
        var financeLineIds = journalIds.Concat(glContras.Select(x => x.Id)).Distinct().ToArray();
        var financeLines = await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => financeLineIds.Contains(x.JournalId))
            .OrderBy(x => x.JournalId).ThenBy(x => x.LineNo).Take(501)
            .Select(x => new { x.JournalId, x.TenantId, x.IsDeleted, x.LineNo,
                x.AccountId, x.CampusId, x.DebitAmount, x.CreditAmount })
            .ToListAsync(ct);
        if (stockLines.Count > 20000 || financeLines.Count > 500)
        {
            foreach (var issue in issues) result[issue.MovementId] = "EvidenceConflict";
            return result;
        }

        var stocksByMovement = stockLines.GroupBy(x => x.InventoryMovementId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var glByJournal = financeLines.GroupBy(x => x.JournalId)
            .ToDictionary(g => g.Key, g => g.ToArray());

        foreach (var issue in issues)
        {
            var stock = stockContras.Where(x => x.ReversalOfMovementId == issue.MovementId ||
                (x.SourceType == "MovementReversal" && x.SourceId == issue.MovementId)).ToArray();
            var gl = glContras.Where(x => issue.OriginalJournalId.HasValue &&
                (x.ReversalOfJournalId == issue.OriginalJournalId ||
                 x.SourceId == issue.OriginalJournalId)).ToArray();
            if (stock.Length == 0 && gl.Length == 0) continue;
            if (stock.Length > 1 || gl.Length != 1 || !issue.OriginalJournalId.HasValue)
            {
                result[issue.MovementId] = "EvidenceConflict";
                continue;
            }

            var contra = gl[0];
            if (contra.TenantId != tenantId || contra.IsDeleted ||
                contra.SourceId != issue.OriginalJournalId ||
                contra.ReversalOfJournalId != issue.OriginalJournalId ||
                contra.CampusId != issue.CampusId ||
                !glByJournal.TryGetValue(issue.OriginalJournalId.Value, out var originalGl) ||
                !glByJournal.TryGetValue(contra.Id, out var reversalGl) ||
                originalGl.Length != 2 || reversalGl.Length != 2 ||
                originalGl.Any(x => x.TenantId != tenantId || x.IsDeleted || x.CampusId != issue.CampusId) ||
                reversalGl.Any(x => x.TenantId != tenantId || x.IsDeleted || x.CampusId != issue.CampusId) ||
                originalGl.Zip(reversalGl, (a, b) => a.LineNo == b.LineNo &&
                    a.AccountId == b.AccountId && a.DebitAmount == b.CreditAmount &&
                    a.CreditAmount == b.DebitAmount).Any(match => !match))
            {
                result[issue.MovementId] = "EvidenceConflict";
                continue;
            }

            if (contra.State == JournalState.Submitted && stock.Length == 0)
            {
                result[issue.MovementId] = "ReversalPending";
                continue;
            }
            if (contra.State != JournalState.Posted || stock.Length != 1)
            {
                result[issue.MovementId] = "EvidenceConflict";
                continue;
            }

            var reversal = stock[0];
            if (reversal.TenantId != tenantId || reversal.IsDeleted ||
                reversal.StateCode != "Posted" || reversal.SourceType != "MovementReversal" ||
                reversal.SourceId != issue.MovementId ||
                reversal.ReversalOfMovementId != issue.MovementId ||
                reversal.MovementTypeCode != "ReversalIssue" ||
                reversal.ClientRequestId != contra.PublicId ||
                reversal.FromLocationId != issue.FromLocationId ||
                reversal.ToLocationId != issue.ToLocationId ||
                !stocksByMovement.TryGetValue(issue.MovementId, out var originalStock) ||
                !stocksByMovement.TryGetValue(reversal.Id, out var reversedStock) ||
                originalStock.Length is < 1 or > 100 || originalStock.Length != reversedStock.Length ||
                originalStock.Any(x => x.TenantId != tenantId || x.IsDeleted || x.Quantity <= 0m || x.UnitCost < 0m) ||
                reversedStock.Any(x => x.TenantId != tenantId || x.IsDeleted) ||
                originalStock.Zip(reversedStock, (a, b) => a.LineNo == b.LineNo &&
                    a.InventoryItemId == b.InventoryItemId && a.Quantity == b.Quantity &&
                    a.UnitCost == b.UnitCost && a.LotOrBatchNo == b.LotOrBatchNo).Any(match => !match))
            {
                result[issue.MovementId] = "EvidenceConflict";
                continue;
            }
            result[issue.MovementId] = "ExplicitReversedReference";
        }
        return result;
    }
}
