using System.Globalization;
using System.Text;
using EduOS.Core.Common;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Platform;

public sealed partial class IntegrationAdministrationService
{
    private const int MaxWebhookDeliveryExportRows = 1000;

    private static string WebhookCsvCell(string? value)
    {
        var text = value ?? string.Empty;
        var first = text.TrimStart(' ', '\t', '\r', '\n');
        if (first.Length > 0 && (first[0] is '=' or '+' or '-' or '@')) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    private static string WebhookUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture) : string.Empty;

    public async Task<ApiResponse<string>> ExportWebhookDeliveriesCsvAsync(long endpointId, CancellationToken cancellationToken = default)
    {
        if (!CanManage) return ApiResponse<string>.ErrorResponse("Tenant administrator required.", 403);
        if (endpointId <= 0) return ApiResponse<string>.ErrorResponse("Valid webhook endpoint required.");
        var exists = await db.WebhookEndpoints.AsNoTracking().AnyAsync(
            x => x.Id == endpointId && x.TenantId == user.TenantId && !x.IsDeleted, cancellationToken);
        if (!exists) return ApiResponse<string>.ErrorResponse("Webhook not found.", 404);
        var rows = await db.WebhookDeliveries.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.WebhookEndpointId == endpointId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Take(MaxWebhookDeliveryExportRows + 1)
            .Select(x => new { x.Id, x.EventId, x.EventCode, x.State, x.AttemptCount,
                x.LastAttemptAt, x.NextAttemptAt, x.FailureReason, x.CreatedAt })
            .ToListAsync(cancellationToken);
        if (rows.Count > MaxWebhookDeliveryExportRows)
            return ApiResponse<string>.ErrorResponse("Delivery history exceeds the 1,000-row export limit.", 413);
        var csv = new StringBuilder("Delivery ID,Event ID,Event Code,State,Attempts,Created UTC,Last Attempt UTC,Next Attempt UTC,Failure Reason\r\n");
        foreach (var row in rows)
        {
            csv.AppendJoin(",", new[]
            {
                WebhookCsvCell(row.Id.ToString(CultureInfo.InvariantCulture)),
                WebhookCsvCell(row.EventId.ToString("D")),
                WebhookCsvCell(row.EventCode),
                WebhookCsvCell(row.State.ToString()),
                WebhookCsvCell(row.AttemptCount.ToString(CultureInfo.InvariantCulture)),
                WebhookCsvCell(WebhookUtc(row.CreatedAt)),
                WebhookCsvCell(WebhookUtc(row.LastAttemptAt)),
                WebhookCsvCell(WebhookUtc(row.NextAttemptAt)),
                WebhookCsvCell(row.FailureReason)
            }).Append("\r\n");
        }
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
