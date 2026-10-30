using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
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
        var values = await db.InventoryMovementLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && ids.Contains(x.InventoryMovementId))
            .GroupBy(x => x.InventoryMovementId)
            .Select(g => new { Id = g.Key, Count = g.Count(),
                Amount = g.Sum(x => x.Quantity * x.UnitCost), Deleted = g.Any(x => x.IsDeleted) })
            .ToListAsync(ct);
        var valueMap = values.ToDictionary(x => x.Id);
        var links = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.SourceType == "InventoryMovement" &&
                x.SourceId.HasValue && ids.Contains(x.SourceId.Value))
            .GroupBy(x => x.SourceId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count(), Deleted = g.Any(x => x.IsDeleted) })
            .ToListAsync(ct);
        var linkMap = links.ToDictionary(x => x.Id);
        // The register deliberately does not infer a single campus for inter-campus transfers.
        // A campus filter is applied in SQL above; the detail endpoint validates finance evidence.
        var rows = pageRows.Select(m =>
        {
            valueMap.TryGetValue(m.Id, out var v);
            linkMap.TryGetValue(m.Id, out var link);
            return new V3EnterpriseReconciliationRegisterRowDto
            {
                Reference = m.PublicId, Number = m.MovementNumber,
                Date = DateOnly.FromDateTime(m.MovementAt), State = m.StateCode,
                RecordedValue = v?.Amount ?? 0m, LineCount = v?.Count ?? 0,
                EvidenceStatus = v?.Deleted == true || v == null || link?.Deleted == true
                    ? "EvidenceConflict" : link == null ? "NoExplicitReference" :
                    "ExplicitReferencePresent"
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
                x.StateCode, x.TotalAmount, x.JournalId, x.ReversalJournalId }).ToListAsync(ct);
        var ids = pageRows.Select(x => x.Id).ToArray();
        var values = await db.AssetDepreciationLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && ids.Contains(x.AssetDepreciationRunId))
            .GroupBy(x => x.AssetDepreciationRunId)
            .Select(g => new { Id = g.Key, Count = g.Count(),
                Amount = g.Sum(x => x.Amount), Deleted = g.Any(x => x.IsDeleted) })
            .ToListAsync(ct);
        var valueMap = values.ToDictionary(x => x.Id);
        var journalIds = pageRows.SelectMany(x => new[] { x.JournalId, x.ReversalJournalId })
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var journals = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && journalIds.Contains(x.Id))
            .Select(x => new { x.Id, x.IsDeleted, x.CampusId }).ToListAsync(ct);
        var journalMap = journals.ToDictionary(x => x.Id);
        var rows = pageRows.Select(r =>
        {
            valueMap.TryGetValue(r.Id, out var v);
            var journalConflict = r.ReversalJournalId.HasValue && !r.JournalId.HasValue;
            foreach (var id in new[] { r.JournalId, r.ReversalJournalId })
                if (id.HasValue && (!journalMap.TryGetValue(id.Value, out var j) ||
                    j.IsDeleted || (j.CampusId.HasValue && j.CampusId != r.CampusId)))
                    journalConflict = true;
            return new V3EnterpriseReconciliationRegisterRowDto
            {
                Reference = r.ClientRequestId, Number = r.ClientRequestId.ToString("D"),
                Date = r.PeriodEnd, CampusId = r.CampusId, State = r.StateCode,
                RecordedValue = r.TotalAmount, LineCount = v?.Count ?? 0,
                EvidenceStatus = journalConflict || v?.Deleted == true ? "EvidenceConflict" :
                    (v?.Amount ?? 0m) != r.TotalAmount ? "RunLineMismatch" :
                    r.JournalId.HasValue ? "ExplicitReferencePresent" : "NoExplicitReference"
            };
        }).ToArray();
        return Success("asset", from, to, campusId, page, size, count, rows);
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
