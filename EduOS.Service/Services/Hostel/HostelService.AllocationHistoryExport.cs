using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    /// <summary>Bounded, campus-authorized export of canonical allocation history; no names, rent or contact data.</summary>
    public async Task<ApiResponse<string>> ExportAllocationHistoryCsvAsync(
        long? campusId, string? search, HostelAllocationState? state,
        DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken = default)
    {
        var first = await GetAllocationHistoryAsync(campusId, 1, 100, search, state,
            fromDate, toDate, cancellationToken);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message ?? "Allocation history unavailable.", first.StatusCode);
        if (first.Data.TotalCount > 1000)
            return ApiResponse<string>.ErrorResponse("Export exceeds 1,000 allocations. Narrow your filters.", 413);

        var rows = new List<HostelAllocationHistoryRowDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetAllocationHistoryAsync(campusId, page, 100, search, state,
                fromDate, toDate, cancellationToken);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<string>.ErrorResponse("History changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var ids = new HashSet<long>();
        if (rows.Count != first.Data.TotalCount || rows.Any(row => !ids.Add(row.Id)))
            return ApiResponse<string>.ErrorResponse("History changed during export. Retry.", 409);

        var csv = new StringBuilder("Campus,Student Code,Hostel,Room,Bed,Start Date,End Date,Status\r\n");
        foreach (var row in rows)
        {
            var label = row.State switch
            {
                HostelAllocationState.Active => "Active",
                HostelAllocationState.Closed => "Closed",
                HostelAllocationState.Cancelled => "Cancelled",
                _ => "Unknown"
            };
            csv.Append(CsvCell(row.CampusName)).Append(',')
                .Append(CsvCell(row.StudentCode)).Append(',')
                .Append(CsvCell(row.HostelName)).Append(',')
                .Append(CsvCell(row.RoomNumber)).Append(',')
                .Append(CsvCell(row.BedNumber)).Append(',')
                .Append(row.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty)
                .Append(',').Append(label).Append("\r\n");
        }
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
