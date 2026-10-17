using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    /// <summary>Tenant-admin-only bounded export from the canonical vehicle/driver assignment history.</summary>
    public async Task<ApiResponse<string>> ExportVehicleDriverLinksCsvAsync(
        long? campusId, string? search, CancellationToken cancellationToken = default)
    {
        var first = await GetVehicleDriverLinksAsync(campusId, 1, 100, search, cancellationToken);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message ?? "Vehicle/driver history unavailable.", first.StatusCode);
        if (first.Data.TotalCount > 1000)
            return ApiResponse<string>.ErrorResponse("Export exceeds 1,000 assignments. Narrow your filters.", 413);

        var rows = new List<TransportVehicleDriverLinkRowDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetVehicleDriverLinksAsync(campusId, page, 100, search, cancellationToken);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<string>.ErrorResponse("Assignments changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var ids = new HashSet<long>();
        if (rows.Count != first.Data.TotalCount || rows.Any(x => !ids.Add(x.Id)))
            return ApiResponse<string>.ErrorResponse("Assignments changed during export. Retry.", 409);

        var csv = new StringBuilder("Campus,Vehicle,Effective From,Effective To,Current Today\r\n");
        foreach (var row in rows)
            csv.Append(CsvCell(row.CampusName)).Append(',')
                .Append(CsvCell(row.VehicleNumber)).Append(',')
                .Append(row.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.EffectiveTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty)
                .Append(',').Append(row.IsCurrent ? "Yes" : "No").Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
