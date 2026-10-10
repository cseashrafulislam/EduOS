using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Hostel;

public sealed class HostelService : IHostelService
{
    private readonly IGenericRepository<EduOS.Core.Entities.Hostel.Hostel> _hostels;
    private readonly IGenericRepository<HostelRoom> _rooms;
    private readonly IGenericRepository<HostelBed> _beds;
    private readonly IGenericRepository<StudentHostelAllocation> _allocations;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<UserCampusAccess> _campusAccesses;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<HostelService> _logger;

    public HostelService(IGenericRepository<EduOS.Core.Entities.Hostel.Hostel> hostels, IGenericRepository<HostelRoom> rooms,
        IGenericRepository<HostelBed> beds, IGenericRepository<StudentHostelAllocation> allocations,
        IGenericRepository<StudentEnrollment> enrollments, IGenericRepository<Student> students,
        IGenericRepository<UserCampusAccess> campusAccesses, IGenericRepository<Campus> campuses,
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, TimeProvider clock, ILogger<HostelService> logger)
    {
        _hostels = hostels; _rooms = rooms; _beds = beds; _allocations = allocations;
        _enrollments = enrollments; _students = students; _campusAccesses = campusAccesses;
        _campuses = campuses; _unitOfWork = unitOfWork;
        _currentUser = currentUser; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<HostelRoomDto>>> GetRoomsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<HostelRoomDto>>();
        var tenant = _currentUser.TenantId;
        var roomsQuery = from room in _rooms.GetQueryable().AsNoTracking()
            join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
            where room.TenantId == tenant && hostel.TenantId == tenant && room.IsActive && hostel.IsActive
            orderby hostel.Name, room.RoomNumber
            select new { Room = room, HostelName = hostel.Name, HostelReference = hostel.PublicId, hostel.CampusId };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            roomsQuery = roomsQuery.Where(x => allowed.Contains(x.CampusId));
        }
        var data = await roomsQuery.ToListAsync(cancellationToken);
        var roomIds = data.Select(x => x.Room.Id).ToArray();
        var occupied = await (from allocation in _allocations.GetQueryable().AsNoTracking()
            join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
            where allocation.TenantId == tenant && bed.TenantId == tenant &&
                allocation.State == HostelAllocationState.Active && roomIds.Contains(bed.HostelRoomId)
            group allocation by bed.HostelRoomId into g
            select new { RoomId = g.Key, Count = g.Count() })
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

