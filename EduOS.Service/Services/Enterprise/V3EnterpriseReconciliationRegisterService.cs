using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Enterprise;

// Tenant-admin read model only. Each page uses bounded projections and batched
// evidence queries; no shadow stock balance, GL, inferred PO or facility lineage.
public sealed class V3EnterpriseReconciliationRegisterService(
    EduOSDbContext db, ICurrentUserService user)
{
    public async Task<ApiResponse<V3EnterpriseReconciliationRegisterDto>> GetAsync(
        string kind, DateOnly from, DateOnly to, long? campusId = null,
        int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || !user.IsTenantAdmin || user.TenantId <= 0 ||
            db.CurrentTenantId != user.TenantId)
            return ApiResponse<V3EnterpriseReconciliationRegisterDto>.ErrorResponse("Access denied.", 403);
        if (kind != "stock" && kind != "asset")
            return ApiResponse<V3EnterpriseReconciliationRegisterDto>.ErrorResponse("Invalid evidence type.", 400);
        if (from > to || to.DayNumber - from.DayNumber > 366 ||
            page < 1 || page > 10000 || pageSize < 1 || pageSize > 50)
            return ApiResponse<V3EnterpriseReconciliationRegisterDto>.ErrorResponse(
                "Choose a date range up to 366 days and a page size of 1 to 50.", 400);
        if (campusId.HasValue && (campusId.Value <= 0 ||
            !await db.Campuses.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && !x.IsDeleted && x.Id == campusId.Value, ct)))
            return ApiResponse<V3EnterpriseReconciliationRegisterDto>.ErrorResponse("Campus not found.", 404);

        return kind == "stock"
            ? await StockAsync(from, to, campusId, page, pageSize, ct)
            : await AssetAsync(from, to, campusId, page, pageSize, ct);
    }

    private async Task<ApiResponse<V3EnterpriseReconciliationRegisterDto>> StockAsync(
        DateOnly from, DateOnly to, long? campusId, int page, int size, CancellationToken ct)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var endExclusive = to.ToDateTime(TimeOnly.MaxValue);
        var query = db.InventoryMovements.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                x.MovementAt >= start && x.MovementAt <= endExclusive);
        if (campusId.HasValue)
            query = query.Where(x => db.InventoryLocations.Any(loc =>
                loc.TenantId == user.TenantId && !loc.IsDeleted &&
                loc.CampusId == campusId.Value &&
                (loc.Id == x.FromLocationId || loc.Id == x.ToLocationId)));
        var count = await query.CountAsync(ct);
        var pageRows = await query.OrderByDescending(x => x.MovementAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(x => new { x.Id, x.PublicId, x.MovementNumber, x.MovementAt,
                x.StateCode, x.FromLocationId, x.ToLocationId }).ToListAsync(ct);
        var ids = pageRows.Select(x => x.Id).ToArray();
        // Resolve only the at-most 100 location endpoints on this report page.
        // Missing or soft-deleted locations are historical integrity conflicts.
        var locationIds = pageRows.SelectMany(x => new[] { x.FromLocationId, x.ToLocationId })
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        var locationMap = await db.InventoryLocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && locationIds.Contains(x.Id))
            .Select(x => new { x.Id, x.CampusId, x.IsDeleted })
            .ToDictionaryAsync(x => x.Id, ct);
        var values = await db.InventoryMovementLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && ids.Contains(x.InventoryMovementId))
            .GroupBy(x => x.InventoryMovementId)
            .Select(g => new { Id = g.Key, Count = g.Count(),
                Amount = g.Sum(x => x.Quantity * x.UnitCost), Deleted = g.Any(x => x.IsDeleted) })
            .ToListAsync(ct);
        var valueMap = values.ToDictionary(x => x.Id);
        // Read canonical journal rows in one bounded batch, not a per-movement N+1.
        // A corrupt fan-out must fail closed rather than silently truncate evidence.
        var journals = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.SourceType == "InventoryMovement" &&
                x.SourceId.HasValue && ids.Contains(x.SourceId.Value))
            .OrderBy(x => x.Id).Take(5001)
            .Select(x => new { x.Id, SourceId = x.SourceId!.Value, x.IsDeleted, x.State, x.CampusId })
            .ToListAsync(ct);
        if (journals.Count > 5000)
            return ApiResponse<V3EnterpriseReconciliationRegisterDto>.ErrorResponse(
                "Excessive journal fan-out requires investigation.", 409);
        var journalTotals = await LoadJournalTotalsAsync(journals.Select(x => x.Id).ToArray(), ct);
        var linkMap = journals.GroupBy(x => x.SourceId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        // The register deliberately does not infer a single campus for inter-campus transfers.
        // A campus filter is applied in SQL above; the detail endpoint validates finance evidence.
        var rows = pageRows.Select(m =>
        {
            valueMap.TryGetValue(m.Id, out var v);
            linkMap.TryGetValue(m.Id, out var link);
            var endpoints = new[] { m.FromLocationId, m.ToLocationId }
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
            var missingLocation = endpoints.Any(id =>
                !locationMap.TryGetValue(id, out var loc) || loc.IsDeleted);
            var allowedCampuses = endpoints.Where(id =>
                locationMap.TryGetValue(id, out var loc) && loc.CampusId.HasValue)
                .Select(id => locationMap[id].CampusId!.Value).ToHashSet();
            var foreignCampusJournal = allowedCampuses.Count > 0 && link != null &&
                link.Any(j => j.CampusId.HasValue &&
                    !allowedCampuses.Contains(j.CampusId.Value));
            var status = missingLocation || foreignCampusJournal ||
                (link?.Length ?? 0) > 100 ||
                v?.Deleted == true || v == null ? "EvidenceConflict" :
                link == null ? "NoExplicitReference" :
                link.Any(j => j.IsDeleted ||
                    (journalTotals.TryGetValue(j.Id, out var t) && t.Deleted))
                    ? "EvidenceConflict" :
                link.Any(j => !journalTotals.TryGetValue(j.Id, out var t) ||
                    t.Debit <= 0m || t.Debit != t.Credit)
                    ? "JournalImbalance" :
                link.Any(j => j.State != JournalState.Posted && j.State != JournalState.Reversed)
                    ? "UnpostedJournal" : "ExplicitBalancedReference";
            return new V3EnterpriseReconciliationRegisterRowDto
            {
                Reference = m.PublicId, Number = m.MovementNumber,
                Date = DateOnly.FromDateTime(m.MovementAt), State = m.StateCode,
                RecordedValue = v?.Amount ?? 0m, LineCount = v?.Count ?? 0,
                EvidenceStatus = status
            };
        }).ToArray();
        return Success("stock", from, to, campusId, page, size, count, rows);
    }

    private async Task<ApiResponse<V3EnterpriseReconciliationRegisterDto>> AssetAsync(
        DateOnly from, DateOnly to, long? campusId, int page, int size, CancellationToken ct)
    {
        var query = db.AssetDepreciationRuns.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                x.PeriodEnd >= from && x.PeriodEnd <= to);
        if (campusId.HasValue)
            query = query.Where(x => x.CampusId == campusId.Value);
        var count = await query.CountAsync(ct);
        var pageRows = await query.OrderByDescending(x => x.PeriodEnd).ThenByDescending(x => x.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(x => new { x.Id, x.ClientRequestId, x.CampusId, x.PeriodEnd,
                x.StateCode, x.TotalAmount, x.AccountingPeriodId,
                x.JournalId, x.ReversalJournalId }).ToListAsync(ct);
        var ids = pageRows.Select(x => x.Id).ToArray();
        var values = await db.AssetDepreciationLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && ids.Contains(x.AssetDepreciationRunId))
            .GroupBy(x => x.AssetDepreciationRunId)
            .Select(g => new { Id = g.Key, Count = g.Count(),
                Amount = g.Sum(x => x.Amount), Deleted = g.Any(x => x.IsDeleted) })
            .ToListAsync(ct);
        var valueMap = values.ToDictionary(x => x.Id);
        // A balanced journal cannot validate a depreciation line belonging to a
        // different campus/book or accounting period. Keep this check in SQL:
        // fetching all depreciation lines for 50 runs would be unbounded.
        var invalidLineRunIds = await (
            from line in db.AssetDepreciationLines.IgnoreQueryFilters().AsNoTracking()
            join run in db.AssetDepreciationRuns.AsNoTracking()
                on line.AssetDepreciationRunId equals run.Id
            where line.TenantId == user.TenantId && run.TenantId == user.TenantId &&
                ids.Contains(line.AssetDepreciationRunId) &&
                (line.PeriodEnd != run.PeriodEnd ||
                 !db.AssetBooks.IgnoreQueryFilters().Any(book =>
                     book.TenantId == user.TenantId && !book.IsDeleted &&
                     book.Id == line.AssetBookId && book.CampusId == run.CampusId))
            select line.AssetDepreciationRunId).Distinct().ToListAsync(ct);
        var invalidLineRuns = invalidLineRunIds.ToHashSet();
        var journalIds = pageRows.SelectMany(x => new[] { x.JournalId, x.ReversalJournalId })
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var journals = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && journalIds.Contains(x.Id))
            .Select(x => new { x.Id, x.IsDeleted, x.CampusId, x.State,
                x.AccountingPeriodId, x.ReversalOfJournalId }).ToListAsync(ct);
        var journalMap = journals.ToDictionary(x => x.Id);
        var journalTotals = await LoadJournalTotalsAsync(journalIds, ct);
        var rows = pageRows.Select(r =>
        {
            valueMap.TryGetValue(r.Id, out var v);
            var journalConflict = r.ReversalJournalId.HasValue && !r.JournalId.HasValue;
            foreach (var id in new[] { r.JournalId, r.ReversalJournalId })
                if (id.HasValue && (!journalMap.TryGetValue(id.Value, out var j) ||
                    j.IsDeleted || (j.CampusId.HasValue && j.CampusId != r.CampusId)))
                    journalConflict = true;
            if (r.JournalId.HasValue &&
                journalMap.TryGetValue(r.JournalId.Value, out var original) &&
                original.AccountingPeriodId != r.AccountingPeriodId)
                journalConflict = true;
            if (r.ReversalJournalId.HasValue &&
                journalMap.TryGetValue(r.ReversalJournalId.Value, out var reversal) &&
                reversal.ReversalOfJournalId != r.JournalId)
                journalConflict = true;
            // Never allow another run on the same page to contaminate this row.
            var runJournalIds = new[] { r.JournalId, r.ReversalJournalId }
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
            // Historical tombstones are integrity conflicts even when run totals
            // also differ; do not downgrade the detail endpoint's 409 evidence.
            var status = journalConflict || invalidLineRuns.Contains(r.Id) ||
                v?.Deleted == true ||
                runJournalIds.Any(id => journalTotals.TryGetValue(id, out var t) && t.Deleted)
                    ? "EvidenceConflict" :
                (v?.Amount ?? 0m) != r.TotalAmount ? "RunLineMismatch" :
                runJournalIds.Any(id => !journalTotals.TryGetValue(id, out var t) ||
                    t.Debit <= 0m || t.Debit != t.Credit)
                    ? "JournalImbalance" :
                runJournalIds.Any(id => journalMap.TryGetValue(id, out var j) &&
                    j.State != JournalState.Posted && j.State != JournalState.Reversed)
                    ? "UnpostedJournal" :
                r.JournalId.HasValue ? "ExplicitBalancedReference" : "NoExplicitReference";
            return new V3EnterpriseReconciliationRegisterRowDto
            {
                Reference = r.ClientRequestId, Number = r.ClientRequestId.ToString("D"),
                Date = r.PeriodEnd, CampusId = r.CampusId, State = r.StateCode,
                RecordedValue = r.TotalAmount, LineCount = v?.Count ?? 0,
                EvidenceStatus = status
            };
        }).ToArray();
        return Success("asset", from, to, campusId, page, size, count, rows);
    }

    private sealed record JournalLineTotals(decimal Debit, decimal Credit, bool Deleted);

    // One tenant-filtered SQL aggregate for the current page, including tombstones.
    // Never use the normal soft-delete filter for historical finance evidence.
    private async Task<Dictionary<long, JournalLineTotals>> LoadJournalTotalsAsync(
        long[] journalIds, CancellationToken ct)
    {
        if (journalIds.Length == 0) return [];
        var totals = await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && journalIds.Contains(x.JournalId))
            .GroupBy(x => x.JournalId)
            .Select(g => new { Id = g.Key, Debit = g.Sum(x => x.DebitAmount),
                Credit = g.Sum(x => x.CreditAmount), Deleted = g.Any(x => x.IsDeleted) })
            .ToListAsync(ct);
        return totals.ToDictionary(x => x.Id,
            x => new JournalLineTotals(x.Debit, x.Credit, x.Deleted));
    }

    private static ApiResponse<V3EnterpriseReconciliationRegisterDto> Success(
        string kind, DateOnly from, DateOnly to, long? campusId,
        int page, int size, int count, V3EnterpriseReconciliationRegisterRowDto[] rows)
        => ApiResponse<V3EnterpriseReconciliationRegisterDto>.SuccessResponse(
            new V3EnterpriseReconciliationRegisterDto
            {
                Kind = kind, From = from, To = to, CampusId = campusId,
                Page = page, PageSize = size, TotalCount = count, Items = rows
            });
}
