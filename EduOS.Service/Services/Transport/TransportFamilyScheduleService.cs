using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace EduOS.Service.Services.Transport;

/// <summary>Read-only family transport schedule. A guardian must have a live tenant-owned
/// relationship to the student; shared Person identity never grants transport access.</summary>
public sealed class TransportFamilyScheduleService : ITransportFamilyScheduleService
{
    private readonly EduOSDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;

    public TransportFamilyScheduleService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    {
        _db = db; _user = user; _clock = clock;
    }

    private bool Allowed => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsInRole("Student") || _user.IsInRole("Guardian") || _user.IsInRole("Parent"));

    private bool ValidDate(DateOnly date)
    {
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        return date >= today && date <= today.AddDays(30);
    }

    private DateOnly ResolveDate(DateOnly? date) =>
        date ?? DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

    public async Task<ApiResponse<PagedResult<TransportFamilyScheduleDto>>> GetAsync(
        DateOnly? operatingDate, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Allowed) return ApiResponse<PagedResult<TransportFamilyScheduleDto>>.ErrorResponse(
            "Student or guardian access is required.", 403);
        var date = ResolveDate(operatingDate);
        if (!ValidDate(date) || page < 1 || pageSize is < 1 or > 50 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<TransportFamilyScheduleDto>>.ErrorResponse(
                "Choose a date within the next 30 days and valid paging.");
        var query = BuildQuery(date);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.StudentCode).ThenBy(x => x.AssignmentId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<TransportFamilyScheduleDto>>.SuccessResponse(
            new PagedResult<TransportFamilyScheduleDto>
            {
                Items = rows.Select(Map).ToList(), TotalCount = count, Page = page, PageSize = pageSize
            });
    }

    public async Task<ApiResponse<string>> ExportCsvAsync(DateOnly? operatingDate, CancellationToken ct = default)
    {
        if (!Allowed) return ApiResponse<string>.ErrorResponse("Student or guardian access is required.", 403);
        var date = ResolveDate(operatingDate);
        if (!ValidDate(date)) return ApiResponse<string>.ErrorResponse("Choose a date within the next 30 days.");
        // One authorized, bounded SQL read; refuse truncation rather than silently dropping children.
        var rows = await BuildQuery(date).OrderBy(x => x.StudentCode).ThenBy(x => x.AssignmentId)
            .Take(501).ToListAsync(ct);
        if (rows.Count > 500)
            return ApiResponse<string>.ErrorResponse("More than 500 matching records; contact your institute.", 409);
        var csv = new StringBuilder("\uFEFFStudent code,Student,Campus,Route,Vehicle,Pickup,Drop,From,Until\r\n");
        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(",", new[]
            {
                Cell(row.StudentCode), Cell(row.StudentName), Cell(row.CampusName),
                Cell(row.RouteName), Cell(row.VehicleNumber), Cell(row.PickupStop),
                Cell(row.DropStop), Cell(row.StartDate.ToString("yyyy-MM-dd")),
                Cell(row.EndDate?.ToString("yyyy-MM-dd"))
            }));
        }
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }

    private static string Cell(string? value)
    {
        var clean = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        if (clean.Length > 0 && (clean[0] is '=' or '+' or '-' or '@' or '\t'))
            clean = "'" + clean;
        return "\"" + clean.Replace("\"", "\"\"") + "\"";
    }

    private static TransportFamilyScheduleDto Map(ScheduleProjection row) => new()
    {
        StudentName = row.StudentName, StudentCode = row.StudentCode,
        CampusName = row.CampusName, RouteName = row.RouteName,
        VehicleNumber = row.VehicleNumber, PickupStop = row.PickupStop,
        DropStop = row.DropStop, StartDate = row.StartDate, EndDate = row.EndDate
    };

    private IQueryable<ScheduleProjection> BuildQuery(DateOnly date)
    {
        var tenant = _user.TenantId;
        var ownStudents = _db.Students.AsNoTracking().Where(s =>
            s.TenantId == tenant && !s.IsDeleted && s.StatusCode == "Active" &&
            s.UserId == _user.UserId).Select(s => s.Id);
        var linkedStudents = from link in _db.StudentGuardians.AsNoTracking()
                             join guardian in _db.Guardians.AsNoTracking()
                                 on link.GuardianId equals guardian.Id
                             where link.TenantId == tenant && !link.IsDeleted &&
                                   guardian.TenantId == tenant && !guardian.IsDeleted &&
                                   guardian.IsActive && guardian.UserId == _user.UserId
                             select link.StudentId;
        IQueryable<long> visibleStudents = _user.IsInRole("Student")
            ? ((_user.IsInRole("Guardian") || _user.IsInRole("Parent")) ? ownStudents.Union(linkedStudents) : ownStudents)
            : linkedStudents;

        return from assignment in _db.StudentTransports.AsNoTracking()
               join student in _db.Students.AsNoTracking() on assignment.StudentId equals student.Id
               join enrollment in _db.StudentEnrollments.AsNoTracking()
                   on assignment.StudentEnrollmentId equals enrollment.Id
               join campus in _db.Campuses.AsNoTracking() on enrollment.CampusId equals campus.Id
               join route in _db.Routes.AsNoTracking() on assignment.RouteId equals route.Id
               join vehicle in _db.Vehicles.AsNoTracking() on assignment.VehicleId equals vehicle.Id
               where assignment.TenantId == tenant && !assignment.IsDeleted &&
                     student.TenantId == tenant && !student.IsDeleted &&
                     student.StatusCode == "Active" && visibleStudents.Contains(student.Id) &&
                     enrollment.TenantId == tenant && !enrollment.IsDeleted &&
                     enrollment.StudentId == student.Id && enrollment.IsCurrent &&
                     enrollment.State == EnrollmentState.Active &&
                     campus.TenantId == tenant && !campus.IsDeleted && campus.IsActive &&
                     route.TenantId == tenant && !route.IsDeleted &&
                     vehicle.TenantId == tenant && !vehicle.IsDeleted &&
                     route.CampusId == campus.Id && vehicle.CampusId == campus.Id &&
                     assignment.State == TransportAssignmentState.Active &&
                     assignment.StartDate <= date &&
                     (!assignment.EndDate.HasValue || assignment.EndDate.Value >= date)
               select new ScheduleProjection
               {
                   AssignmentId = assignment.Id,
                   StudentCode = student.StudentCode, StudentName = student.FullName,
                   CampusName = campus.Name, RouteName = route.Name,
                   VehicleNumber = vehicle.VehicleNumber,
                   PickupStop = _db.RouteStops.Where(stop =>
                       stop.TenantId == tenant && !stop.IsDeleted &&
                       stop.RouteId == route.Id && stop.Id == assignment.PickupStopId)
                       .Select(stop => stop.Name).FirstOrDefault(),
                   DropStop = _db.RouteStops.Where(stop =>
                       stop.TenantId == tenant && !stop.IsDeleted &&
                       stop.RouteId == route.Id && stop.Id == assignment.DropStopId)
                       .Select(stop => stop.Name).FirstOrDefault(),
                   StartDate = assignment.StartDate, EndDate = assignment.EndDate
               };
    }

    private sealed class ScheduleProjection
    {
        public long AssignmentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public string StudentCode { get; set; } = string.Empty;
        public string CampusName { get; set; } = string.Empty;
        public string RouteName { get; set; } = string.Empty;
        public string VehicleNumber { get; set; } = string.Empty;
        public string? PickupStop { get; set; }
        public string? DropStop { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
    }
}
