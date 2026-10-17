using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Service.Services.SaaS;

public sealed partial class TenantFeatureAuditService
{
    public static string CsvCell(string? value)
    {
        var text = value ?? string.Empty;
        var first = text.TrimStart(' ', '\t', '\r', '\n');
        if (first.Length > 0 && (first[0] is '=' or '+' or '-' or '@'))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>Bounded read-only export of the same tenant-only audit visible on screen.</summary>
    public async Task<ApiResponse<string>> ExportCsvAsync(CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || user.TenantId <= 0 || !user.IsTenantAdmin)
            return ApiResponse<string>.ErrorResponse("Tenant administrator required.", 403);
        var rows = new List<TenantFeatureAuditRowDto>();
        for (var page = 1; page <= 10; page++)
        {
            var result = await GetAsync(page, 100, cancellationToken);
            if (!result.Success || result.Data == null)
                return ApiResponse<string>.ErrorResponse(result.Message, result.StatusCode);
            if (result.Data.TotalCount > 1000)
                return ApiResponse<string>.ErrorResponse("Audit exceeds the 1,000-feature export limit.", 413);
            rows.AddRange(result.Data.Items);
            if (rows.Count >= result.Data.TotalCount) break;
        }
        var csv = new StringBuilder("Feature Code,Feature Name,Included In Plan,Override Recorded,Currently Effective,Restricted,Effective From UTC,Effective To UTC,Reason\r\n");
        foreach (var row in rows)
        {
            csv.AppendJoin(",", new[]
            {
                CsvCell(row.FeatureCode), CsvCell(row.FeatureName),
                CsvCell(row.IncludedInPlan ? "Yes" : "No"),
                CsvCell(row.HasOverride ? "Yes" : "No"),
                CsvCell(row.OverrideCurrentlyEffective ? "Yes" : "No"),
                CsvCell(row.RestrictedInConfiguration ? "Yes" : "No"),
                CsvCell(row.EffectiveFrom?.ToUniversalTime().ToString("O")),
                CsvCell(row.EffectiveTo?.ToUniversalTime().ToString("O")),
                CsvCell(row.Reason)
            }).Append("\r\n");
        }
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
