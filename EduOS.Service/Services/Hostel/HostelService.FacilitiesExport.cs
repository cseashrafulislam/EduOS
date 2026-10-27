using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    public async Task<ApiResponse<string>> ExportFacilitiesCsvAsync(
        long? campusId, string? search, CancellationToken ct = default)
    {
        var first = await GetFacilitiesAsync(campusId, 1, 100, search, ct);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message ?? "Facilities unavailable.", first.StatusCode);
        var count = first.Data.TotalCount;
        if (count > 1000) return ApiResponse<string>.ErrorResponse("Export exceeds 1,000 facilities. Narrow filters.", 413);
        var rows = new List<HostelFacilityDto>(first.Data.Items);
        for (var page = 2; page <= (count + 99) / 100; page++)
        {
            var next = await GetFacilitiesAsync(campusId, page, 100, search, ct);
            if (!next.Success || next.Data == null || next.Data.TotalCount != count ||
                next.Data.Page != page || next.Data.PageSize != 100)
                return ApiResponse<string>.ErrorResponse("Facilities changed during export.", 409);
            rows.AddRange(next.Data.Items);
        }
        var refs = new HashSet<Guid>();
        if (rows.Count != count || rows.Any(x => !refs.Add(x.Reference)))
            return ApiResponse<string>.ErrorResponse("Facilities changed during export.", 409);
        var csv = new StringBuilder("Campus,Facility,Code,Restriction,Active Rooms,Occupied Today,Active Reservations,Status\r\n");
        foreach (var x in rows)
            csv.Append(CsvCell(x.CampusName)).Append(',')
                .Append(CsvCell(x.Name)).Append(',')
                .Append(CsvCell(x.Code)).Append(',')
                .Append(CsvCell(x.GenderRestriction)).Append(',')
                .Append(x.ActiveRooms.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(x.OccupiedBeds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(x.ActiveReservations.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(x.IsActive ? "Active" : "Inactive").Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
