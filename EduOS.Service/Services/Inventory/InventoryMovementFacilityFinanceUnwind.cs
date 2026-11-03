using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

public sealed partial class InventoryMovementService
{
    /// <summary>Called only inside the independent Finance review transaction.
    /// The caller posts the exact GL contra before committing this stock reversal.</summary>
    internal async Task<(InventoryMovement? Reversal, string? Error)> ReverseFacilityIssueInTransactionAsync(
        long movementId, Guid commandId, string reason, CancellationToken ct)
    {
        if (!Reversible || db.CurrentTenantId != user.TenantId || db.Database.CurrentTransaction == null ||
            movementId <= 0 || commandId == Guid.Empty || string.IsNullOrWhiteSpace(reason) ||
            reason.Trim().Length is < 10 or > 1000)
            return (null, "Independent Finance/Inventory reversal authority required.");
        var original = await db.InventoryMovements.FirstOrDefaultAsync(m => m.TenantId == user.TenantId &&
            m.Id == movementId && m.SourceType == "FacilityWorkOrder" && m.MovementTypeCode == "Issue" &&
            m.StateCode == "Posted" && !m.IsDeleted && m.ReversalOfMovementId == null, ct);
        if (original == null || original.FromLocationId is null or <= 0 ||
            !await LocationsAllowed(original.FromLocationId, null, ct) ||
            await db.InventoryMovements.AnyAsync(m => m.TenantId == user.TenantId &&
                m.ReversalOfMovementId == movementId && !m.IsDeleted, ct))
            return (null, "Stock issue is invalid, out of scope or already corrected.");
        var approvedFinanceSource = await (from reversal in db.Journals.AsNoTracking()
            join posted in db.Journals.AsNoTracking() on reversal.ReversalOfJournalId equals (long?)posted.Id
            where reversal.TenantId == user.TenantId && posted.TenantId == user.TenantId &&
                reversal.PublicId == commandId && !reversal.IsDeleted && !posted.IsDeleted &&
                reversal.SourceType == "FacilityPartsExpenseReversal" && reversal.State == EduOS.Core.Enums.Domain.JournalState.Submitted &&
                reversal.CreatedBy != user.UserId && reversal.SourceId == posted.Id &&
                posted.SourceType == "InventoryMovement" && posted.SourceId == movementId &&
                posted.State == EduOS.Core.Enums.Domain.JournalState.Posted
            select reversal.Id).AnyAsync(ct);
        if (!approvedFinanceSource) return (null, "Submitted independent Finance reversal must match this posted stock issue.");
        var (error, lines) = await CheckReversalAsync(original, ct);
        if (error != null) return (null, error);
        var (reversal, _) = await PersistReversalAsync(original, lines, commandId, reason.Trim(), ct);
        return (reversal, null);
    }
}
