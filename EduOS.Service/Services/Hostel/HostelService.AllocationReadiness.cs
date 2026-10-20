using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    /// <summary>Finds scheduled arrivals and at-risk active reservations from the canonical allocation aggregate.</summary>
    public async Task<ApiResponse<PagedResult<HostelAllocationReadinessRowDto>>> GetAllocationReadinessAsync(
        long? campusId, int page, int pageSize, string? search, bool attentionOnly,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelAllocationReadinessRowDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<HostelAllocationReadinessRowDto>>.ErrorResponse("Invalid readiness filter.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var query = from allocation in _allocations.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking() on allocation.StudentEnrollmentId equals enrollment.Id
            join student in _students.GetQueryable().AsNoTracking() on allocation.StudentId equals student.Id
            join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
            join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
            join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
            join campus in _campuses.GetQueryable().AsNoTracking() on hostel.CampusId equals campus.Id
            where allocation.TenantId == tenant && enrollment.TenantId == tenant &&
                student.TenantId == tenant && bed.TenantId == tenant && room.TenantId == tenant &&
                hostel.TenantId == tenant && campus.TenantId == tenant &&
                !allocation.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted &&
                !bed.IsDeleted && !room.IsDeleted && !hostel.IsDeleted && !campus.IsDeleted &&
                allocation.State == HostelAllocationState.Active &&
                (!allocation.EndDate.HasValue || allocation.EndDate.Value >= today ||
                    allocation.EndDate.Value < allocation.StartDate) &&
                (!campusId.HasValue || hostel.CampusId == campusId.Value)
            select new
            {
                allocation.Id, allocation.StartDate,
                InvalidDates = allocation.EndDate.HasValue && allocation.EndDate.Value < allocation.StartDate,
                EnrollmentMatches = enrollment.StudentId == allocation.StudentId,
                CampusId = campus.Id, CampusName = campus.Name, HostelName = hostel.Name,
                room.RoomNumber, bed.BedNumber,
                CampusActive = campus.IsActive, FacilityActive = hostel.IsActive,
                RoomActive = room.IsActive, BedActive = bed.IsActive,
                EnrollmentCampusMatches = enrollment.CampusId == hostel.CampusId,
                EnrollmentCurrent = enrollment.IsCurrent,
                EnrollmentActive = enrollment.State == EnrollmentState.Active,
                StudentActive = student.StatusCode == "Active"
            };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.HostelName.Contains(term) || x.RoomNumber.Contains(term) ||
                x.BedNumber.Contains(term) || x.CampusName.Contains(term));
        query = query.Where(x => x.InvalidDates || !x.EnrollmentMatches ||
            !x.CampusActive || !x.FacilityActive || !x.RoomActive ||
            !x.BedActive || !x.EnrollmentCampusMatches || !x.EnrollmentCurrent ||
            !x.EnrollmentActive || !x.StudentActive || (!attentionOnly && x.StartDate > today));
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(x => x.StartDate).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var result = rows.Select(x =>
        {
            var readiness = ClassifyAllocationReadinessWithIntegrity(x.InvalidDates, x.EnrollmentMatches,
                x.CampusActive, x.FacilityActive, x.RoomActive, x.BedActive, x.EnrollmentCampusMatches,
                x.EnrollmentCurrent, x.EnrollmentActive, x.StudentActive, x.StartDate, today);
            return new HostelAllocationReadinessRowDto
            {
                AllocationId = x.Id, CampusId = x.CampusId, CampusName = x.CampusName,
                HostelName = x.HostelName, RoomNumber = x.RoomNumber, BedNumber = x.BedNumber,
                StartDate = x.StartDate, Readiness = readiness, NeedsAttention = readiness != "Scheduled arrival"
            };
        }).ToList();
        return ApiResponse<PagedResult<HostelAllocationReadinessRowDto>>.SuccessResponse(
            new PagedResult<HostelAllocationReadinessRowDto>
            {
                Page = page, PageSize = pageSize, TotalCount = total, Items = result
            });
    }

    // Preserve invalid records as actionable exceptions without disclosing student identity.
    public static string ClassifyAllocationReadinessWithIntegrity(bool invalidDates, bool enrollmentMatches,
        bool campusActive, bool facilityActive, bool roomActive, bool bedActive,
        bool enrollmentCampusMatches, bool enrollmentCurrent, bool enrollmentActive,
        bool studentActive, DateOnly startDate, DateOnly today)
    {
        if (invalidDates) return "Invalid allocation dates";
        if (!enrollmentMatches) return "Enrollment identity mismatch";
        return ClassifyAllocationReadiness(campusActive, facilityActive, roomActive, bedActive,
            enrollmentCampusMatches, enrollmentCurrent, enrollmentActive, studentActive, startDate, today);
    }

    // Pure classification: all safety exceptions outrank a future scheduled arrival.
    public static string ClassifyAllocationReadiness(bool campusActive, bool facilityActive, bool roomActive,
        bool bedActive, bool enrollmentCampusMatches, bool enrollmentCurrent, bool enrollmentActive,
        bool studentActive, DateOnly startDate, DateOnly today)
    {
        if (!campusActive) return "Inactive campus";
        if (!facilityActive) return "Inactive facility";
        if (!roomActive) return "Inactive room";
        if (!bedActive) return "Inactive bed";
        if (!enrollmentCampusMatches) return "Enrollment campus mismatch";
        if (!enrollmentCurrent || !enrollmentActive) return "Inactive enrollment";
        if (!studentActive) return "Inactive student";
        return startDate > today ? "Scheduled arrival" : "Ready";
    }

    /// <summary>Bounded export reuses the authorized paged query, with consistency checks.</summary>
    public async Task<ApiResponse<string>> ExportAllocationReadinessCsvAsync(
        long? campusId, string? search, bool attentionOnly, CancellationToken cancellationToken = default)
    {
        var first = await GetAllocationReadinessAsync(campusId, 1, 100, search, attentionOnly, cancellationToken);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message ?? "Readiness unavailable.", first.StatusCode);
        var count = first.Data.TotalCount;
        if (count > 1000)
            return ApiResponse<string>.ErrorResponse("Export exceeds 1,000 allocations. Filter by campus or search.", 413);
        var rows = new List<HostelAllocationReadinessRowDto>(first.Data.Items);
        for (var page = 2; page <= (count + 99) / 100; page++)
        {
            var next = await GetAllocationReadinessAsync(campusId, page, 100, search, attentionOnly, cancellationToken);
            if (!next.Success || next.Data == null || next.Data.TotalCount != count ||
                next.Data.Page != page)
                return ApiResponse<string>.ErrorResponse("Readiness changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var ids = new HashSet<long>();
        if (rows.Count != count || rows.Any(x => !ids.Add(x.AllocationId)))
            return ApiResponse<string>.ErrorResponse("Readiness changed during export. Retry.", 409);
        var csv = new StringBuilder("Campus,Hostel,Room,Bed,Start Date,Readiness,Attention Required\r\n");
        foreach (var row in rows)
        {
            csv.Append(CsvCell(row.CampusName)).Append(',')
                .Append(CsvCell(row.HostelName)).Append(',')
                .Append(CsvCell(row.RoomNumber)).Append(',')
                .Append(CsvCell(row.BedNumber)).Append(',')
                .Append(row.StartDate.ToString("yyyy-MM-dd")).Append(',')
                .Append(CsvCell(row.Readiness)).Append(',')
                .Append(row.NeedsAttention ? "Yes" : "No").Append("\r\n");
        }
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
