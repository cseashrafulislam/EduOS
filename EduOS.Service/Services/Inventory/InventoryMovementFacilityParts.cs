using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Facilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

public sealed partial class InventoryMovementService
{
    // The work-order source is resolved on the server. Clients cannot create a
    // FacilityWorkOrder issue through the generic manual-movement endpoint.
    public async Task<ApiResponse<InventoryMovementDto>> CreateFacilityPartsIssueAsync(
        long facilityRequestId, CreateFacilityPartsIssueDto request, CancellationToken ct = default)
    {
        if (!Writable || db.CurrentTenantId != user.TenantId) return Denied<InventoryMovementDto>();
        if (modules != null && !await modules.IsCurrentTenantModuleAvailableAsync("FACILITIES"))
            return Denied<InventoryMovementDto>();
        if (facilityRequestId <= 0 || request == null || request.ClientRequestId == Guid.Empty ||
            request.FromLocationId <= 0 || request.Lines == null || request.Lines.Count is < 1 or > 100 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 1000)
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Valid request, location, 1-100 parts and reason (10-1000 characters) required.");

        var normalized = new CreateInventoryMovementRequestDto {
            ClientRequestId = request.ClientRequestId, FromLocationId = request.FromLocationId,
            MovementTypeCode = "Issue", SourceType = "ManualIssue", Remarks = request.Reason.Trim(),
            Lines = request.Lines.Select(x => new CreateInventoryMovementLineRequestDto {
                InventoryItemId = x.InventoryItemId, Quantity = x.Quantity, UnitCost = 0m,
                LotOrBatchNo = x.LotOrBatchNo?.Trim() ?? ""
            }).ToArray()
        };
        var validation = ValidateCreate(normalized);
        if (validation != null) return ApiResponse<InventoryMovementDto>.ErrorResponse(validation);
        if (!await LocationsAllowed(request.FromLocationId, null, ct)) return Denied<InventoryMovementDto>();

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var source = await (from w in db.FacilityWorkOrders
                join r in db.FacilityServiceRequests on w.FacilityServiceRequestId equals r.Id
                where w.TenantId == user.TenantId && r.TenantId == user.TenantId &&
                    w.FacilityServiceRequestId == facilityRequestId && !w.IsDeleted && !r.IsDeleted
                select new { Order = w, Request = r }).FirstOrDefaultAsync(ct);
            if (source == null) return ApiResponse<InventoryMovementDto>.ErrorResponse("Work order not found.", 404);
            var campusAllowed = AllCampuses || await db.UserCampusAccesses.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.UserId == user.UserId && x.CampusId == source.Request.CampusId &&
                x.IsActive && !x.IsDeleted, ct);
            var locationAllowed = await db.InventoryLocations.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.Id == request.FromLocationId && x.IsActive && !x.IsDeleted &&
                (x.CampusId == null || x.CampusId == source.Request.CampusId), ct);
            if (!campusAllowed || !locationAllowed) return Denied<InventoryMovementDto>();

            normalized.SourceType = "FacilityWorkOrder";
            normalized.SourceId = source.Order.Id;
            var existing = await ExistingCreateAsync(normalized, ct);
            if (existing != null) return existing;
            if (source.Request.StateCode != "Approved" || source.Order.StateCode is not
                ("Approved" or "Scheduled" or "InProgress" or "Paused"))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Work order is not active for parts issue.", 409);

            var ids = normalized.Lines.Select(x => x.InventoryItemId).Distinct().ToArray();
            if (await db.InventoryItems.AsNoTracking().CountAsync(x => x.TenantId == user.TenantId &&
                ids.Contains(x.Id) && x.IsActive && x.IsStockTracked && !x.IsDeleted, ct) != ids.Length)
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Invalid stock item.", 400);
            var (movement, lines) = await PersistDraftAsync(normalized, ct);
            movement.SourceId = source.Order.Id;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InventoryMovementDto>.SuccessResponse(Map(movement, lines), "Facility parts issue drafted for independent inventory posting.");
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222 or 2601 or 2627 or 3960)
        { logger.LogWarning(ex, "Facility parts issue conflict"); return ApiResponse<InventoryMovementDto>.ErrorResponse("Concurrent parts issue; reload or retry with the same request ID.", 409); }
        catch (DbUpdateException ex)
        { logger.LogWarning(ex, "Facility parts issue persistence conflict"); return ApiResponse<InventoryMovementDto>.ErrorResponse("Concurrent parts issue; reload.", 409); }
    }

    // Called inside the caller's serializable posting transaction. Every facility
    // issue rechecks the live source, location, campus grant and maker-checker.
    private async Task<bool> FacilityIssuePostingAllowedAsync(EduOS.Core.Entities.Inventory.InventoryMovement movement, CancellationToken ct)
    {
        if (movement.SourceType != "FacilityWorkOrder") return true;
        if (modules != null && !await modules.IsCurrentTenantModuleAvailableAsync("FACILITIES")) return false;
        if (movement.MovementTypeCode != "Issue" || movement.ToLocationId.HasValue ||
            movement.SourceId is not > 0 || movement.FromLocationId is not > 0 ||
            movement.StateCode != "Draft" || !movement.CreatedBy.HasValue ||
            movement.CreatedBy.Value == user.UserId) return false;
        return await (from w in db.FacilityWorkOrders.AsNoTracking()
            join r in db.FacilityServiceRequests.AsNoTracking() on w.FacilityServiceRequestId equals r.Id
            join l in db.InventoryLocations.AsNoTracking() on movement.FromLocationId.Value equals l.Id
            where w.TenantId == user.TenantId && r.TenantId == user.TenantId &&
                l.TenantId == user.TenantId && w.Id == movement.SourceId && !w.IsDeleted && !r.IsDeleted &&
                r.StateCode == "Approved" && (w.StateCode == "Approved" || w.StateCode == "Scheduled" ||
                    w.StateCode == "InProgress" || w.StateCode == "Paused") && l.IsActive && !l.IsDeleted &&
                (l.CampusId == null || l.CampusId == r.CampusId) &&
                (AllCampuses || db.UserCampusAccesses.Any(a => a.TenantId == user.TenantId &&
                    a.UserId == user.UserId && a.CampusId == r.CampusId && a.IsActive && !a.IsDeleted))
            select w.Id).AnyAsync(ct);
    }

    public async Task<ApiResponse<InventoryMovementDto>> VoidFacilityPartsIssueAsync(
        Guid movementReference, VoidFacilityPartsIssueDto request, CancellationToken ct = default)
    {
        if (!Writable || db.CurrentTenantId != user.TenantId) return Denied<InventoryMovementDto>();
        if (modules != null && !await modules.IsCurrentTenantModuleAvailableAsync("FACILITIES"))
            return Denied<InventoryMovementDto>();
        if (movementReference == Guid.Empty || request == null || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length is < 10 or > 1000)
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Movement, row version and void reason (10-1000 characters) required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var movement = await db.InventoryMovements.FirstOrDefaultAsync(m => m.TenantId == user.TenantId &&
                m.PublicId == movementReference && m.SourceType == "FacilityWorkOrder" && !m.IsDeleted, ct);
            if (movement == null || !await LocationsAllowed(movement.FromLocationId, movement.ToLocationId, ct))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Facility movement not found.", 404);
            if (movement.CreatedBy != user.UserId && !Postable) return Denied<InventoryMovementDto>();
            if (movement.StateCode == "Voided")
                return ApiResponse<InventoryMovementDto>.SuccessResponse(await MapAsync(movement, ct), "Already voided.");
            if (movement.StateCode != "Draft" || !Version(movement.RowVersion, request.RowVersion))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Only the current unposted draft can be voided; reverse posted stock instead.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            movement.StateCode = "Voided"; movement.UpdatedAt = now; movement.UpdatedBy = user.UserId;
            var annotation = (movement.Remarks ?? "") + " | Voided: " + request.Reason.Trim();
            movement.Remarks = annotation[..Math.Min(annotation.Length, 1000)];
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InventoryMovementDto>.SuccessResponse(await MapAsync(movement, ct), "Unused facility parts draft voided.");
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222 or 3960)
        { logger.LogWarning(ex, "Facility draft void SQL conflict"); return ApiResponse<InventoryMovementDto>.ErrorResponse("Void conflict; reload.", 409); }
        catch (DbUpdateException ex)
        { logger.LogWarning(ex, "Facility draft void conflict"); return ApiResponse<InventoryMovementDto>.ErrorResponse("Void conflict; reload.", 409); }
    }
}
