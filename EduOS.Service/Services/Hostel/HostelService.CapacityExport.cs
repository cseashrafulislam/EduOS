using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    // Bounded, privacy-minimized export reuses the same tenant/campus-authorized report.
    public async Task<ApiResponse<string>> ExportCapacityCsvAsync(
        long? campusId, string? search, CancellationToken cancellationToken = default, bool attentionOnly = false)
    {
        var first = await GetCapacityAsync(campusId, 1, 100, search, cancellationToken, attentionOnly);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message ?? "Capacity report unavailable.", first.StatusCode);
        if (first.Data.TotalCount > 1000)
            return ApiResponse<string>.ErrorResponse("Export exceeds 1,000 rooms. Filter by campus or search.", 413);
        var rows = new List<HostelCapacityRowDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetCapacityAsync(campusId, page, 100, search, cancellationToken, attentionOnly);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<string>.ErrorResponse("Export changed during retrieval. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var ids = new HashSet<long>();
        if (rows.Count != first.Data.TotalCount || rows.Any(x => !ids.Add(x.RoomId)))
            return ApiResponse<string>.ErrorResponse("Export changed during retrieval. Retry.", 409);
        var csv = new StringBuilder("Hostel,Room,Hostel Active,Room Active,Capacity,Active Beds,Inactive Beds,Occupied Beds,Available Beds,Risk\r\n");
        foreach (var row in rows)
        {
            csv.Append(CsvCell(row.HostelName)).Append(',').Append(CsvCell(row.RoomNumber)).Append(',')
                .Append(row.HostelActive ? "Yes" : "No").Append(',')
                .Append(row.RoomActive ? "Yes" : "No").Append(',')
                .Append(row.Capacity.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.ActiveBeds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.InactiveBeds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.OccupiedBeds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.AvailableBeds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.HasRisk ? "Review" : "OK").Append("\r\n");
        }
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }

    private static string CsvCell(string? input)
    {
        var value = (input ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
