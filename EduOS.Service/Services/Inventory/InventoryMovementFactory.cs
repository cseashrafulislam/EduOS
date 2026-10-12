using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
namespace EduOS.Service.Services.Inventory;
public sealed partial class InventoryMovementService
{
    private InventoryMovement NewDraft(CreateInventoryMovementRequestDto request, DateTime now)
    {
        var reference = Guid.NewGuid();
        return new InventoryMovement {
            TenantId = user.TenantId, PublicId = reference, ClientRequestId = request.ClientRequestId,
            MovementNumber = "IM-" + reference.ToString("N").ToUpperInvariant(),
            MovementTypeCode = request.MovementTypeCode.Trim(), FromLocationId = request.FromLocationId,
            ToLocationId = request.ToLocationId, SourceType = request.SourceType?.Trim(),
            Remarks = request.Remarks!.Trim(), MovementAt = now, StateCode = "Draft",
            CreatedAt = now, CreatedBy = user.UserId
        };
    }
}