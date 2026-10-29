using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Hostel;

/// <summary>
/// Read-only guardian view. A guardian relationship is insufficient by itself:
/// the current tenant-local link must explicitly authorize student pickup.
/// No current financial or historical location information is exposed.
/// </summary>
public sealed class GuardianHostelStayService(
    EduOSDbContext db, ICurrentUserService user, TimeProvider clock,
    ILogger<GuardianHostelStayService> logger) : IGuardianHostelStayService
{
    private bool Allowed => user.IsAuthenticated && user.UserId > 0 && user.TenantId > 0 &&
        (user.IsInRole("Guardian") || user.IsInRole("Parent"));

    public async Task<ApiResponse<PagedResult<GuardianHostelStayRowDto>>> GetCurrentStaysAsync(
        int page, int pageSize, string? search, CancellationToken ct = default)
    {
        if (!Allowed)
            return ApiResponse<PagedResult<GuardianHostelStayRowDto>>.ErrorResponse("Guardian access required.", 403);
        if (page < 1 || pageSize is < 1 or > 20 || (long)(page - 1) * pageSize > int.MaxValue ||
            search?.Trim().Length > 100)
            return ApiResponse<PagedResult<GuardianHostelStayRowDto>>.ErrorResponse("Invalid hostel stay filters.");

        var tenant = user.TenantId;
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        try
        {
            var query =
                from link in db.StudentGuardians.AsNoTracking()
                join guardian in db.Guardians.AsNoTracking() on link.GuardianId equals guardian.Id
                join student in db.Students.AsNoTracking() on link.StudentId equals student.Id
                join enrollment in db.StudentEnrollments.AsNoTracking() on student.Id equals enrollment.StudentId
                join stay in db.StudentHostelAllocations.AsNoTracking() on enrollment.Id equals stay.StudentEnrollmentId
                join bed in db.HostelBeds.AsNoTracking() on stay.HostelBedId equals bed.Id
                join room in db.HostelRooms.AsNoTracking() on bed.HostelRoomId equals room.Id
                join hostel in db.Hostels.AsNoTracking() on room.HostelId equals hostel.Id
                join campus in db.Campuses.AsNoTracking() on hostel.CampusId equals campus.Id
                where link.TenantId == tenant && guardian.TenantId == tenant &&
                    student.TenantId == tenant && enrollment.TenantId == tenant &&
                    stay.TenantId == tenant && bed.TenantId == tenant &&
                    room.TenantId == tenant && hostel.TenantId == tenant &&
                    campus.TenantId == tenant && guardian.UserId == user.UserId &&
                    guardian.IsActive && !guardian.IsDeleted && !link.IsDeleted &&
                    link.IsAuthorizedPickup && !student.IsDeleted &&
                    (student.StatusCode == "Active" || student.StatusCode == "Suspended") &&
                    !enrollment.IsDeleted && enrollment.IsCurrent &&
                    enrollment.State == EnrollmentState.Active &&
                    enrollment.CampusId == campus.Id && campus.IsActive &&
                    stay.StudentId == student.Id && !stay.IsDeleted &&
                    stay.State == HostelAllocationState.Active &&
                    stay.StartDate <= today && (!stay.EndDate.HasValue || stay.EndDate.Value >= today)
                select new
                {
                    stay.Id, student.FullName, student.StudentCode, CampusName = campus.Name,
                    HostelName = hostel.Name, room.RoomNumber, bed.BedNumber,
                    stay.StartDate, stay.EndDate
                };
            var term = search?.Trim();
            if (!string.IsNullOrWhiteSpace(term))
                query = query.Where(x => x.StudentCode.Contains(term) || x.FullName.Contains(term));
            var count = await query.CountAsync(ct);
            var rows = await query.OrderBy(x => x.StudentCode).ThenBy(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new GuardianHostelStayRowDto
                {
                    StudentName = x.FullName, StudentCode = x.StudentCode,
                    CampusName = x.CampusName, HostelName = x.HostelName,
                    RoomNumber = x.RoomNumber, BedNumber = x.BedNumber,
                    StartDate = x.StartDate, EndDate = x.EndDate
                }).ToListAsync(ct);
            return ApiResponse<PagedResult<GuardianHostelStayRowDto>>.SuccessResponse(
                new PagedResult<GuardianHostelStayRowDto>
                {
                    Page = page, PageSize = pageSize, TotalCount = count, Items = rows
                });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Guardian hostel stay read failed for tenant {TenantId}", tenant);
            return ApiResponse<PagedResult<GuardianHostelStayRowDto>>.ErrorResponse("Hostel stays unavailable.", 500);
        }
    }

    public async Task<ApiResponse<string>> ExportCurrentPageCsvAsync(
        int page, string? search, CancellationToken ct = default)
    {
        var result = await GetCurrentStaysAsync(page, 20, search, ct);
        if (!result.Success || result.Data is null)
            return ApiResponse<string>.ErrorResponse(result.Message, result.StatusCode);
        var csv = new StringBuilder("Student Code,Student Name,Campus,Hostel,Room,Bed,Start Date,End Date\r\n");
        foreach (var row in result.Data.Items)
            csv.Append(Cell(row.StudentCode)).Append(',').Append(Cell(row.StudentName)).Append(',')
                .Append(Cell(row.CampusName)).Append(',').Append(Cell(row.HostelName)).Append(',')
                .Append(Cell(row.RoomNumber)).Append(',').Append(Cell(row.BedNumber)).Append(',')
                .Append(row.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "")
                .Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }

    private static string Cell(string? value)
    {
        var safe = (value ?? "").Replace("\r", " ").Replace("\n", " ");
        var leading = safe.TrimStart(' ', '\t');
        if (leading.Length > 0 && (leading[0] is '=' or '+' or '-' or '@'))
            safe = "'" + safe;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }
}
