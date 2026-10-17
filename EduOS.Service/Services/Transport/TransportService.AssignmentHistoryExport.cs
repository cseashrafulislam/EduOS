using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    /// <summary>Bounded, campus-authorized export of canonical transport history; no fares, contacts or names.</summary>
    public async Task<ApiResponse<string>> ExportAssignmentHistoryCsvAsync(
        long? campusId, string? search, TransportAssignmentState? state,
        DateOnly? fromDate, DateOnly? toDate, Guid? routeReference, Guid? vehicleReference,
        CancellationToken cancellationToken = default)
    {
        var first = await GetAssignmentHistoryAsync(campusId, 1, 100, search, state,
            fromDate, toDate, routeReference, vehicleReference, cancellationToken);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message ?? "History unavailable.", first.StatusCode);
        if (first.Data.TotalCount > 1000)
            return ApiResponse<string>.ErrorResponse("Export exceeds 1,000 assignments. Narrow your filters.", 413);

        var rows = new List<TransportAssignmentHistoryRowDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetAssignmentHistoryAsync(campusId, page, 100, search, state,
                fromDate, toDate, routeReference, vehicleReference, cancellationToken);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<string>.ErrorResponse("History changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var references = new HashSet<Guid>();
        if (rows.Count != first.Data.TotalCount || rows.Any(row => !references.Add(row.Reference)))
            return ApiResponse<string>.ErrorResponse("History changed during export. Retry.", 409);

        var csv = new StringBuilder("Campus,Student Code,Route,Vehicle,Start Date,End Date,Status\r\n");
        foreach (var row in rows)
        {
            var stateLabel = row.State switch
            {
                TransportAssignmentState.Active => "Active",
                TransportAssignmentState.Closed => "Closed",
                TransportAssignmentState.Cancelled => "Cancelled",
                _ => "Unknown"
            };
            csv.Append(CsvCell(row.CampusName)).Append(',')
                .Append(CsvCell(row.StudentCode)).Append(',')
                .Append(CsvCell(row.RouteName)).Append(',')
                .Append(CsvCell(row.VehicleNumber)).Append(',')
                .Append(row.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty)
                .Append(',').Append(stateLabel).Append("\r\n");
        }
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
