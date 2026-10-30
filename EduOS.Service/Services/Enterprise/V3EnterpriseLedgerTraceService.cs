using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Enterprise;

// Reconciles only explicit InventoryMovement -> Journal source references.
// Item-to-Asset matches, purchase order matches and facility consumption are NOT
// financial provenance; those contracts belong to their domain owners.
public sealed class V3EnterpriseLedgerTraceService(EduOSDbContext db, ICurrentUserService user)
{
    public async Task<ApiResponse<V3EnterpriseLedgerTraceDto>> GetAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || !user.IsTenantAdmin || user.TenantId <= 0 ||
            db.CurrentTenantId != user.TenantId)
            return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse("Access denied.", 403);
        if (reference == Guid.Empty)
            return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse("Movement reference required.", 400);

        var movement = await db.InventoryMovements.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted && x.PublicId == reference)
            .Select(x => new {
                x.Id, x.PublicId, x.MovementNumber, x.MovementTypeCode, x.StateCode,
                x.PostedAt, x.SourceType, x.SourceId, x.ReversalOfMovementId
            }).SingleOrDefaultAsync(ct);
        if (movement == null)
            return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse("Movement not found.", 404);

        // Historical tombstones are integrity failures, never silently excluded.
        var lines = db.InventoryMovementLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.InventoryMovementId == movement.Id);
        if (await lines.AnyAsync(x => x.IsDeleted, ct))
            return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse(
                "Deleted stock evidence requires reconciliation.", 409);
        var amount = await lines.GroupBy(x => 1).Select(g => new {
            Count = g.Count(), Value = g.Sum(x => x.Quantity * x.UnitCost)
        }).FirstOrDefaultAsync(ct);

        var linked = db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId &&
                x.SourceType == "InventoryMovement" && x.SourceId == movement.Id);
        if (await linked.AnyAsync(x => x.IsDeleted, ct))
            return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse(
                "Deleted finance evidence requires reconciliation.", 409);
        var journals = await linked.OrderBy(x => x.Id).Take(101)
            .Select(x => new { x.Id, x.PublicId, x.JournalNumber, x.State, x.CampusId })
            .ToListAsync(ct);
        if (journals.Count > 100)
            return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse(
                "Too many explicit journal references; investigate before reporting.", 409);
        var journalIds = journals.Select(x => x.Id).ToArray();
        var journalLines = db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && journalIds.Contains(x.JournalId));
        if (await journalLines.AnyAsync(x => x.IsDeleted, ct))
            return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse(
                "Deleted journal line requires reconciliation.", 409);
        var totals = await journalLines.GroupBy(x => x.JournalId)
            .Select(g => new { JournalId = g.Key, Debit = g.Sum(x => x.DebitAmount),
                Credit = g.Sum(x => x.CreditAmount) }).ToListAsync(ct);
        var byId = totals.ToDictionary(x => x.JournalId);
        var evidence = journals.Select(j => {
            byId.TryGetValue(j.Id, out var t);
            var debit = t?.Debit ?? 0m;
            var credit = t?.Credit ?? 0m;
            return new V3EnterpriseJournalEvidenceDto {
                Reference = j.PublicId, Number = j.JournalNumber, State = j.State.ToString(),
                CampusId = j.CampusId, Debit = debit, Credit = credit,
                Balanced = debit == credit && debit > 0m
            };
        }).ToArray();

        Guid? dispatch = null;
        if (movement.SourceType == "TransferReceipt" && movement.SourceId.HasValue)
            dispatch = await db.InventoryMovements.AsNoTracking()
                .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                    x.Id == movement.SourceId.Value && x.SourceType == "TransferDispatch")
                .Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync(ct);
        Guid? reversal = await db.InventoryMovements.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                x.ReversalOfMovementId == movement.Id && x.StateCode == "Posted")
            .OrderBy(x => x.Id).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync(ct);
        if (movement.ReversalOfMovementId.HasValue)
            reversal = await db.InventoryMovements.AsNoTracking()
                .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                    x.Id == movement.ReversalOfMovementId.Value)
                .Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync(ct);

        var financeStatus = evidence.Length == 0 ? "NoExplicitReference" :
            evidence.Any(x => !x.Balanced) ? "JournalImbalance" :
            journals.Any(x => x.State != JournalState.Posted && x.State != JournalState.Reversed)
                ? "UnpostedJournal" : "ExplicitBalancedReference";
        return ApiResponse<V3EnterpriseLedgerTraceDto>.SuccessResponse(new V3EnterpriseLedgerTraceDto {
            MovementReference = movement.PublicId, MovementNumber = movement.MovementNumber,
            MovementType = movement.MovementTypeCode, State = movement.StateCode,
            PostedAtUtc = movement.PostedAt, SourceType = movement.SourceType, SourceId = movement.SourceId,
            TransferDispatchReference = dispatch, ReversalReference = reversal,
            RecordedMovementValue = amount?.Value ?? 0m, MovementLineCount = amount?.Count ?? 0,
            FinanceEvidenceStatus = financeStatus, Journals = evidence
        });
    }
}