    public async Task<ApiResponse<PagedResult<HostelStudentOptionDto>>> GetEligibleStudentsAsync(
        int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelStudentOptionDto>>();
        if (page < 1 || pageSize is < 1 or > 100) return ApiResponse<PagedResult<HostelStudentOptionDto>>.ErrorResponse("Invalid paging parameters.");
        var tenant = _currentUser.TenantId;
        var query = from enrollment in _enrollments.GetQueryable().AsNoTracking()
                    join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
                    where enrollment.TenantId == tenant && student.TenantId == tenant &&
                        enrollment.IsCurrent && enrollment.State == EnrollmentState.Active && student.StatusCode == "Active" &&
                        !_allocations.GetQueryable().Any(x => x.TenantId == tenant && x.StudentId == student.Id &&
                            x.State == HostelAllocationState.Active)
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
        int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelBedOptionDto>>();
        if (page < 1 || pageSize is < 1 or > 100) return ApiResponse<PagedResult<HostelBedOptionDto>>.ErrorResponse("Invalid paging parameters.");
        var tenant = _currentUser.TenantId;
        var query = from bed in _beds.GetQueryable().AsNoTracking()
                    join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                    join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
                    where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                        bed.IsActive && room.IsActive && hostel.IsActive && room.Capacity > 0 &&
                        !_allocations.GetQueryable().Any(x => x.TenantId == tenant && x.HostelBedId == bed.Id &&
                            x.State == HostelAllocationState.Active) &&
                        _allocations.GetQueryable().Count(x => x.TenantId == tenant &&
                            x.State == HostelAllocationState.Active &&
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
        var query = from allocation in _allocations.GetQueryable().AsNoTracking()
                    join student in _students.GetQueryable().AsNoTracking() on allocation.StudentId equals student.Id
                    join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
                    join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                    join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
                    where allocation.TenantId == tenant && student.TenantId == tenant && bed.TenantId == tenant &&
                        room.TenantId == tenant && hostel.TenantId == tenant && allocation.State == HostelAllocationState.Active
                    select new { allocation.Id, allocation.StudentId, allocation.StudentEnrollmentId, hostel.CampusId,
                        allocation.StartDate, allocation.RowVersion, student.FullName, student.StudentCode,
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
            BedNumber = x.BedNumber, StartDate = x.StartDate, RowVersion = Convert.ToBase64String(x.RowVersion)
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

    public async Task<ApiResponse<StudentHostelAllocationDto>> AllocateAsync(AllocateStudentHostelRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentHostelAllocationDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.StudentEnrollmentReference == Guid.Empty ||
            request.HostelBedId <= 0) return Error("Enrollment, bed and client request references are required.");
        if (request.MonthlyRent.HasValue && request.MonthlyRent < 0) return Error("Monthly rent cannot be negative.");
        var tenant = _currentUser.TenantId;
        try
        {
            using var scope = new TransactionScope(TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.Serializable },
                TransactionScopeAsyncFlowOption.Enabled);
            var existing = await _allocations.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (existing != null)
            {
                if (!_currentUser.IsTenantAdmin)
                {
                    var enrollmentCampus = await _enrollments.GetQueryable().AsNoTracking()
                        .Where(x => x.TenantId == tenant && x.Id == existing.StudentEnrollmentId &&
                            x.StudentId == existing.StudentId).Select(x => (long?)x.CampusId)
                        .FirstOrDefaultAsync(cancellationToken);
                    var bedCampus = await (from bed in _beds.GetQueryable().AsNoTracking()
                        join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                        join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
                        where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                            bed.Id == existing.HostelBedId
                        select (long?)hostel.CampusId).FirstOrDefaultAsync(cancellationToken);
                    if (!enrollmentCampus.HasValue || enrollmentCampus != bedCampus ||
                        !await CanManageCampusAsync(enrollmentCampus.Value, tenant, cancellationToken))
                        return Denied<StudentHostelAllocationDto>();
                }
                var enrollmentReference = await _enrollments.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                    x.Id == existing.StudentEnrollmentId).Select(x => x.PublicId).FirstOrDefaultAsync(cancellationToken);
                if (enrollmentReference != request.StudentEnrollmentReference || existing.HostelBedId != request.HostelBedId ||
                    (request.StartDate != default && existing.StartDate != request.StartDate) ||
                    (request.MonthlyRent.HasValue && existing.MonthlyRent != request.MonthlyRent.Value))
                    return Error("Client request ID was reused for different allocation data.", 409);
                var replay = await MapAsync(existing, cancellationToken);
                scope.Complete();
                return ApiResponse<StudentHostelAllocationDto>.SuccessResponse(replay, "Hostel allocation already processed.");
            }
            var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == request.StudentEnrollmentReference && x.IsCurrent &&
                x.State == EnrollmentState.Active, cancellationToken);
            if (enrollment == null) return Error("Active student enrollment not found.", 404);
            if (!await _campuses.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.Id == enrollment.CampusId && x.IsActive, cancellationToken))
                return Error("Enrollment campus is inactive.", 409);
            if (!await CanManageCampusAsync(enrollment.CampusId, tenant, cancellationToken))
                return Denied<StudentHostelAllocationDto>();
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == enrollment.StudentId && x.StatusCode == "Active", cancellationToken);
            if (student == null) return Error("Active student not found.", 404);
            var selection = await (from bed in _beds.GetQueryable().AsNoTracking()
                join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
                where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                    bed.Id == request.HostelBedId && bed.IsActive && room.IsActive && hostel.IsActive
                select new { Bed = bed, Room = room, Hostel = hostel }).FirstOrDefaultAsync(cancellationToken);
            if (selection == null) return Error("Hostel bed is unavailable.", 404);
            if (selection.Hostel.CampusId != enrollment.CampusId)
                return Error("Hostel does not belong to the enrollment campus.", 409);
            if (!string.IsNullOrWhiteSpace(selection.Hostel.GenderRestriction) &&
                !string.Equals(selection.Hostel.GenderRestriction, student.Gender, StringComparison.OrdinalIgnoreCase))
                return Error("The student's gender does not meet the hostel restriction.", 409);
            if (selection.Room.Capacity <= 0) return Error("Hostel room capacity is unavailable.", 409);
            if (await _allocations.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.StudentId == student.Id && x.State == HostelAllocationState.Active, cancellationToken))
                return Error("Student already has an active hostel allocation.", 409);
            if (await _allocations.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.HostelBedId == selection.Bed.Id && x.State == HostelAllocationState.Active, cancellationToken))
                return Error("Selected bed is occupied.", 409);
            var roomBedIds = await _beds.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.HostelRoomId == selection.Room.Id).Select(x => x.Id).ToArrayAsync(cancellationToken);
            var occupied = await _allocations.GetQueryable().AsNoTracking().CountAsync(x => x.TenantId == tenant &&
                roomBedIds.Contains(x.HostelBedId) && x.State == HostelAllocationState.Active, cancellationToken);
            if (occupied >= selection.Room.Capacity) return Error("Hostel room has reached capacity.", 409);
            var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
            var now = _clock.GetUtcNow().UtcDateTime;
            var allocation = new StudentHostelAllocation
            {
                TenantId = tenant, ClientRequestId = request.ClientRequestId, StudentId = student.Id,
                StudentEnrollmentId = enrollment.Id, HostelBedId = selection.Bed.Id,
                StartDate = request.StartDate == default ? today : request.StartDate,
                MonthlyRent = request.MonthlyRent ?? selection.Room.RentPerBed,
                SecurityDeposit = 0m, State = HostelAllocationState.Active,
                CreatedAt = now, CreatedBy = _currentUser.UserId
            };
            await _allocations.AddAsync(allocation);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var dto = await MapAsync(allocation, cancellationToken);
            scope.Complete();
            return new ApiResponse<StudentHostelAllocationDto>
            {
                Success = true, StatusCode = 201, Message = "Hostel allocated.", Data = dto
            };
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hostel allocation failed for tenant {TenantId}", tenant);
            return Error("Hostel allocation could not be created.", 500);
        }
    }

    public async Task<ApiResponse<StudentHostelAllocationDto>> CloseAsync(long id, CloseStudentHostelAllocationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentHostelAllocationDto>();
        if (id <= 0 || request == null) return Error("Invalid allocation close request.");
        var tenant = _currentUser.TenantId;
        try
        {
            var allocation = await _allocations.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == id, cancellationToken);
            if (allocation == null) return Error("Hostel allocation not found.", 404);
            if (!_currentUser.IsTenantAdmin)
            {
                var enrollmentCampus = await _enrollments.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.Id == allocation.StudentEnrollmentId &&
                        x.StudentId == allocation.StudentId).Select(x => (long?)x.CampusId)
                    .FirstOrDefaultAsync(cancellationToken);
                var bedCampus = await (from bed in _beds.GetQueryable().AsNoTracking()
                    join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                    join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
                    where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                        bed.Id == allocation.HostelBedId
                    select (long?)hostel.CampusId).FirstOrDefaultAsync(cancellationToken);
                if (!enrollmentCampus.HasValue || enrollmentCampus != bedCampus ||
                    !await CanManageCampusAsync(enrollmentCampus.Value, tenant, cancellationToken))
                    return Denied<StudentHostelAllocationDto>();
            }
            var end = request.EndDate == default ? DateOnly.FromDateTime(_clock.GetLocalNow().DateTime) : request.EndDate;
            if (allocation.State == HostelAllocationState.Closed)
            {
                if (allocation.EndDate.HasValue && (request.EndDate == default || allocation.EndDate.Value == end))
                    return ApiResponse<StudentHostelAllocationDto>.SuccessResponse(await MapAsync(allocation, cancellationToken), "Allocation already closed.");
                return Error("Allocation was already closed with a different end date.", 409);
            }
            if (allocation.State != HostelAllocationState.Active)
                return Error("Allocation cannot be closed in its current state.", 409);
            if (!TryDecodeVersion(request.RowVersion, out var bytes) ||
                !allocation.RowVersion.AsSpan().SequenceEqual(bytes))
                return Error("Hostel allocation changed. Reload and retry.", 409);
            if (end < allocation.StartDate) return Error("End date cannot precede start date.");
            allocation.EndDate = end;
            allocation.State = HostelAllocationState.Closed;
            allocation.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            allocation.UpdatedBy = _currentUser.UserId;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<StudentHostelAllocationDto>.SuccessResponse(await MapAsync(allocation, cancellationToken), "Allocation closed.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error("Allocation was changed by another user.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hostel close failed for tenant {TenantId}", tenant);
            return Error("Hostel allocation could not be closed.", 500);
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
