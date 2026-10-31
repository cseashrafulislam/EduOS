using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EduOS.Service.Services.Inventory;

/// <summary>Checker worklist and privacy-minimized PO audit projection. No stock or GL mutations.</summary>
public sealed partial class ProcurementPurchaseOrderService
{
    public async Task<ApiResponse<PagedResult<ProcurementPurchaseOrderDto>>> PendingApprovalsAsync(
        int page, int pageSize, long? campusId, string? search, CancellationToken ct = default)
    {
        if (!Checker) return Denied<PagedResult<ProcurementPurchaseOrderDto>>();
        if (page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue || campusId is <= 0 || search?.Length > 100)
            return ApiResponse<PagedResult<ProcurementPurchaseOrderDto>>.ErrorResponse("Invalid approval filters.");
        // SQL applies tenant/campus and maker exclusion before counting or pagination.
        var query = Visible().Where(x =>
            (x.StateCode == "Submitted" || x.StateCode == "CancelRequested") &&
            x.CreatedBy.HasValue && x.CreatedBy != user.UserId);
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.OrderNumber.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var headers = await query.OrderBy(x => x.SubmittedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = headers.Select(x => x.Id).ToArray();
        var lines = await db.ProcurementPurchaseOrderLines.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && ids.Contains(x.PurchaseOrderId) && !x.IsDeleted)
            .ToListAsync(ct);
        var grouped = lines.GroupBy(x => x.PurchaseOrderId)
            .ToDictionary(x => x.Key, x => x.AsEnumerable());
        return ApiResponse<PagedResult<ProcurementPurchaseOrderDto>>.SuccessResponse(
            new PagedResult<ProcurementPurchaseOrderDto>
            {
                TotalCount = total, Page = page, PageSize = pageSize,
                Items = headers.Select(x => Map(x, grouped.GetValueOrDefault(x.Id) ??
                    Enumerable.Empty<ProcurementPurchaseOrderLine>())).ToList()
            });
    }

    private static string? SafeAuditField(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object ||
                !parsed.RootElement.TryGetProperty(name, out var value) ||
                value.ValueKind != JsonValueKind.String) return null;
            var text = value.GetString();
            return text?.Length > 500 ? text[..500] : text;
        }
        catch (JsonException) { return null; }
    }

    public async Task<ApiResponse<PagedResult<ProcurementPurchaseOrderHistoryDto>>> HistoryAsync(
        long id, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Reader) return Denied<PagedResult<ProcurementPurchaseOrderHistoryDto>>();
        if (id <= 0 || page < 1 || pageSize is < 1 or > 50 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<ProcurementPurchaseOrderHistoryDto>>.ErrorResponse("Invalid history page.");
        if (!await Visible().AnyAsync(x => x.Id == id, ct))
            return ApiResponse<PagedResult<ProcurementPurchaseOrderHistoryDto>>.ErrorResponse("Order not found.", 404);
        var query = db.AuditLogs.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.EntityName == nameof(ProcurementPurchaseOrder) &&
            x.EntityId == id && x.IsSuccess);
        var total = await query.CountAsync(ct);
        var entries = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.OccurredAt, x.UserId, x.Action, x.OldValue, x.NewValue })
            .ToListAsync(ct);
        return ApiResponse<PagedResult<ProcurementPurchaseOrderHistoryDto>>.SuccessResponse(
            new PagedResult<ProcurementPurchaseOrderHistoryDto>
            {
                TotalCount = total, Page = page, PageSize = pageSize,
                Items = entries.Select(x => new ProcurementPurchaseOrderHistoryDto
                {
                    Id = x.Id, OccurredAt = x.OccurredAt, ActorUserId = x.UserId,
                    Action = x.Action,
                    PreviousState = SafeAuditField(x.OldValue, "StateCode"),
                    State = SafeAuditField(x.NewValue, "StateCode"),
                    Reason = SafeAuditField(x.NewValue, "DecisionReason") ??
                             SafeAuditField(x.OldValue, "DecisionReason")
                }).ToList()
            });
    }
}
