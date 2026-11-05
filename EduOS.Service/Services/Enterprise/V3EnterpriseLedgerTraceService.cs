using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Enterprise;

// Reconciles only explicit InventoryMovement -> Journal source references.
// Item-to-Asset matches are NOT financial provenance; only the canonical domain
// source and explicit InventoryMovement -> Journal link can be trusted.
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
                x.PostedAt, x.SourceType, x.SourceId, x.ReversalOfMovementId,
                x.CreatedBy, x.PostedByUserId,
                x.FromLocationId, x.ToLocationId
            }).SingleOrDefaultAsync(ct);
        if (movement == null)
            return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse("Movement not found.", 404);

        // Resolve both endpoints against the authoritative tenant's locations.
        // Inter-campus transfers may legitimately have two different campuses.
        var locationIds = new[] { movement.FromLocationId, movement.ToLocationId }
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        var locations = await db.InventoryLocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && locationIds.Contains(x.Id))
            .Select(x => new { x.Id, x.CampusId, x.IsDeleted }).ToListAsync(ct);
        if (locations.Count != locationIds.Length || locations.Any(x => x.IsDeleted))
            return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse(
                "Missing, deleted or cross-tenant stock location requires reconciliation.", 409);
        var allowedCampuses = locations.Where(x => x.CampusId.HasValue)
            .Select(x => x.CampusId!.Value).ToHashSet();

        // Verify the real canonical dispatch and exact line mirror, not an
        // inferred purchase-order, asset or Finance reference.
        if (movement.SourceType == "TransferReceipt" &&
            (await V3EnterpriseTransferEvidenceValidator.InvalidReceiptsAsync(
                db, user.TenantId,
                [new V3EnterpriseTransferEvidenceValidator.Receipt(
                    movement.Id, movement.SourceId, movement.ToLocationId,
                    movement.StateCode, movement.MovementTypeCode)], ct))
                .Contains(movement.Id))
            return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse(
                "Transfer dispatch or receipt evidence requires reconciliation.", 409);

        V3EnterpriseProcurementReceiptEvidenceValidator.Source? procurementSource = null;
        if (movement.SourceType == "ProcurementGoodsReceipt")
        {
            var receiptEvidence = await V3EnterpriseProcurementReceiptEvidenceValidator.InspectAsync(
                db, user.TenantId,
                [new V3EnterpriseProcurementReceiptEvidenceValidator.Movement(
                    movement.Id, movement.SourceId, movement.FromLocationId,
                    movement.ToLocationId, movement.StateCode, movement.MovementTypeCode)], ct);
            if (!receiptEvidence.Verified.TryGetValue(movement.Id, out procurementSource))
                return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse(
                    "Canonical procurement receipt, purchase order or posted stock evidence requires reconciliation.", 409);
        }
        var verifiedFacility = false;
        long? facilityCampus = null;
        if (movement.SourceType == "FacilityWorkOrder")
        {
            var facility = await V3EnterpriseFacilityEvidenceValidator.InspectAsync(
                db, user.TenantId,
                [new V3EnterpriseFacilityEvidenceValidator.Movement(
                    movement.Id, movement.SourceId, movement.FromLocationId,
                    movement.ToLocationId, movement.StateCode, movement.MovementTypeCode,
                    movement.CreatedBy, movement.PostedByUserId)], ct);
            if (!facility.Verified.Contains(movement.Id))
                return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse(
                    "Facility work order, campus or posted stock evidence requires reconciliation.", 409);
            verifiedFacility = true;
            facilityCampus = facility.CampusByMovement[movement.Id];
        }

        // Historical tombstones are integrity failures, never silently excluded.
        var lines = db.InventoryMovementLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.InventoryMovementId == movement.Id);
        // Foreign-tenant lines linked to this movement are corruption, not absent evidence.
        if (await db.InventoryMovementLines.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.InventoryMovementId == movement.Id &&
                x.TenantId != user.TenantId, ct))
            return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse(
                "Cross-tenant stock line evidence requires reconciliation.", 409);
        if (await lines.AnyAsync(x => x.IsDeleted, ct))
            return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse(
                "Deleted stock evidence requires reconciliation.", 409);
        var amount = await lines.GroupBy(x => 1).Select(g => new {
            Count = g.Count(), Value = g.Sum(x => x.Quantity * x.UnitCost)
        }).FirstOrDefaultAsync(ct);

        if (amount == null && movement.StateCode == "Posted")
            return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse(
                "Posted movement has no line evidence.", 409);

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
        if (allowedCampuses.Count > 0 && journals.Any(x =>
            !x.CampusId.HasValue || !allowedCampuses.Contains(x.CampusId.Value)))
            return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse(
                "Journal campus does not match either stock location.", 409);
        if (verifiedFacility && journals.Any(x => x.CampusId != facilityCampus))
            return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse(
                "Finance journal campus differs from the facility request campus.", 409);
        var journalIds = journals.Select(x => x.Id).ToArray();
        var journalLines = db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && journalIds.Contains(x.JournalId));
        if (await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => journalIds.Contains(x.JournalId) &&
                x.TenantId != user.TenantId, ct))
            return ApiResponse<V3EnterpriseLedgerTraceDto>.ErrorResponse(
                "Cross-tenant journal line evidence requires reconciliation.", 409);
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

        // Facility Finance recognizes the posted issue's rounded stock cost.
        // A balanced but wrong-value or multiply posted GL is not reconciled.
        // Rejected attempts remain in history, but are not active postings.
        var facilityActive = verifiedFacility ? journals.Where(x =>
            x.State != JournalState.Rejected && x.State != JournalState.Cancelled).ToArray() : [];
        var facilityValue = decimal.Round(amount?.Value ?? 0m, 2, MidpointRounding.AwayFromZero);
        var facilityAmountMismatch = verifiedFacility && facilityActive.Any(x =>
            x.State == JournalState.Posted &&
            (!byId.TryGetValue(x.Id, out var total) || total.Debit != facilityValue));
        var financeStatus = evidence.Length == 0 ||
            (verifiedFacility && facilityActive.Length == 0) ? "NoExplicitReference" :
            evidence.Any(x => !x.Balanced) ? "JournalImbalance" :
            verifiedFacility && facilityActive.Length > 1 ? "EvidenceConflict" :
            (verifiedFacility ? facilityActive : journals.ToArray()).Any(x =>
                x.State != JournalState.Posted && x.State != JournalState.Reversed)
                ? "UnpostedJournal" :
            facilityAmountMismatch ? "JournalAmountMismatch" : "ExplicitBalancedReference";
        if (verifiedFacility)
        {
            var originalJournalId = facilityActive.Length == 1
                ? (long?)facilityActive[0].Id : null;
            var reversalEvidence = await V3EnterpriseFacilityReversalEvidenceValidator.InspectAsync(
                db, user.TenantId,
                [new V3EnterpriseFacilityReversalEvidenceValidator.Issue(
                    movement.Id, originalJournalId, facilityCampus!.Value,
                    movement.FromLocationId, movement.ToLocationId)], ct);
            if (reversalEvidence.TryGetValue(movement.Id, out var reversalStatus))
            {
                if (reversalStatus == "EvidenceConflict") financeStatus = reversalStatus;
                else if (financeStatus == "ExplicitBalancedReference") financeStatus = reversalStatus;
            }
        }
        return ApiResponse<V3EnterpriseLedgerTraceDto>.SuccessResponse(new V3EnterpriseLedgerTraceDto {
            MovementReference = movement.PublicId, MovementNumber = movement.MovementNumber,
            MovementType = movement.MovementTypeCode, State = movement.StateCode,
            PostedAtUtc = movement.PostedAt, SourceType = movement.SourceType, SourceId = movement.SourceId,
            TransferDispatchReference = dispatch, ReversalReference = reversal,
            RecordedMovementValue = amount?.Value ?? 0m, MovementLineCount = amount?.Count ?? 0,
            FinanceEvidenceStatus = financeStatus,
            SourceEvidenceStatus = procurementSource != null ? "VerifiedProcurementReceipt" :
                verifiedFacility ? "VerifiedFacilityWorkOrder" : "NotApplicable",
            ProcurementGoodsReceiptReference = procurementSource?.GoodsReceiptReference,
            ProcurementPurchaseOrderReference = procurementSource?.PurchaseOrderReference,
            Journals = evidence
        });
    }
}
