using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    /// <summary>
    /// Active driver license compliance, tenant-wide by design: TransportDriver has no canonical CampusId.
    /// Only TenantAdmin can access driver identity. No phone, license number or finance data is returned.
    /// </summary>
    public async Task<ApiResponse<PagedResult<TransportDriverLicenseComplianceRowDto>>> GetDriverLicenseComplianceAsync(
        int page, int pageSize, string? search, string? state, int withinDays,
        CancellationToken cancellationToken = default)
    {
        if (!CanManageDriverRegistry()) return Denied<PagedResult<TransportDriverLicenseComplianceRowDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            search?.Trim().Length > 100 || withinDays is < 1 or > 365)
            return ApiResponse<PagedResult<TransportDriverLicenseComplianceRowDto>>.ErrorResponse("Invalid compliance filters.");
        var mode = string.IsNullOrWhiteSpace(state) ? "ATTENTION" : state.Trim().ToUpperInvariant();
        if (mode is not ("ATTENTION" or "ALL" or "MISSING" or "EXPIRED" or "EXPIRING" or "VALID"))
            return ApiResponse<PagedResult<TransportDriverLicenseComplianceRowDto>>.ErrorResponse("Invalid license state.");

        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var cutoff = today.AddDays(withinDays);
        var query = _drivers.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.IsActive);
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.FullName.Contains(term));
        query = mode switch
        {
            "ATTENTION" => query.Where(x => !x.LicenseExpiresOn.HasValue || x.LicenseExpiresOn.Value <= cutoff),
            "MISSING" => query.Where(x => !x.LicenseExpiresOn.HasValue),
            "EXPIRED" => query.Where(x => x.LicenseExpiresOn.HasValue && x.LicenseExpiresOn.Value < today),
            "EXPIRING" => query.Where(x => x.LicenseExpiresOn.HasValue &&
                x.LicenseExpiresOn.Value >= today && x.LicenseExpiresOn.Value <= cutoff),
            "VALID" => query.Where(x => x.LicenseExpiresOn.HasValue && x.LicenseExpiresOn.Value > cutoff),
            _ => query
        };

        var total = await query.CountAsync(cancellationToken);
        var drivers = await query.OrderBy(x => !x.LicenseExpiresOn.HasValue ? 0 :
                x.LicenseExpiresOn.Value < today ? 1 : x.LicenseExpiresOn.Value <= cutoff ? 2 : 3)
            .ThenBy(x => x.LicenseExpiresOn).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.FullName, x.LicenseExpiresOn })
            .ToListAsync(cancellationToken);
        var ids = drivers.Select(x => x.Id).ToArray();
        var assignedIds = await _driverLinks.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && ids.Contains(x.TransportDriverId) &&
                x.IsCurrent && x.EffectiveFrom <= today &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today))
            .Select(x => x.TransportDriverId).Distinct().ToListAsync(cancellationToken);
        var assigned = assignedIds.ToHashSet();
        var rows = drivers.Select(x => new TransportDriverLicenseComplianceRowDto
        {
            DriverId = x.Id, DriverName = x.FullName, LicenseExpiresOn = x.LicenseExpiresOn,
            DaysUntilExpiry = x.LicenseExpiresOn.HasValue
                ? x.LicenseExpiresOn.Value.DayNumber - today.DayNumber : null,
            ComplianceState = !x.LicenseExpiresOn.HasValue ? "Missing" :
                x.LicenseExpiresOn.Value < today ? "Expired" :
                x.LicenseExpiresOn.Value <= cutoff ? "Expiring" : "Valid",
            HasCurrentVehicle = assigned.Contains(x.Id)
        }).ToList();
        return ApiResponse<PagedResult<TransportDriverLicenseComplianceRowDto>>.SuccessResponse(
            new PagedResult<TransportDriverLicenseComplianceRowDto>
            {
                Items = rows, TotalCount = total, Page = page, PageSize = pageSize
            });
    }

    /// <summary>Bounded CSV with IDs rather than names; no license identifiers or contact details.</summary>
    public async Task<ApiResponse<string>> ExportDriverLicenseComplianceCsvAsync(
        string? search, string? state, int withinDays, CancellationToken cancellationToken = default)
    {
        var first = await GetDriverLicenseComplianceAsync(1, 100, search, state, withinDays, cancellationToken);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message, first.StatusCode);
        if (first.Data.TotalCount > 1000)
            return ApiResponse<string>.ErrorResponse("Export exceeds 1,000 drivers. Narrow the filters.", 413);
        var rows = new List<TransportDriverLicenseComplianceRowDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetDriverLicenseComplianceAsync(page, 100, search, state, withinDays, cancellationToken);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<string>.ErrorResponse("Compliance data changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var ids = new HashSet<long>();
        if (rows.Count != first.Data.TotalCount || rows.Any(x => !ids.Add(x.DriverId)))
            return ApiResponse<string>.ErrorResponse("Compliance data changed during export. Retry.", 409);
        var csv = new StringBuilder("Driver ID,License Expiry,Days Until Expiry,State,Current Vehicle\r\n");
        foreach (var row in rows)
            csv.Append(row.DriverId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.LicenseExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty)
                .Append(',').Append(row.DaysUntilExpiry?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
                .Append(',').Append(CsvCell(row.ComplianceState)).Append(',')
                .Append(row.HasCurrentVehicle ? "Yes" : "No").Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
