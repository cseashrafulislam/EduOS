using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using System.Transactions;

namespace EduOS.Service.Services.Hostel;

public sealed class HostelService : IHostelService
{
    private readonly IGenericRepository<HostelRoom> _rooms;
    private readonly IGenericRepository<StudentHostel> _allocations;
    private readonly IGenericRepository<Student> _students;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;

    public HostelService(IGenericRepository<HostelRoom> rooms, IGenericRepository<StudentHostel> allocations, IGenericRepository<Student> students, IUnitOfWork unitOfWork, ICurrentUserService currentUser, TimeProvider clock)
    { _rooms = rooms; _allocations = allocations; _students = students; _unitOfWork = unitOfWork; _currentUser = currentUser; _clock = clock; }

    public async Task<ApiResponse<IReadOnlyList<HostelRoomDto>>> GetRoomsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<HostelRoomDto>>();
        var tenantId = _currentUser.TenantId;
        IReadOnlyList<HostelRoomDto> rows = await _rooms.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && x.Hostel != null && x.Hostel.IsActive).OrderBy(x => x.Hostel!.Name).ThenBy(x => x.RoomNo).Select(x => new HostelRoomDto
        {
            Id = x.Id, HostelId = x.HostelId, HostelName = x.Hostel!.Name, RoomNo = x.RoomNo, Capacity = x.Capacity,
            OccupiedBeds = _allocations.GetQueryable().Count(a => a.TenantId == tenantId && a.HostelRoomId == x.Id && a.IsActive),
            AvailableBeds = Math.Max(0, x.Capacity - _allocations.GetQueryable().Count(a => a.TenantId == tenantId && a.HostelRoomId == x.Id && a.IsActive)), RentPerBed = x.RentPerBed
        }).ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<HostelRoomDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<StudentHostelDto?>> GetMyAllocationAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<StudentHostelDto?>();
        var tenantId = _currentUser.TenantId;
        var studentIds = await _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && x.UserId == _currentUser.UserId).Select(x => x.Id).ToListAsync(cancellationToken);
        var row = await Query().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && studentIds.Contains(x.StudentId)).OrderByDescending(x => x.StartDate).FirstOrDefaultAsync(cancellationToken);
        return ApiResponse<StudentHostelDto?>.SuccessResponse(row == null ? null : Map(row));
    }

    public async Task<ApiResponse<StudentHostelDto>> AllocateAsync(AllocateHostelDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentHostelDto>();
        if (request == null || request.StudentReference == Guid.Empty || request.HostelRoomId <= 0) return Error("Student and room are required.");
        var tenantId = _currentUser.TenantId;
        try
        {
            using var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == request.StudentReference && x.IsActive, cancellationToken);
            if (student == null) return Error("Student not found.", 404);
            var room = await _rooms.GetQueryable().Include(x => x.Hostel).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.HostelRoomId && x.IsActive && x.Hostel != null && x.Hostel.IsActive, cancellationToken);
            if (room == null) return Error("Hostel room not found.", 404);
            if (room.Capacity <= 0) return Error("Room capacity is not configured.", 409);
            if (await _allocations.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.StudentId == student.Id && x.IsActive, cancellationToken)) return Error("Student already has an active hostel allocation.", 409);
            var occupied = await _allocations.GetQueryable().CountAsync(x => x.TenantId == tenantId && x.HostelRoomId == room.Id && x.IsActive, cancellationToken);
            if (occupied >= room.Capacity) return Error("Hostel room has reached capacity.", 409);
            if (!string.IsNullOrWhiteSpace(request.BedNo) && await _allocations.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.HostelRoomId == room.Id && x.IsActive && x.BedNo == request.BedNo.Trim(), cancellationToken)) return Error("Bed is already allocated.", 409);
            var row = new StudentHostel { TenantId = tenantId, StudentId = student.Id, HostelId = room.HostelId, HostelRoomId = room.Id, BedNo = string.IsNullOrWhiteSpace(request.BedNo) ? null : request.BedNo.Trim(), StartDate = request.StartDate == default ? _clock.GetLocalNow().Date : request.StartDate.Date, MonthlyRent = request.MonthlyRent ?? room.RentPerBed, IsActive = true, CreatedAt = _clock.GetUtcNow().UtcDateTime, CreatedBy = _currentUser.UserId };
            await _allocations.AddAsync(row); await _unitOfWork.SaveChangesAsync(cancellationToken); scope.Complete();
            row.Student = student; row.Hostel = room.Hostel; row.HostelRoom = room;
            return new ApiResponse<StudentHostelDto> { Success = true, StatusCode = 201, Message = "Hostel allocated.", Data = Map(row) };
        }
        catch (DbUpdateException) { return Error("Hostel allocation conflicts with another update. Reload and try again.", 409); }
        catch (TransactionAbortedException) { return Error("Hostel allocation conflicts with another update. Reload and try again.", 409); }
    }

    public async Task<ApiResponse<StudentHostelDto>> CloseAsync(long id, CloseHostelAllocationDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentHostelDto>();
        var tenantId = _currentUser.TenantId;
        var row = await Query().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (row == null) return Error("Hostel allocation not found.", 404);
        if (!row.IsActive) return ApiResponse<StudentHostelDto>.SuccessResponse(Map(row), "Hostel allocation is already closed.");
        var end = request?.EndDate?.Date ?? _clock.GetLocalNow().Date;
        if (end < row.StartDate.Date) return Error("End date cannot be before start date.");
        row.EndDate = end; row.IsActive = false; row.UpdatedAt = _clock.GetUtcNow().UtcDateTime; row.UpdatedBy = _currentUser.UserId;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ApiResponse<StudentHostelDto>.SuccessResponse(Map(row), "Hostel allocation closed.");
    }

    private IQueryable<StudentHostel> Query() => _allocations.GetQueryable().Include(x => x.Student).Include(x => x.Hostel).Include(x => x.HostelRoom);
    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0;
    private bool CanManage() => CanRead() && (_currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal") || _currentUser.IsInRole("HostelWarden"));
    private static StudentHostelDto Map(StudentHostel x) => new() { Id = x.Id, StudentReference = x.Student?.PublicId ?? Guid.Empty, StudentName = x.Student?.FullName ?? string.Empty, HostelId = x.HostelId, HostelName = x.Hostel?.Name ?? string.Empty, HostelRoomId = x.HostelRoomId, RoomNo = x.HostelRoom?.RoomNo ?? string.Empty, BedNo = x.BedNo, StartDate = x.StartDate, EndDate = x.EndDate, MonthlyRent = x.MonthlyRent, IsActive = x.IsActive };
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Hostel access is required.", 403);
    private static ApiResponse<StudentHostelDto> Error(string message, int status = 400) => ApiResponse<StudentHostelDto>.ErrorResponse(message, status);
}
