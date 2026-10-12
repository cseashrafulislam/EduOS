using EduOS.Core.DTOs.Inventory;
namespace EduOS.Service.Services.Inventory;
public sealed partial class InventoryMovementService
{
    private static string? ValidateCreate(CreateInventoryMovementRequestDto? request)
    {
        if (request == null || request.ClientRequestId == Guid.Empty || request.Lines == null ||
            request.Lines.Count is < 1 or > 100 || string.IsNullOrWhiteSpace(request.Remarks) ||
            request.Remarks.Trim().Length is < 10 or > 1000)
            return "Unique request ID, 1-100 lines and reason (10-1000 characters) required.";
        var type = request.MovementTypeCode?.Trim();
        var source = request.SourceType?.Trim();
        var sourceAllowed = (type == "Receipt" && (source == "ManualReceipt" || source == "OpeningBalance")) ||
            (type == "Issue" && source == "ManualIssue") || (type == "Transfer" && source == "ManualTransfer");
        if (!sourceAllowed || request.SourceId.HasValue ||
            (type == "Receipt" && (request.ToLocationId is null or <= 0 || request.FromLocationId.HasValue)) ||
            (type == "Issue" && (request.FromLocationId is null or <= 0 || request.ToLocationId.HasValue)) ||
            (type == "Transfer" && (request.FromLocationId is null or <= 0 || request.ToLocationId is null or <= 0 ||
                request.FromLocationId == request.ToLocationId)))
            return "Invalid movement location/source; linked procurement documents require verified integration.";
        if (request.Lines.Any(x => x.InventoryItemId <= 0 || x.Quantity <= 0m || x.Quantity > 999999999999m ||
            x.UnitCost < 0m || x.UnitCost > 999999999999m || !Precision(x.Quantity, 6) ||
            !Precision(x.UnitCost, 4) || x.LotOrBatchNo?.Trim().Length > 100))
            return "Invalid item, quantity, cost, lot or decimal precision.";
        return null;
    }
}