using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Hostel;

/// <summary>
/// Read-only export through the canonical tenant/campus-authorized room inventory query.
/// Pagination checks are best effort; they do not provide a transactional snapshot.
/// </summary>
public sealed partial class HostelService
{
    public async Task<ApiResponse<string>> ExportRoomInventoryCsvAsync(
        long? campusId, string? search, CancellationToken cancellationToken = default)
    {
        var first = await GetRoomInventoryAsync(campusId, 1, 100, search, cancellationToken);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message ?? "Room inventory unavailable.", first.StatusCode);
        var expected = first.Data.TotalCount;
        if (expected > 1000)
            return ApiResponse<string>.ErrorResponse("Export exceeds 1,000 rooms. Filter by campus or search.", 413);
        if (first.Data.Page != 1 || first.Data.PageSize != 100)
            return ApiResponse<string>.ErrorResponse("Room inventory changed during export. Retry.", 409);
        var rows = new List<HostelRoomInventoryDto>(first.Data.Items);
        for (var page = 2; page <= (expected + 99) / 100; page++)
        {
            var next = await GetRoomInventoryAsync(campusId, page, 100, search, cancellationToken);
            if (!next.Success || next.Data == null || next.Data.TotalCount != expected ||
                next.Data.Page != page || next.Data.PageSize != 100)
                return ApiResponse<string>.ErrorResponse("Room inventory changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var ids = new HashSet<long>();
        if (rows.Count != expected || rows.Any(x => !ids.Add(x.Id)))
            return ApiResponse<string>.ErrorResponse("Room inventory changed during export. Retry.", 409);

        var csv = new StringBuilder("Hostel,Room,Hostel Active,Room Active,Capacity,Active Beds,Occupied Today,Active Reservation\r\n");
        foreach (var row in rows)
            csv.Append(CsvCell(row.HostelName)).Append(',')
                .Append(CsvCell(row.RoomNumber)).Append(',')
                .Append(row.HostelActive ? "Yes" : "No").Append(',')
                .Append(row.IsActive ? "Yes" : "No").Append(',')
                .Append(row.Capacity.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.ActiveBeds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.OccupiedBeds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.HasActiveReservations ? "Yes" : "No").Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
