using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text;

namespace EduOS.Service.Services.Transport;

/// <summary>Tenant/campus-scoped operational incident and maintenance register.
/// It does not create financial vouchers or assert that a vehicle is roadworthy.</summary>
public sealed class TransportVehicleCaseService : ITransportVehicleCaseService
{
    private readonly EduOSDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<TransportVehicleCaseService> _logger;

    public TransportVehicleCaseService(EduOSDbContext db, ICurrentUserService user,
        TimeProvider clock, ILogger<TransportVehicleCaseService> logger)
    {
        _db = db; _user = user; _clock = clock; _logger = logger;
    }

    private bool CanManage => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("TransportManager"));

    private IQueryable<long> AllowedCampuses() =>
        _db.UserCampusAccesses.AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted &&
                x.UserId == _user.UserId && x.IsActive)
            .Select(x => x.CampusId);

    private IQueryable<TransportVehicleCaseRowDto> Query()
    {
        var tenant = _user.TenantId;
        var allowed = AllowedCampuses();
        return from item in _db.TransportVehicleCases.AsNoTracking()
               join vehicle in _db.Vehicles.AsNoTracking() on item.VehicleId equals vehicle.Id
               join campus in _db.Campuses.AsNoTracking() on item.CampusId equals campus.Id
               where item.TenantId == tenant && !item.IsDeleted &&
                     vehicle.TenantId == tenant && !vehicle.IsDeleted &&
                     campus.TenantId == tenant && !campus.IsDeleted &&
                     (_user.IsTenantAdmin || allowed.Contains(item.CampusId))
               select new TransportVehicleCaseRowDto
               {
                   Reference = item.PublicId, VehicleReference = vehicle.PublicId,
                   CampusId = item.CampusId, CampusName = campus.Name,
                   VehicleNumber = item.VehicleNumberSnapshot,
                   OccurredOn = item.OccurredOn, CaseType = item.CaseType,
                   Severity = item.Severity, Summary = item.Summary, State = item.State,
                   Resolution = item.Resolution, ResolvedAt = item.ResolvedAt,
                   RowVersion = Convert.ToBase64String(item.RowVersion)
               };
    }

    public async Task<ApiResponse<PagedResult<TransportVehicleCaseRowDto>>> GetAsync(
        long? campusId, string? state, string? search, int page, int pageSize,
        CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<PagedResult<TransportVehicleCaseRowDto>>.ErrorResponse("Transport permission required.", 403);
        var mode = (state ?? "All").Trim();
        if (campusId is <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue || search?.Trim().Length > 100 ||
            mode is not ("All" or "Open" or "Resolved"))
            return ApiResponse<PagedResult<TransportVehicleCaseRowDto>>.ErrorResponse("Invalid case filters.");
        var query = Query();
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        if (mode != "All") query = query.Where(x => x.State == mode);
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
            query = query.Where(x => x.VehicleNumber.Contains(term) ||
                x.Summary.Contains(term) || x.CampusName.Contains(term));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.OccurredOn)
            .ThenBy(x => x.CampusName).ThenBy(x => x.VehicleNumber).ThenBy(x => x.Reference)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<TransportVehicleCaseRowDto>>.SuccessResponse(
            new PagedResult<TransportVehicleCaseRowDto>
            {
                Items = rows, TotalCount = count, Page = page, PageSize = pageSize
            });
    }

    public async Task<ApiResponse<TransportVehicleCaseRowDto>> CreateAsync(
        CreateTransportVehicleCaseRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Transport permission required.", 403);
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.VehicleReference == Guid.Empty ||
            request.OccurredOn < today.AddYears(-2) || request.OccurredOn > today ||
            request.CaseType is not ("Maintenance" or "Incident") ||
            request.Severity is not ("Low" or "Medium" or "High") ||
            string.IsNullOrWhiteSpace(request.Summary) || request.Summary.Trim().Length > 500)
            return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Valid vehicle, date, type, severity and summary are required.");
        var tenant = _user.TenantId;
        var summary = request.Summary.Trim();
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var prior = await _db.TransportVehicleCases.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant &&
                        x.ClientRequestId == request.ClientRequestId, token);
                if (prior != null)
                {
                    if (!_user.IsTenantAdmin &&
                        !await AllowedCampuses().AnyAsync(x => x == prior.CampusId, token))
                        return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Campus access required.", 403);
                    if (prior.CaseType != request.CaseType || prior.Severity != request.Severity ||
                        prior.OccurredOn != request.OccurredOn || prior.Summary != summary)
                        return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Request key reused with different case details.", 409);
                    var vehicleMatch = await _db.Vehicles.AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == prior.VehicleId &&
                        x.PublicId == request.VehicleReference && !x.IsDeleted, token);
                    if (!vehicleMatch) return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Request key belongs to another vehicle.", 409);
                    var replay = await Query().FirstOrDefaultAsync(x => x.Reference == prior.PublicId, token);
                    return replay == null
                        ? ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Case is not available to this campus.", 403)
                        : ApiResponse<TransportVehicleCaseRowDto>.SuccessResponse(replay, "Existing case returned; no duplicate created.");
                }
                var vehicle = await _db.Vehicles.AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                        x.PublicId == request.VehicleReference && x.CampusId.HasValue)
                    .Select(x => new { x.Id, CampusId = x.CampusId!.Value, x.VehicleNumber })
                    .FirstOrDefaultAsync(token);
                if (vehicle == null) return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Vehicle not found.", 404);
                var campusOk = await _db.Campuses.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsActive && x.Id == vehicle.CampusId, token);
                if (!campusOk || (!_user.IsTenantAdmin &&
                    !await AllowedCampuses().AnyAsync(x => x == vehicle.CampusId, token)))
                    return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Campus access required.", 403);
                var now = _clock.GetUtcNow().UtcDateTime;
                var item = new TransportVehicleCase
                {
                    TenantId = tenant, CampusId = vehicle.CampusId, VehicleId = vehicle.Id,
                    VehicleNumberSnapshot = vehicle.VehicleNumber,
                    PublicId = Guid.NewGuid(), ClientRequestId = request.ClientRequestId,
                    OccurredOn = request.OccurredOn, CaseType = request.CaseType,
                    Severity = request.Severity, Summary = summary, State = "Open",
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                _db.TransportVehicleCases.Add(item);
                await _db.SaveChangesAsync(token);
                var created = await Query().FirstAsync(x => x.Reference == item.PublicId, token);
                return new ApiResponse<TransportVehicleCaseRowDto>
                {
                    Success = true, StatusCode = 201, Data = created, Message = "Vehicle case recorded."
                };
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Transport vehicle case conflict in tenant {TenantId}", tenant);
            return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Case conflicts with another write.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Transport vehicle case creation failed in tenant {TenantId}", tenant);
            return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Case could not be recorded.", 500);
        }
    }

    public async Task<ApiResponse<TransportVehicleCaseRowDto>> ResolveAsync(
        Guid reference, ResolveTransportVehicleCaseRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Transport permission required.", 403);
        byte[] version;
        try { version = Convert.FromBase64String(request?.RowVersion ?? ""); }
        catch (FormatException) { return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Invalid row version."); }
        if (reference == Guid.Empty || version.Length != 8 ||
            string.IsNullOrWhiteSpace(request?.Resolution) || request.Resolution.Trim().Length > 1000)
            return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Valid case, row version and resolution are required.");
        var tenant = _user.TenantId;
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var item = await _db.TransportVehicleCases.FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.PublicId == reference && !x.IsDeleted, token);
                if (item == null) return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Case not found.", 404);
                if (!_user.IsTenantAdmin && !await AllowedCampuses().AnyAsync(x => x == item.CampusId, token))
                    return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Campus access required.", 403);
                if (!item.RowVersion.SequenceEqual(version))
                    return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Case changed. Reload before resolving.", 409);
                if (item.State != "Open")
                    return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Only open cases can be resolved.", 409);
                var vehicleOk = await _db.Vehicles.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == item.VehicleId && !x.IsDeleted &&
                    x.CampusId == item.CampusId, token);
                if (!vehicleOk) return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Vehicle campus changed. Manual review required.", 409);
                _db.Entry(item).Property(x => x.RowVersion).OriginalValue = version;
                item.State = "Resolved";
                item.Resolution = request.Resolution.Trim();
                item.ResolvedAt = _clock.GetUtcNow().UtcDateTime;
                item.ResolvedBy = _user.UserId;
                item.UpdatedAt = item.ResolvedAt;
                item.UpdatedBy = _user.UserId;
                await _db.SaveChangesAsync(token);
                var updated = await Query().FirstAsync(x => x.Reference == reference, token);
                return ApiResponse<TransportVehicleCaseRowDto>.SuccessResponse(updated, "Case resolved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Case changed. Reload before resolving.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Transport vehicle case resolution failed in tenant {TenantId}", tenant);
            return ApiResponse<TransportVehicleCaseRowDto>.ErrorResponse("Case resolution failed.", 500);
        }
    }

    public async Task<ApiResponse<string>> ExportCsvAsync(
        long? campusId, string? state, string? search, CancellationToken ct = default)
    {
        var response = await GetAsync(campusId, state, search, 1, 100, ct);
        if (!response.Success || response.Data == null)
            return ApiResponse<string>.ErrorResponse(response.Message, response.StatusCode);
        if (response.Data.TotalCount > 500)
            return ApiResponse<string>.ErrorResponse("More than 500 cases. Narrow the filters.", 413);
        var rows = new List<TransportVehicleCaseRowDto>(response.Data.Items);
        for (var page = 2; page <= (response.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetAsync(campusId, state, search, page, 100, ct);
            if (!next.Success || next.Data == null || next.Data.TotalCount != response.Data.TotalCount)
                return ApiResponse<string>.ErrorResponse("Cases changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        if (rows.Count != response.Data.TotalCount || rows.Select(x => x.Reference).Distinct().Count() != rows.Count)
            return ApiResponse<string>.ErrorResponse("Cases changed during export. Retry.", 409);
        var csv = new StringBuilder("\uFEFFDate,Campus,Vehicle,Type,Severity,Summary,State,Resolution,Resolved At\r\n");
        foreach (var row in rows)
            csv.Append(row.OccurredOn.ToString("yyyy-MM-dd")).Append(',')
                .Append(Cell(row.CampusName)).Append(',').Append(Cell(row.VehicleNumber)).Append(',')
                .Append(Cell(row.CaseType)).Append(',').Append(Cell(row.Severity)).Append(',')
                .Append(Cell(row.Summary)).Append(',').Append(Cell(row.State)).Append(',')
                .Append(Cell(row.Resolution)).Append(',').Append(Cell(row.ResolvedAt?.ToString("O")))
                .Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }

    private static string Cell(string? value)
    {
        var text = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        if (text.Length > 0 && (text[0] is '=' or '+' or '-' or '@' or '\t')) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
