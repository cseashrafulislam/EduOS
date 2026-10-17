using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using System.Text;

namespace EduOS.Service.Services.Hostel;

/// <summary>
/// Read-only inventory export through the canonical, tenant/campus-authorized bed query.
/// This is a bounded best-effort multi-page read, not a transactional database snapshot.
/// </summary>
public sealed partial class HostelService
{
    public async Task<ApiResponse<string>> ExportBedInventoryCsvAsync(
        long? campusId, string? search, CancellationToken cancellationToken = default)
    {
        var first = await GetBedInventoryAsync(campusId, 1, 100, search, cancellationToken);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message ?? "Bed inventory unavailable.", first.StatusCode);
        var expected = first.Data.TotalCount;
        if (expected > 1000)
            return ApiResponse<string>.ErrorResponse("Export exceeds 1,000 beds. Filter by campus or search.", 413);
        var rows = new List<HostelBedInventoryDto>(first.Data.Items);
        for (var page = 2; page <= (expected + 99) / 100; page++)
        {
            var next = await GetBedInventoryAsync(campusId, page, 100, search, cancellationToken);
            if (!next.Success || next.Data == null || next.Data.TotalCount != expected ||
                next.Data.Page != page || next.Data.PageSize != 100)
                return ApiResponse<string>.ErrorResponse("Bed inventory changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var ids = new HashSet<long>();
        if (rows.Count != expected || rows.Any(x => !ids.Add(x.Id)))
            return ApiResponse<string>.ErrorResponse("Bed inventory changed during export. Retry.", 409);

        var csv = new StringBuilder("Hostel,Room,Bed,Active,Occupied Today,Active Reservation\r\n");
        foreach (var row in rows)
            csv.Append(CsvCell(row.HostelName)).Append(',')
                .Append(CsvCell(row.RoomNumber)).Append(',')
                .Append(CsvCell(row.BedNumber)).Append(',')
                .Append(row.IsActive ? "Yes" : "No").Append(',')
                .Append(row.IsOccupied ? "Yes" : "No").Append(',')
                .Append(row.HasActiveReservations ? "Yes" : "No").Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
