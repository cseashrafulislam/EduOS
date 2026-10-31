using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService : IHostelService
{
    private readonly IGenericRepository<EduOS.Core.Entities.Hostel.Hostel> _hostels;
    private readonly IGenericRepository<HostelRoom> _rooms;
    private readonly IGenericRepository<HostelBed> _beds;
    private readonly IGenericRepository<StudentHostelAllocation> _allocations;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<UserCampusAccess> _campusAccesses;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<HostelApplication> _applications;
    private readonly IGenericRepository<HostelApplicationEvent> _applicationEvents;
    private readonly IGenericRepository<HostelApplicationAuthority> _applicationAuthorities;
    private readonly IGenericRepository<StudentGuardian> _studentGuardians;
    private readonly IGenericRepository<Guardian> _guardians;
    private readonly IGenericRepository<AuditLog> _auditLogs;
    private readonly IGenericRepository<HostelFinanceClearance> _hostelClearances;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReservationFinancePolicy _reservationFinancePolicy;
    private readonly IReservationCommandReceiptStore _commandReceipts;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<HostelService> _logger;

    public HostelService(IGenericRepository<EduOS.Core.Entities.Hostel.Hostel> hostels, IGenericRepository<HostelRoom> rooms,
        IGenericRepository<HostelBed> beds, IGenericRepository<StudentHostelAllocation> allocations,
        IGenericRepository<StudentEnrollment> enrollments, IGenericRepository<Student> students,
        IGenericRepository<UserCampusAccess> campusAccesses, IGenericRepository<Campus> campuses,
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, TimeProvider clock,
        ILogger<HostelService> logger, IReservationFinancePolicy reservationFinancePolicy,
        IReservationCommandReceiptStore commandReceipts,
        IGenericRepository<HostelApplication> applications,
        IGenericRepository<HostelApplicationEvent> applicationEvents,
        IGenericRepository<HostelApplicationAuthority> applicationAuthorities,
        IGenericRepository<StudentGuardian> studentGuardians, IGenericRepository<Guardian> guardians,
        IGenericRepository<AuditLog> auditLogs,
        IGenericRepository<HostelFinanceClearance> hostelClearances)
    {
        _hostels = hostels; _rooms = rooms; _beds = beds; _allocations = allocations;
        _enrollments = enrollments; _students = students; _campusAccesses = campusAccesses;
        _campuses = campuses; _unitOfWork = unitOfWork;
        _currentUser = currentUser; _clock = clock; _logger = logger;
        _reservationFinancePolicy = reservationFinancePolicy;
        _commandReceipts = commandReceipts;
        _applications = applications; _applicationEvents = applicationEvents;
        _applicationAuthorities = applicationAuthorities; _studentGuardians = studentGuardians; _guardians = guardians;
        _auditLogs = auditLogs;
        _hostelClearances = hostelClearances;
    }


    /// <summary>Administrative bed inventory: bounded, tenant/campus-filtered, no student or finance data.</summary>
    public async Task<ApiResponse<PagedResult<HostelBedInventoryDto>>> GetBedInventoryAsync(
        long? campusId, int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelBedInventoryDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<HostelBedInventoryDto>>.ErrorResponse("Invalid bed inventory filter.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var query = from bed in _beds.GetQueryable().AsNoTracking()
            join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
            join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
            join campus in _campuses.GetQueryable().AsNoTracking() on hostel.CampusId equals campus.Id
            where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                campus.TenantId == tenant && campus.IsActive &&
                (!campusId.HasValue || hostel.CampusId == campusId.Value)
            select new
            {
                bed.Id, bed.HostelRoomId, bed.BedNumber, bed.IsActive, bed.RowVersion,
                room.RoomNumber, HostelName = hostel.Name, hostel.CampusId,
                IsOccupied = _allocations.GetQueryable().Any(a => a.TenantId == tenant &&
                    a.HostelBedId == bed.Id && a.State == HostelAllocationState.Active &&
                    a.StartDate <= today && (!a.EndDate.HasValue || a.EndDate.Value >= today)),
                HasActiveReservations = _allocations.GetQueryable().Any(a => a.TenantId == tenant &&
                    a.HostelBedId == bed.Id && a.State == HostelAllocationState.Active &&
                    (!a.EndDate.HasValue || a.EndDate.Value >= today)),
                HasAllocationHistory = _allocations.GetQueryable().IgnoreQueryFilters().Any(a =>
                    a.TenantId == tenant && a.HostelBedId == bed.Id)
            };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.HostelName.Contains(term) ||
                x.RoomNumber.Contains(term) || x.BedNumber.Contains(term));
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(x => x.HostelName).ThenBy(x => x.RoomNumber)
            .ThenBy(x => x.BedNumber).ThenBy(x => x.Id).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<HostelBedInventoryDto>>.SuccessResponse(new PagedResult<HostelBedInventoryDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = rows.Select(x => new HostelBedInventoryDto
            {
                Id = x.Id, HostelRoomId = x.HostelRoomId, HostelName = x.HostelName,
                RoomNumber = x.RoomNumber, BedNumber = x.BedNumber,
                IsActive = x.IsActive, IsOccupied = x.IsOccupied,
                HasActiveReservations = x.HasActiveReservations,
                HasAllocationHistory = x.HasAllocationHistory,
                RowVersion = Convert.ToBase64String(x.RowVersion)
            }).ToList()
        });
    }

    /// <summary>Controlled bed activation/closure. Occupied beds cannot be closed.</summary>
    public async Task<ApiResponse<HostelBedInventoryDto>> SetBedStateAsync(long bedId,
        SetHostelBedStateRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<HostelBedInventoryDto>();
        if (bedId <= 0 || request == null || !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed and row version are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var bed = await _beds.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == bedId, ct);
                if (bed == null) return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed not found.", 404);
                var room = await _rooms.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == bed.HostelRoomId, ct);
                if (room == null) return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Room not found.", 404);
                var hostel = await _hostels.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == room.HostelId, ct);
                if (hostel == null || !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == hostel.CampusId && x.IsActive, ct))
                    return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Active campus not found.", 404);
                if (!await CanManageCampusAsync(hostel.CampusId, tenant, ct))
                    return Denied<HostelBedInventoryDto>();
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                var reserved = await _allocations.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.HostelBedId == bed.Id &&
                    x.State == HostelAllocationState.Active &&
                    (!x.EndDate.HasValue || x.EndDate.Value >= today), ct);
                var occupiedToday = await _allocations.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.HostelBedId == bed.Id &&
                    x.State == HostelAllocationState.Active &&
                    x.StartDate <= today && (!x.EndDate.HasValue || x.EndDate.Value >= today), ct);
                HostelBedInventoryDto Result() => new()
                {
                    Id = bed.Id, HostelRoomId = bed.HostelRoomId, HostelName = hostel.Name,
                    RoomNumber = room.RoomNumber, BedNumber = bed.BedNumber,
                    IsActive = bed.IsActive, IsOccupied = occupiedToday,
                    HasActiveReservations = reserved,
                    RowVersion = Convert.ToBase64String(bed.RowVersion)
                };
                // Same desired state is an idempotent replay; never overwrite unrelated fields.
                if (bed.IsActive == request.IsActive)
                    return ApiResponse<HostelBedInventoryDto>.SuccessResponse(Result(), "Bed already has the requested state.");
                if (!bed.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed changed. Reload and retry.", 409);
                if (!request.IsActive && reserved)
                    return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Close active allocation before deactivating bed.", 409);
                if (request.IsActive)
                {
                    if (!room.IsActive || !hostel.IsActive || room.Capacity < 1)
                        return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Hostel room is inactive or has no capacity.", 409);
                    var reservedBedSlots = await _beds.GetQueryable().AsNoTracking().CountAsync(x =>
                        x.TenantId == tenant && x.HostelRoomId == room.Id &&
                        (x.IsActive || _allocations.GetQueryable().Any(a => a.TenantId == tenant &&
                            a.HostelBedId == x.Id && a.State == HostelAllocationState.Active)), ct);
                    if (reservedBedSlots >= room.Capacity)
                        return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Room bed capacity reached.", 409);
                }
                bed.IsActive = request.IsActive;
                bed.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                bed.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(ct);
                return ApiResponse<HostelBedInventoryDto>.SuccessResponse(Result(), "Bed state updated.");
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel bed state conflict for tenant {TenantId}", tenant);
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed state conflicts with another change.", 409);
        }
        catch (Exception ex) when (ex is System.Transactions.TransactionAbortedException || HasHostelSqlSerializationConflict(ex))
        {
            _logger.LogWarning(ex, "Hostel bed state SQL concurrency conflict for tenant {TenantId}", tenant);
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Concurrent hostel bed state change. Reload and retry.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hostel bed state update failed for tenant {TenantId}", tenant);
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed state update failed.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<HostelRoomDto>>> GetRoomsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<HostelRoomDto>>();
        var tenant = _currentUser.TenantId;
        var roomsQuery = from room in _rooms.GetQueryable().AsNoTracking()
            join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
            where room.TenantId == tenant && hostel.TenantId == tenant && room.IsActive && hostel.IsActive &&
                _campuses.GetQueryable().Any(c => c.TenantId == tenant && c.Id == hostel.CampusId && c.IsActive)
            orderby hostel.Name, room.RoomNumber
            select new { Room = room, HostelName = hostel.Name, HostelReference = hostel.PublicId, hostel.CampusId };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            roomsQuery = roomsQuery.Where(x => allowed.Contains(x.CampusId));
        }
        // Compatibility endpoint: fail explicitly for large catalogues instead of materializing
        // every room and building an unbounded SQL IN list. The manager UI uses paged inventory.
        var roomCount = await roomsQuery.CountAsync(cancellationToken);
        if (roomCount > 100)
            return ApiResponse<IReadOnlyList<HostelRoomDto>>.ErrorResponse(
                "Room summary exceeds 100 rooms. Use the paged room inventory.", 409);
        var data = await roomsQuery.Take(100).ToListAsync(cancellationToken);
        var roomIds = data.Select(x => x.Room.Id).ToArray();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var occupied = await (from allocation in _allocations.GetQueryable().AsNoTracking()
            join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
            where allocation.TenantId == tenant && bed.TenantId == tenant &&
                allocation.State == HostelAllocationState.Active &&
                allocation.StartDate <= today && (!allocation.EndDate.HasValue || allocation.EndDate.Value >= today) &&
                roomIds.Contains(bed.HostelRoomId)
            group bed.Id by bed.HostelRoomId into g
            select new { RoomId = g.Key, Count = g.Distinct().Count() })
            .ToDictionaryAsync(x => x.RoomId, x => x.Count, cancellationToken);
        IReadOnlyList<HostelRoomDto> rows = data.Select(x => new HostelRoomDto
        {
            Id = x.Room.Id, HostelReference = x.HostelReference, HostelName = x.HostelName,
            RoomNumber = x.Room.RoomNumber, Capacity = x.Room.Capacity, RentPerBed = x.Room.RentPerBed,
            ActiveAllocations = occupied.GetValueOrDefault(x.Room.Id), IsActive = x.Room.IsActive,
            RowVersion = Convert.ToBase64String(x.Room.RowVersion)
        }).ToList();
        return ApiResponse<IReadOnlyList<HostelRoomDto>>.SuccessResponse(rows);
    }


    /// <summary>Privacy-minimized, tenant/campus-scoped physical room capacity report.</summary>
    public async Task<ApiResponse<PagedResult<HostelCapacityRowDto>>> GetCapacityAsync(
        long? campusId, int page, int pageSize, string? search, CancellationToken cancellationToken = default, bool attentionOnly = false)
    {
        if (!CanManage()) return Denied<PagedResult<HostelCapacityRowDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<HostelCapacityRowDto>>.ErrorResponse("Invalid report filter.");
        if (attentionOnly) return await GetCapacityRiskAsync(campusId, page, pageSize, search, cancellationToken);
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var term = search?.Trim();
        var query = from room in _rooms.GetQueryable().AsNoTracking()
                    join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
                    join campus in _campuses.GetQueryable().AsNoTracking() on hostel.CampusId equals campus.Id
                    where room.TenantId == tenant && hostel.TenantId == tenant && campus.TenantId == tenant &&
                          campus.IsActive && (!campusId.HasValue || hostel.CampusId == campusId.Value)
                    select new { RoomId = room.Id, room.RoomNumber, room.Capacity,
                        RoomActive = room.IsActive, HostelName = hostel.Name,
                        HostelActive = hostel.IsActive, hostel.CampusId };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.HostelName.Contains(term) || x.RoomNumber.Contains(term));
        var total = await query.CountAsync(cancellationToken);
        var rooms = await query.OrderBy(x => x.HostelName).ThenBy(x => x.RoomNumber).ThenBy(x => x.RoomId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var ids = rooms.Select(x => x.RoomId).ToArray();
        var beds = await _beds.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && ids.Contains(x.HostelRoomId))
            .GroupBy(x => x.HostelRoomId)
            .Select(g => new { RoomId = g.Key, Total = g.Count(), Active = g.Count(b => b.IsActive) })
            .ToDictionaryAsync(x => x.RoomId, cancellationToken);
        var occupied = await (from allocation in _allocations.GetQueryable().AsNoTracking()
            join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
            where allocation.TenantId == tenant && bed.TenantId == tenant &&
                ids.Contains(bed.HostelRoomId) && allocation.State == HostelAllocationState.Active &&
                allocation.StartDate <= today &&
                (!allocation.EndDate.HasValue || allocation.EndDate.Value >= today)
            group allocation.HostelBedId by bed.HostelRoomId into g
            select new { RoomId = g.Key, Occupied = g.Distinct().Count(), Records = g.Count() })
            .ToDictionaryAsync(x => x.RoomId, cancellationToken);
        var inactiveOccupied = await (from allocation in _allocations.GetQueryable().AsNoTracking()
            join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
            where allocation.TenantId == tenant && bed.TenantId == tenant && !bed.IsActive &&
                ids.Contains(bed.HostelRoomId) && allocation.State == HostelAllocationState.Active &&
                allocation.StartDate <= today &&
                (!allocation.EndDate.HasValue || allocation.EndDate.Value >= today)
            group allocation.HostelBedId by bed.HostelRoomId into g
            select new { RoomId = g.Key, Count = g.Distinct().Count() })
            .ToDictionaryAsync(x => x.RoomId, x => x.Count, cancellationToken);
        var items = new List<HostelCapacityRowDto>(rooms.Count);
        foreach (var room in rooms)
        {
            beds.TryGetValue(room.RoomId, out var bedInfo);
            occupied.TryGetValue(room.RoomId, out var occupancy);
            var activeBeds = bedInfo?.Active ?? 0;
            var inactiveBeds = (bedInfo?.Total ?? 0) - activeBeds;
            var occupiedBeds = occupancy?.Occupied ?? 0;
            var inactiveReserved = inactiveOccupied.GetValueOrDefault(room.RoomId);
            var activeOccupied = occupiedBeds - inactiveReserved;
            var usable = room.RoomActive && room.HostelActive
                ? Math.Min(activeBeds, Math.Max(0, room.Capacity - inactiveReserved)) : 0;
            items.Add(new HostelCapacityRowDto
            {
                RoomId = room.RoomId, HostelName = room.HostelName, RoomNumber = room.RoomNumber,
                Capacity = room.Capacity, ActiveBeds = activeBeds, InactiveBeds = inactiveBeds,
                OccupiedBeds = occupiedBeds, AvailableBeds = Math.Max(0, usable - activeOccupied),
                HostelActive = room.HostelActive, RoomActive = room.RoomActive,
                HasRisk = occupiedBeds > room.Capacity || inactiveReserved > 0 ||
                    (occupancy != null && occupancy.Records > occupiedBeds) ||
                    (occupiedBeds > 0 && (!room.HostelActive || !room.RoomActive)) ||
                    (room.HostelActive && room.RoomActive && activeBeds > room.Capacity) ||
                    (room.HostelActive && room.RoomActive && room.Capacity > 0 && activeBeds == 0)
            });
        }
        return ApiResponse<PagedResult<HostelCapacityRowDto>>.SuccessResponse(new PagedResult<HostelCapacityRowDto>
        {
            Items = items, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<PagedResult<HostelStudentOptionDto>>> GetEligibleStudentsAsync(
        int page, int pageSize, string? search, CancellationToken cancellationToken = default,
        DateOnly? startDate = null, DateOnly? endDate = null)
    {
        if (!CanManage()) return Denied<PagedResult<HostelStudentOptionDto>>();
        if (page < 1 || pageSize is < 1 or > 100) return ApiResponse<PagedResult<HostelStudentOptionDto>>.ErrorResponse("Invalid paging parameters.");
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var reservationStart = startDate ?? today;
        if (reservationStart < today || reservationStart > today.AddDays(365) || !IsValidFiniteBookingPeriod(reservationStart, endDate, today))
            return ApiResponse<PagedResult<HostelStudentOptionDto>>.ErrorResponse("Invalid reservation period.");
        var through = endDate ?? DateOnly.MaxValue;
        var tenant = _currentUser.TenantId;
        var query = from enrollment in _enrollments.GetQueryable().AsNoTracking()
                    join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
                    where enrollment.TenantId == tenant && student.TenantId == tenant &&
                        enrollment.IsCurrent && enrollment.State == EnrollmentState.Active && student.StatusCode == "Active" &&
                        _campuses.GetQueryable().Any(c => c.TenantId == tenant && c.Id == enrollment.CampusId && c.IsActive) &&
                        !_allocations.GetQueryable().Any(x => x.TenantId == tenant && x.StudentId == student.Id &&
                            !x.IsDeleted && x.State != HostelAllocationState.Cancelled &&
                            x.StartDate <= through && (!x.EndDate.HasValue || x.EndDate.Value >= reservationStart))
                    select new { enrollment.PublicId, enrollment.CampusId, student.FullName, student.StudentCode, enrollment.RollNo };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 100) return ApiResponse<PagedResult<HostelStudentOptionDto>>.ErrorResponse("Search term is too long.");
            query = query.Where(x => x.FullName.Contains(term) || x.StudentCode.Contains(term) || x.RollNo.Contains(term));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.FullName).ThenBy(x => x.StudentCode).Skip((page - 1) * pageSize)
            .Take(pageSize).Select(x => new HostelStudentOptionDto
            {
                EnrollmentReference = x.PublicId, StudentName = x.FullName,
                StudentCode = x.StudentCode, Roll = x.RollNo
            }).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<HostelStudentOptionDto>>.SuccessResponse(new PagedResult<HostelStudentOptionDto>
        {
            Items = items, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<PagedResult<HostelBedOptionDto>>> GetAvailableBedsAsync(
        int page, int pageSize, string? search, CancellationToken cancellationToken = default,
        DateOnly? startDate = null)
    {
        if (!CanManage()) return Denied<PagedResult<HostelBedOptionDto>>();
        if (page < 1 || pageSize is < 1 or > 100) return ApiResponse<PagedResult<HostelBedOptionDto>>.ErrorResponse("Invalid paging parameters.");
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var reservationStart = startDate ?? today;
        if (reservationStart < today || reservationStart > today.AddDays(365))
            return ApiResponse<PagedResult<HostelBedOptionDto>>.ErrorResponse("Invalid reservation start date.");
        var tenant = _currentUser.TenantId;
        var query = from bed in _beds.GetQueryable().AsNoTracking()
                    join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                    join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
                    where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                        bed.IsActive && room.IsActive && hostel.IsActive && room.Capacity > 0 &&
                        _campuses.GetQueryable().Any(c => c.TenantId == tenant && c.Id == hostel.CampusId && c.IsActive) &&
                        !_allocations.GetQueryable().Any(x => x.TenantId == tenant && x.HostelBedId == bed.Id &&
                            !x.IsDeleted && x.State != HostelAllocationState.Cancelled &&
                            (!x.EndDate.HasValue || x.EndDate.Value >= reservationStart)) &&
                        _allocations.GetQueryable().Count(x => x.TenantId == tenant &&
                            !x.IsDeleted && x.State != HostelAllocationState.Cancelled &&
                            (!x.EndDate.HasValue || x.EndDate.Value >= reservationStart) &&
                            _beds.GetQueryable().Any(b => b.TenantId == tenant && b.Id == x.HostelBedId &&
                                b.HostelRoomId == room.Id)) < room.Capacity
                    select new { BedId = bed.Id, bed.BedNumber, room.RoomNumber, room.RentPerBed,
                        HostelName = hostel.Name, hostel.GenderRestriction, hostel.CampusId };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 100) return ApiResponse<PagedResult<HostelBedOptionDto>>.ErrorResponse("Search term is too long.");
            query = query.Where(x => x.HostelName.Contains(term) || x.RoomNumber.Contains(term) || x.BedNumber.Contains(term));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.HostelName).ThenBy(x => x.RoomNumber).ThenBy(x => x.BedNumber)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new HostelBedOptionDto
            {
                BedId = x.BedId, HostelName = x.HostelName, RoomNumber = x.RoomNumber,
                BedNumber = x.BedNumber, RentPerBed = x.RentPerBed, GenderRestriction = x.GenderRestriction
            }).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<HostelBedOptionDto>>.SuccessResponse(new PagedResult<HostelBedOptionDto>
        {
            Items = items, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<PagedResult<HostelAllocationRowDto>>> GetActiveAllocationsAsync(
        int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelAllocationRowDto>>();
        if (page < 1 || pageSize is < 1 or > 100) return ApiResponse<PagedResult<HostelAllocationRowDto>>.ErrorResponse("Invalid paging parameters.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var query = from allocation in _allocations.GetQueryable().AsNoTracking()
                    join student in _students.GetQueryable().AsNoTracking() on allocation.StudentId equals student.Id
                    join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
                    join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                    join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
                    where allocation.TenantId == tenant && student.TenantId == tenant && bed.TenantId == tenant &&
                        room.TenantId == tenant && hostel.TenantId == tenant &&
                        allocation.State == HostelAllocationState.Active &&
                        (!allocation.EndDate.HasValue || allocation.EndDate.Value >= today)
                    select new { allocation.Id, allocation.StudentId, allocation.StudentEnrollmentId, hostel.CampusId,
                        allocation.StartDate, allocation.EndDate, allocation.RowVersion, allocation.MonthlyRent,
                        allocation.SecurityDeposit, allocation.FeeHeadId, student.FullName, student.StudentCode,
                        HostelName = hostel.Name, room.RoomNumber, bed.BedNumber };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId) &&
                _enrollments.GetQueryable().Any(e => e.TenantId == tenant &&
                    e.Id == x.StudentEnrollmentId && e.StudentId == x.StudentId && e.CampusId == x.CampusId));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 100) return ApiResponse<PagedResult<HostelAllocationRowDto>>.ErrorResponse("Search term is too long.");
            query = query.Where(x => x.FullName.Contains(term) || x.StudentCode.Contains(term));
        }
        var total = await query.CountAsync(cancellationToken);
        var data = await query.OrderBy(x => x.FullName).ThenBy(x => x.StudentCode)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var items = data.Select(x => new HostelAllocationRowDto
        {
            Id = x.Id, StudentName = x.FullName, HostelName = x.HostelName, RoomNumber = x.RoomNumber,
            BedNumber = x.BedNumber, StartDate = x.StartDate, EndDate = x.EndDate,
            CanExtendNoCharge = x.MonthlyRent == 0m && x.SecurityDeposit == 0m &&
                !x.FeeHeadId.HasValue && x.EndDate.HasValue && x.EndDate.Value >= today &&
                x.EndDate.Value.DayNumber - x.StartDate.DayNumber < 89,
            CanShortenNoCharge = x.MonthlyRent == 0m && x.SecurityDeposit == 0m &&
                !x.FeeHeadId.HasValue && x.StartDate > today && x.EndDate.HasValue &&
                x.EndDate.Value > x.StartDate,
            CanTransferNoCharge = x.MonthlyRent == 0m && x.SecurityDeposit == 0m &&
                !x.FeeHeadId.HasValue && x.StartDate < today,
            RowVersion = Convert.ToBase64String(x.RowVersion)
        }).ToList();
        return ApiResponse<PagedResult<HostelAllocationRowDto>>.SuccessResponse(new PagedResult<HostelAllocationRowDto>
        {
            Items = items, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<StudentHostelAllocationDto?>> GetMyAllocationAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<StudentHostelAllocationDto?>();
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var studentIds = _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.UserId == _currentUser.UserId && x.StatusCode == "Active").Select(x => x.Id);
        var row = await _allocations.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.State == HostelAllocationState.Active && studentIds.Contains(x.StudentId) &&
            _enrollments.GetQueryable().Any(e => e.TenantId == tenant && e.Id == x.StudentEnrollmentId &&
                e.StudentId == x.StudentId && e.IsCurrent && e.State == EnrollmentState.Active) &&
            x.StartDate <= today && (!x.EndDate.HasValue || x.EndDate >= today))
            .OrderByDescending(x => x.StartDate).FirstOrDefaultAsync(cancellationToken);
        return ApiResponse<StudentHostelAllocationDto?>.SuccessResponse(row == null ? null : await MapAsync(row, cancellationToken));
    }

    public Task<ApiResponse<StudentHostelAllocationDto>> AllocateAsync(AllocateStudentHostelRequestDto request, CancellationToken cancellationToken = default) =>
        AllocateValidatedAsync(request, false, cancellationToken);

    // Used by an accepted offer inside the caller's SERIALIZABLE unit of work. Never a second writer.
    private async Task<ApiResponse<StudentHostelAllocationDto>> AllocateValidatedAsync(
        AllocateStudentHostelRequestDto request, bool approvedOffer, CancellationToken cancellationToken)
    {
        if (!CanManage() && !approvedOffer) return Denied<StudentHostelAllocationDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.StudentEnrollmentReference == Guid.Empty ||
            request.HostelBedId <= 0) return Error("Enrollment, bed and client request references are required.");
        if (request.MonthlyRent.HasValue && request.MonthlyRent < 0) return Error("Monthly rent cannot be negative.");
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var start = request.StartDate == default ? today : request.StartDate;
        if (!IsValidFiniteBookingPeriod(start, request.EndDate, today))
            return Error("Finite hostel reservations must start within 365 days and last at most 90 inclusive days.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
            var existing = await _allocations.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.ClientRequestId == request.ClientRequestId, ct);
            if (existing != null)
            {
                if (approvedOffer)
                {
                    var ownsStay = await CanApplyForStudentAsync(existing.StudentId, tenant, ct);
                    if (!ownsStay) return Denied<StudentHostelAllocationDto>();
                }
                else if (!_currentUser.IsTenantAdmin)
                {
                    var enrollmentCampus = await _enrollments.GetQueryable().AsNoTracking()
                        .Where(x => x.TenantId == tenant && x.Id == existing.StudentEnrollmentId &&
                            x.StudentId == existing.StudentId).Select(x => (long?)x.CampusId)
                        .FirstOrDefaultAsync(ct);
                    var bedCampus = await (from bed in _beds.GetQueryable().AsNoTracking()
                        join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                        join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
                        where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                            bed.Id == existing.HostelBedId
                        select (long?)hostel.CampusId).FirstOrDefaultAsync(ct);
                    if (!enrollmentCampus.HasValue || enrollmentCampus != bedCampus ||
                        !await CanManageCampusAsync(enrollmentCampus.Value, tenant, ct))
                        return Denied<StudentHostelAllocationDto>();
                }
                var enrollmentReference = await _enrollments.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                    x.Id == existing.StudentEnrollmentId).Select(x => x.PublicId).FirstOrDefaultAsync(ct);
                if (enrollmentReference != request.StudentEnrollmentReference || existing.HostelBedId != request.HostelBedId ||
                    (request.StartDate != default && existing.StartDate != request.StartDate) ||
                    existing.EndDate != request.EndDate ||
                    (request.MonthlyRent.HasValue && existing.MonthlyRent != request.MonthlyRent.Value))
                    return Error("Client request ID was reused for different allocation data.", 409);
                var replay = await MapAsync(existing, ct);
                return ApiResponse<StudentHostelAllocationDto>.SuccessResponse(replay, "Hostel allocation already processed.");
            }
            var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == request.StudentEnrollmentReference && x.IsCurrent &&
                x.State == EnrollmentState.Active, ct);
            if (enrollment == null) return Error("Active student enrollment not found.", 404);
            if (!await _campuses.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.Id == enrollment.CampusId && x.IsActive, ct))
                return Error("Enrollment campus is inactive.", 409);
            if (!approvedOffer && !await CanManageCampusAsync(enrollment.CampusId, tenant, ct))
                return Denied<StudentHostelAllocationDto>();
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == enrollment.StudentId && x.StatusCode == "Active", ct);
            if (student == null) return Error("Active student not found.", 404);
            if (approvedOffer && !await CanApplyForStudentAsync(student.Id, tenant, ct))
                return Denied<StudentHostelAllocationDto>();
            var selection = await (from bed in _beds.GetQueryable().AsNoTracking()
                join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
                where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                    bed.Id == request.HostelBedId && bed.IsActive && room.IsActive && hostel.IsActive
                select new { Bed = bed, Room = room, Hostel = hostel }).FirstOrDefaultAsync(ct);
            if (selection == null) return Error("Hostel bed is unavailable.", 404);
            if (selection.Hostel.CampusId != enrollment.CampusId)
                return Error("Hostel does not belong to the enrollment campus.", 409);
            if (!string.IsNullOrWhiteSpace(selection.Hostel.GenderRestriction) &&
                !string.Equals(selection.Hostel.GenderRestriction, student.Gender, StringComparison.OrdinalIgnoreCase))
                return Error("The student's gender does not meet the hostel restriction.", 409);
            if (selection.Room.Capacity <= 0) return Error("Hostel room capacity is unavailable.", 409);
            var end = request.EndDate ?? DateOnly.MaxValue;
            if (await _allocations.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                !x.IsDeleted && x.StudentId == student.Id && x.State != HostelAllocationState.Cancelled &&
                x.StartDate <= end && (!x.EndDate.HasValue || x.EndDate.Value >= start), ct))
                return Error("Student has an overlapping hostel reservation.", 409);
            if (await _allocations.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                !x.IsDeleted && x.HostelBedId == selection.Bed.Id && x.State != HostelAllocationState.Cancelled &&
                x.StartDate <= end && (!x.EndDate.HasValue || x.EndDate.Value >= start), ct))
                return Error("Selected bed is reserved during the requested period.", 409);
            var roomStays = await (from stay in _allocations.GetQueryable().AsNoTracking()
                join bed in _beds.GetQueryable().AsNoTracking() on stay.HostelBedId equals bed.Id
                where stay.TenantId == tenant && bed.TenantId == tenant &&
                    !stay.IsDeleted && !bed.IsDeleted && stay.State != HostelAllocationState.Cancelled &&
                    bed.HostelRoomId == selection.Room.Id && stay.StartDate <= end &&
                    (!stay.EndDate.HasValue || stay.EndDate.Value >= start)
                select new { stay.StartDate, stay.EndDate }).Take(5001).ToListAsync(ct);
            if (roomStays.Count > 5000) return Error("Too many overlapping reservations to validate safely.", 413);
            if (PeakConcurrentStays(start, request.EndDate,
                roomStays.Select(x => (x.StartDate, x.EndDate))) > selection.Room.Capacity)
                return Error("Hostel room would exceed capacity during the requested stay.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            var allocation = new StudentHostelAllocation
            {
                TenantId = tenant, ClientRequestId = request.ClientRequestId, StudentId = student.Id,
                StudentEnrollmentId = enrollment.Id, HostelBedId = selection.Bed.Id,
                StartDate = start, EndDate = request.EndDate,
                MonthlyRent = request.MonthlyRent ?? selection.Room.RentPerBed,
                SecurityDeposit = 0m, State = HostelAllocationState.Active,
                CreatedAt = now, CreatedBy = _currentUser.UserId
            };
            await _allocations.AddAsync(allocation);
            await _unitOfWork.SaveChangesAsync(ct);
            var dto = await MapAsync(allocation, ct);
            return new ApiResponse<StudentHostelAllocationDto>
            {
                Success = true, StatusCode = 201, Message = "Hostel allocated.", Data = dto
            };
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel allocation conflict for tenant {TenantId}", tenant);
            return Error("Hostel allocation conflicts with another request.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Serialized hostel transaction aborted for tenant {TenantId}", tenant);
            return Error("Hostel allocation conflicts with another request.", 409);
        }
        catch (Exception ex) when (HasHostelSqlSerializationConflict(ex))
        {
            _logger.LogWarning(ex, "SQL Server hostel allocation serialization conflict for tenant {TenantId}", tenant);
            return Error("Hostel allocation conflicts with another request.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hostel allocation failed for tenant {TenantId}", tenant);
            return Error("Hostel allocation could not be created.", 500);
        }
    }

    private async Task<StudentHostelAllocationDto> MapAsync(StudentHostelAllocation row, CancellationToken ct)
    {
        var tenant = _currentUser.TenantId;
        var student = await _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.Id == row.StudentId).Select(x => new { x.PublicId, x.FullName }).FirstOrDefaultAsync(ct);
        var enrollmentReference = await _enrollments.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.Id == row.StudentEnrollmentId).Select(x => x.PublicId).FirstOrDefaultAsync(ct);
        var bedDetails = await (from bed in _beds.GetQueryable().AsNoTracking()
            join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
            join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
            where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                bed.Id == row.HostelBedId
            select new { Bed = bed, RoomNumber = room.RoomNumber, hostel.PublicId, HostelName = hostel.Name, RoomId = room.Id })
            .FirstOrDefaultAsync(ct);
        return new StudentHostelAllocationDto
        {
            Id = row.Id, StudentReference = student?.PublicId ?? Guid.Empty,
            StudentEnrollmentReference = enrollmentReference, StudentName = student?.FullName ?? string.Empty,
            HostelReference = bedDetails?.PublicId ?? Guid.Empty, HostelName = bedDetails?.HostelName ?? string.Empty,
            HostelRoomId = bedDetails?.RoomId ?? 0, RoomNumber = bedDetails?.RoomNumber ?? string.Empty,
            HostelBedId = row.HostelBedId, BedNumber = bedDetails?.Bed.BedNumber ?? string.Empty,
            StartDate = row.StartDate, EndDate = row.EndDate, MonthlyRent = row.MonthlyRent,
            State = row.State, RowVersion = Convert.ToBase64String(row.RowVersion)
        };
    }

    private IQueryable<long> AllowedCampusIds(long tenant) =>
        from access in _campusAccesses.GetQueryable().AsNoTracking()
        join campus in _campuses.GetQueryable().AsNoTracking() on access.CampusId equals campus.Id
        where access.TenantId == tenant && campus.TenantId == tenant &&
            access.UserId == _currentUser.UserId && access.IsActive && campus.IsActive
        select campus.Id;

    private Task<bool> CanManageCampusAsync(long campusId, long tenant, CancellationToken ct) =>
        _currentUser.IsTenantAdmin ? Task.FromResult(true) : AllowedCampusIds(tenant).AnyAsync(x => x == campusId, ct);

    private static bool HasHostelSqlSerializationConflict(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current is Microsoft.Data.SqlClient.SqlException sql &&
                sql.Number is 1205 or 1222 or 3960 or 2601 or 2627)
                return true;
        return false;
    }

    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0;
    private bool CanManage() => CanRead() && (_currentUser.IsTenantAdmin ||
        _currentUser.IsInRole("Principal") || _currentUser.IsInRole("HostelWarden"));
    private static bool TryDecodeVersion(string? supplied, out byte[] value)
    {
        value = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { value = Convert.FromBase64String(supplied); return value.Length > 0; }
        catch (FormatException) { return false; }
    }
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Hostel permission is required.", 403);
    private static ApiResponse<StudentHostelAllocationDto> Error(string message, int code = 400) =>
        ApiResponse<StudentHostelAllocationDto>.ErrorResponse(message, code);
}
