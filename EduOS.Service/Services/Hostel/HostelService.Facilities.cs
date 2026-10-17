using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    public async Task<ApiResponse<IReadOnlyList<HostelFacilityCampusDto>>> GetFacilityCampusesAsync(CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<HostelFacilityCampusDto>>();
        var tenant = _currentUser.TenantId;
        var query = _campuses.GetQueryable().AsNoTracking().Where(c => c.TenantId == tenant && !c.IsDeleted && c.IsActive);
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(c => allowed.Contains(c.Id));
        }
        var campuses = await query.OrderBy(c => c.Name).ThenBy(c => c.Id).Take(200)
            .Select(c => new HostelFacilityCampusDto { Id = c.Id, Name = c.Name }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<HostelFacilityCampusDto>>.SuccessResponse(campuses);
    }

    public async Task<ApiResponse<PagedResult<HostelFacilityDto>>> GetFacilitiesAsync(
        long? campusId, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelFacilityDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<HostelFacilityDto>>.ErrorResponse("Invalid facility filter.");
        var tenant = _currentUser.TenantId;
        var query = from hostel in _hostels.GetQueryable().AsNoTracking()
            join campus in _campuses.GetQueryable().AsNoTracking() on hostel.CampusId equals campus.Id
            where hostel.TenantId == tenant && campus.TenantId == tenant &&
                !hostel.IsDeleted && !campus.IsDeleted && campus.IsActive &&
                (!campusId.HasValue || hostel.CampusId == campusId.Value)
            select new { hostel.Id, hostel.PublicId, hostel.CampusId, CampusName = campus.Name,
                hostel.Name, hostel.Code, hostel.GenderRestriction, hostel.Address, hostel.IsActive, hostel.RowVersion };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.Name.Contains(term) || x.Code.Contains(term) || x.CampusName.Contains(term));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.CampusName).ThenBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var activeRooms = await _rooms.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && ids.Contains(x.HostelId) && x.IsActive)
            .GroupBy(x => x.HostelId).Select(g => new { HostelId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.HostelId, x => x.Count, ct);
        var activeReservations = await (from allocation in _allocations.GetQueryable().AsNoTracking()
            join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
            join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
            where allocation.TenantId == tenant && bed.TenantId == tenant && room.TenantId == tenant &&
                !allocation.IsDeleted && !bed.IsDeleted && !room.IsDeleted &&
                ids.Contains(room.HostelId) && allocation.State == HostelAllocationState.Active
            group bed.Id by room.HostelId into g
            select new { HostelId = g.Key, Count = g.Distinct().Count() })
            .ToDictionaryAsync(x => x.HostelId, x => x.Count, ct);
        var occupiedBeds = await (from allocation in _allocations.GetQueryable().AsNoTracking()
            join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
            join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
            where allocation.TenantId == tenant && bed.TenantId == tenant && room.TenantId == tenant &&
                !allocation.IsDeleted && !bed.IsDeleted && !room.IsDeleted &&
                ids.Contains(room.HostelId) && allocation.State == HostelAllocationState.Active &&
                allocation.StartDate <= today && (!allocation.EndDate.HasValue || allocation.EndDate.Value >= today)
            group bed.Id by room.HostelId into g
            select new { HostelId = g.Key, Count = g.Distinct().Count() })
            .ToDictionaryAsync(x => x.HostelId, x => x.Count, ct);
        return ApiResponse<PagedResult<HostelFacilityDto>>.SuccessResponse(new PagedResult<HostelFacilityDto>
        {
            Items = rows.Select(x => new HostelFacilityDto
            {
                Reference = x.PublicId, CampusId = x.CampusId, CampusName = x.CampusName,
                Name = x.Name, Code = x.Code, GenderRestriction = x.GenderRestriction, Address = x.Address,
                IsActive = x.IsActive, ActiveRooms = activeRooms.GetValueOrDefault(x.Id),
                OccupiedBeds = occupiedBeds.GetValueOrDefault(x.Id),
                ActiveReservations = activeReservations.GetValueOrDefault(x.Id),
                RowVersion = Convert.ToBase64String(x.RowVersion)
            }).ToList(), TotalCount = count, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<HostelFacilityDto>> CreateFacilityAsync(CreateHostelFacilityRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<HostelFacilityDto>();
        if (request == null || request.CampusId <= 0 || string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Code) || request.Name.Trim().Length is < 2 or > 150 ||
            request.Code.Trim().Length is < 2 or > 50 || request.Address?.Trim().Length > 500 ||
            request.GenderRestriction?.Trim().Length > 50)
            return ApiResponse<HostelFacilityDto>.ErrorResponse("Invalid facility information.");
        var name = request.Name.Trim();
        var code = request.Code.Trim().ToUpperInvariant();
        var gender = string.IsNullOrWhiteSpace(request.GenderRestriction) ? null : request.GenderRestriction.Trim();
        var address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        if (gender != null && gender is not ("Male" or "Female" or "Other"))
            return ApiResponse<HostelFacilityDto>.ErrorResponse("Invalid gender restriction.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var campus = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.CampusId && !x.IsDeleted && x.IsActive, token);
                if (campus == null) return ApiResponse<HostelFacilityDto>.ErrorResponse("Active campus not found.", 404);
                if (!await CanManageCampusAsync(campus.Id, tenant, token)) return Denied<HostelFacilityDto>();
                var existing = await _hostels.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.CampusId == campus.Id && !x.IsDeleted && x.Code == code, token);
                if (existing != null)
                {
                    if (existing.Name != name || existing.GenderRestriction != gender || existing.Address != address)
                        return ApiResponse<HostelFacilityDto>.ErrorResponse("Facility code already exists with different details.", 409);
                    return ApiResponse<HostelFacilityDto>.SuccessResponse(new HostelFacilityDto
                    {
                        Reference = existing.PublicId, CampusId = campus.Id, CampusName = campus.Name,
                        Name = existing.Name, Code = existing.Code, GenderRestriction = existing.GenderRestriction,
                        Address = existing.Address, IsActive = existing.IsActive,
                        RowVersion = Convert.ToBase64String(existing.RowVersion)
                    }, "Facility already exists; no duplicate created.");
                }
                var hostel = new EduOS.Core.Entities.Hostel.Hostel
                {
                    TenantId = tenant, CampusId = campus.Id, Name = name, Code = code,
                    GenderRestriction = gender, Address = address, IsActive = true,
                    CreatedAt = _clock.GetUtcNow().UtcDateTime, CreatedBy = _currentUser.UserId
                };
                await _hostels.AddAsync(hostel);
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<HostelFacilityDto>.SuccessResponse(new HostelFacilityDto
                {
                    Reference = hostel.PublicId, CampusId = campus.Id, CampusName = campus.Name,
                    Name = hostel.Name, Code = hostel.Code, GenderRestriction = hostel.GenderRestriction,
                    Address = hostel.Address, IsActive = hostel.IsActive,
                    RowVersion = Convert.ToBase64String(hostel.RowVersion)
                }, "Facility created.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel facility create conflict for tenant {TenantId}", tenant);
            return ApiResponse<HostelFacilityDto>.ErrorResponse("Facility creation conflicts with an existing record.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hostel facility creation failed for tenant {TenantId}", tenant);
            return ApiResponse<HostelFacilityDto>.ErrorResponse("Facility creation failed.", 500);
        }
    }

    public async Task<ApiResponse<HostelFacilityDto>> SetFacilityStateAsync(Guid reference,
        SetHostelFacilityStateRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<HostelFacilityDto>();
        if (reference == Guid.Empty || request == null || !TryDecodeVersion(request.RowVersion, out var version) || version.Length != 8)
            return ApiResponse<HostelFacilityDto>.ErrorResponse("Facility and valid row version are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var hostel = await _hostels.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference, token);
                if (hostel == null) return ApiResponse<HostelFacilityDto>.ErrorResponse("Facility not found.", 404);
                var campus = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == hostel.CampusId && !x.IsDeleted && x.IsActive, token);
                if (campus == null) return ApiResponse<HostelFacilityDto>.ErrorResponse("Active campus not found.", 404);
                if (!await CanManageCampusAsync(campus.Id, tenant, token)) return Denied<HostelFacilityDto>();
                var activeRooms = await _rooms.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && x.HostelId == hostel.Id && !x.IsDeleted && x.IsActive, token);
                var activeReservations = await (from allocation in _allocations.GetQueryable().AsNoTracking()
                    join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
                    join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                    where allocation.TenantId == tenant && bed.TenantId == tenant && room.TenantId == tenant &&
                        !allocation.IsDeleted && !bed.IsDeleted && !room.IsDeleted &&
                        room.HostelId == hostel.Id && allocation.State == HostelAllocationState.Active
                    select bed.Id).Distinct().CountAsync(token);
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                var occupiedToday = await (from allocation in _allocations.GetQueryable().AsNoTracking()
                    join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
                    join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                    where allocation.TenantId == tenant && bed.TenantId == tenant && room.TenantId == tenant &&
                        !allocation.IsDeleted && !bed.IsDeleted && !room.IsDeleted &&
                        room.HostelId == hostel.Id && allocation.State == HostelAllocationState.Active &&
                        allocation.StartDate <= today && (!allocation.EndDate.HasValue || allocation.EndDate.Value >= today)
                    select bed.Id).Distinct().CountAsync(token);
                HostelFacilityDto Result() => new()
                {
                    Reference = hostel.PublicId, CampusId = campus.Id, CampusName = campus.Name,
                    Name = hostel.Name, Code = hostel.Code, GenderRestriction = hostel.GenderRestriction,
                    Address = hostel.Address, IsActive = hostel.IsActive, ActiveRooms = activeRooms,
                    OccupiedBeds = occupiedToday, ActiveReservations = activeReservations,
                    RowVersion = Convert.ToBase64String(hostel.RowVersion)
                };
                if (hostel.IsActive == request.IsActive)
                    return ApiResponse<HostelFacilityDto>.SuccessResponse(Result(), "Facility already has the requested state.");
                if (!hostel.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<HostelFacilityDto>.ErrorResponse("Facility changed. Reload and retry.", 409);
                if (!request.IsActive && (activeRooms > 0 || activeReservations > 0))
                    return ApiResponse<HostelFacilityDto>.ErrorResponse("Close active rooms and allocations before deactivating facility.", 409);
                hostel.IsActive = request.IsActive;
                hostel.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                hostel.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<HostelFacilityDto>.SuccessResponse(Result(), "Facility state updated.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<HostelFacilityDto>.ErrorResponse("Facility changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel facility state conflict for tenant {TenantId}", tenant);
            return ApiResponse<HostelFacilityDto>.ErrorResponse("Facility state conflicts with another change.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hostel facility state update failed for tenant {TenantId}", tenant);
            return ApiResponse<HostelFacilityDto>.ErrorResponse("Facility state update failed.", 500);
        }
    }
}
