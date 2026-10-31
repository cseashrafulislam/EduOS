using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Service.Services.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Transactions;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    // Same-day transfers avoid a future-dated predecessor being marked Closed while
    // it still physically holds a bed. Future transfers require a scheduled state policy.
    public static bool CanTransferOccupiedToday(DateOnly start, DateOnly? end,
        DateOnly effectiveFrom, DateOnly today) =>
        start < today && effectiveFrom == today && (!end.HasValue || end.Value >= today);

    public async Task<ApiResponse<PagedResult<HostelBedInventoryDto>>> GetOccupiedTransferBedOptionsAsync(
        long id, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelBedInventoryDto>>();
        if (id <= 0 || page < 1 || pageSize is < 1 or > 50 || (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<HostelBedInventoryDto>>.ErrorResponse("Invalid transfer selection.");
        var tenant = _currentUser.TenantId;
        var source = await _allocations.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Id == id, ct);
        if (source == null) return ApiResponse<PagedResult<HostelBedInventoryDto>>.ErrorResponse("Stay not found.", 404);
        var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Id == source.StudentEnrollmentId &&
            x.StudentId == source.StudentId && x.IsCurrent && x.State == EnrollmentState.Active, ct);
        var sourceCampus = await (from bed in _beds.GetQueryable().AsNoTracking()
            join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
            join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
            where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                bed.Id == source.HostelBedId && !bed.IsDeleted && !room.IsDeleted && !hostel.IsDeleted
            select (long?)hostel.CampusId).FirstOrDefaultAsync(ct);
        if (enrollment == null || !sourceCampus.HasValue || sourceCampus.Value != enrollment.CampusId ||
            !await _campuses.GetQueryable().AnyAsync(x => x.TenantId == tenant &&
                x.Id == enrollment.CampusId && x.IsActive && !x.IsDeleted, ct))
            return ApiResponse<PagedResult<HostelBedInventoryDto>>.ErrorResponse("Active enrollment campus unavailable.", 409);
        if (!await CanManageCampusAsync(enrollment.CampusId, tenant, ct))
            return Denied<PagedResult<HostelBedInventoryDto>>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (source.State != HostelAllocationState.Active ||
            !CanTransferOccupiedToday(source.StartDate, source.EndDate, today, today))
            return ApiResponse<PagedResult<HostelBedInventoryDto>>.ErrorResponse(
                "Only currently occupied stays that began before today can transfer today.", 409);
        var end = source.EndDate ?? DateOnly.MaxValue;
        var options = from bed in _beds.GetQueryable().AsNoTracking()
            join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
            join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
            where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                !bed.IsDeleted && !room.IsDeleted && !hostel.IsDeleted &&
                bed.IsActive && room.IsActive && hostel.IsActive && room.Capacity > 0 &&
                hostel.CampusId == enrollment.CampusId && bed.Id != source.HostelBedId &&
                !_allocations.GetQueryable().Any(x => x.TenantId == tenant && !x.IsDeleted &&
                    x.State != HostelAllocationState.Cancelled && x.HostelBedId == bed.Id &&
                    x.StartDate <= end && (!x.EndDate.HasValue || x.EndDate.Value >= today))
            select new { Bed = bed, Room = room, Hostel = hostel };
        var count = await options.CountAsync(ct);
        var rows = await options.OrderBy(x => x.Hostel.Name).ThenBy(x => x.Room.RoomNumber)
            .ThenBy(x => x.Bed.BedNumber).ThenBy(x => x.Bed.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<HostelBedInventoryDto>>.SuccessResponse(new PagedResult<HostelBedInventoryDto>
        {
            Page = page, PageSize = pageSize, TotalCount = count,
            Items = rows.Select(x => new HostelBedInventoryDto
            {
                Id = x.Bed.Id, HostelRoomId = x.Room.Id, HostelName = x.Hostel.Name,
                RoomNumber = x.Room.RoomNumber, BedNumber = x.Bed.BedNumber,
                IsActive = true, RowVersion = Convert.ToBase64String(x.Bed.RowVersion)
            }).ToList()
        });
    }

    /// <summary>Same-day split; charged/billed stays additionally require exact single-use Finance clearance.</summary>
    public async Task<ApiResponse<TransferOccupiedHostelResultDto>> TransferOccupiedNoChargeAsync(
        long id, TransferOccupiedHostelRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<TransferOccupiedHostelResultDto>();
        if (id <= 0 || request == null || request.ClientRequestId == Guid.Empty ||
            request.DestinationBedId <= 0 || request.EffectiveFrom == default ||
            !TryDecodeVersion(request.SourceRowVersion, out var sourceVersion) ||
            !TryDecodeVersion(request.DestinationBedRowVersion, out var bedVersion) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 5 or > 500)
            return ApiResponse<TransferOccupiedHostelResultDto>.ErrorResponse(
                "Source, destination, dates, both row versions, request ID and reason (5-500 characters) are required.");
        var tenant = _currentUser.TenantId;
        const string operation = "hostel.occupied-transfer";
        var reason = request.Reason.Trim();
        var requestHash = HostelCommandFingerprint(operation, id, request.DestinationBedId,
            request.EffectiveFrom.ToString("yyyy-MM-dd"), Convert.ToBase64String(sourceVersion),
            Convert.ToBase64String(bedVersion), reason, request.FinanceClearanceId?.ToString() ?? "none");
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var source = await _allocations.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == id, ct);
                if (source == null) return TransferError("Stay not found.", 404);
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == source.StudentEnrollmentId &&
                    x.StudentId == source.StudentId && x.IsCurrent && x.State == EnrollmentState.Active, ct);
                var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == source.StudentId && x.StatusCode == "Active", ct);
                if (enrollment == null || student == null ||
                    !await _campuses.GetQueryable().AnyAsync(x => x.TenantId == tenant && !x.IsDeleted &&
                        x.Id == enrollment.CampusId && x.IsActive, ct))
                    return TransferError("Active student and enrollment campus required.", 409);
                if (!await CanManageCampusAsync(enrollment.CampusId, tenant, ct))
                    return Denied<TransferOccupiedHostelResultDto>();

                var beds = await (from bed in _beds.GetQueryable()
                    join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                    join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
                    where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                        (bed.Id == source.HostelBedId || bed.Id == request.DestinationBedId) &&
                        !bed.IsDeleted && !room.IsDeleted && !hostel.IsDeleted
                    orderby bed.Id
                    select new { Bed = bed, Room = room, Hostel = hostel }).ToListAsync(ct);
                var old = beds.FirstOrDefault(x => x.Bed.Id == source.HostelBedId);
                var target = beds.FirstOrDefault(x => x.Bed.Id == request.DestinationBedId);
                if (old == null || target == null || target.Bed.Id == old.Bed.Id ||
                    old.Hostel.CampusId != enrollment.CampusId || target.Hostel.CampusId != enrollment.CampusId)
                    return TransferError("Choose a different bed inside the current enrollment campus.", 409);
                if (!old.Bed.IsActive || !old.Room.IsActive || !old.Hostel.IsActive ||
                    !target.Bed.IsActive || !target.Room.IsActive || !target.Hostel.IsActive ||
                    target.Room.Capacity < 1 ||
                    !string.IsNullOrWhiteSpace(target.Hostel.GenderRestriction) &&
                    !string.Equals(target.Hostel.GenderRestriction, student.Gender, StringComparison.OrdinalIgnoreCase))
                    return TransferError("Hostel or destination eligibility is unavailable.", 409);

                // Validate authorisation before replay; an exactly matching retry returns
                // the original response, not another successor or modified row versions.
                var replay = await _commandReceipts.ReplayAsync<TransferOccupiedHostelResultDto>(tenant,
                    _currentUser.UserId, operation, request.ClientRequestId, id, requestHash, ct);
                if (replay.Conflict) return TransferError("Request key was used for different transfer details.", 409);
                if (replay.Found) return replay.Response!;

                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                if (source.State != HostelAllocationState.Active ||
                    !CanTransferOccupiedToday(source.StartDate, source.EndDate, request.EffectiveFrom, today))
                    return TransferError("Transfer is supported only on the current day after a prior occupied day.", 409);
                if (!source.RowVersion.AsSpan().SequenceEqual(sourceVersion) ||
                    !target.Bed.RowVersion.AsSpan().SequenceEqual(bedVersion))
                    return TransferError("Stay or destination bed changed. Reload and retry.", 409);
                if (await _allocations.GetQueryable().IgnoreQueryFilters().AnyAsync(x =>
                    x.TenantId == tenant && x.PreviousAllocationId == id, ct))
                    return TransferError("This stay already has a transfer successor.", 409);
                if (!request.FinanceClearanceId.HasValue)
                {
                    if (ReservationFinancePolicy.HasChargeableTerms(source.MonthlyRent, source.SecurityDeposit,
                        source.FeeHeadId.HasValue))
                        return TransferError("Charged transfers require approved Finance clearance.", 409);
                    if (await _reservationFinancePolicy.HasPostedActivityAsync(tenant,
                        source.StudentId, source.StudentEnrollmentId, ct))
                        return TransferError("Issued/posted Finance activity must be reconciled before transfer.", 409);
                }

                var end = source.EndDate ?? DateOnly.MaxValue;
                if (await _allocations.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.State != HostelAllocationState.Cancelled &&
                    x.Id != source.Id && x.StudentId == source.StudentId &&
                    x.StartDate <= end && (!x.EndDate.HasValue || x.EndDate.Value >= today), ct))
                    return TransferError("Student has another overlapping stay.", 409);
                if (await _allocations.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.State != HostelAllocationState.Cancelled &&
                    x.HostelBedId == target.Bed.Id && x.StartDate <= end &&
                    (!x.EndDate.HasValue || x.EndDate.Value >= today), ct))
                    return TransferError("Destination bed is reserved during the remaining stay.", 409);
                var concurrent = await (from stay in _allocations.GetQueryable().AsNoTracking()
                    join bed in _beds.GetQueryable().AsNoTracking() on stay.HostelBedId equals bed.Id
                    where stay.TenantId == tenant && bed.TenantId == tenant && !stay.IsDeleted &&
                        stay.State != HostelAllocationState.Cancelled && stay.Id != source.Id &&
                        bed.HostelRoomId == target.Room.Id && stay.StartDate <= end &&
                        (!stay.EndDate.HasValue || stay.EndDate.Value >= today)
                    select new { stay.StartDate, stay.EndDate }).Take(5001).ToListAsync(ct);
                if (concurrent.Count > 5000) return TransferError("Too many room reservations to validate safely.", 409);
                if (PeakConcurrentStays(today, source.EndDate,
                    concurrent.Select(x => (x.StartDate, x.EndDate))) > target.Room.Capacity)
                    return TransferError("Destination room would exceed its capacity.", 409);

                HostelFinanceClearance? approvedClearance = null;
                if (request.FinanceClearanceId.HasValue)
                {
                    var approved = await ConsumeHostelFinanceClearanceAsync(request.FinanceClearanceId.Value,
                        source, enrollment.CampusId, HostelClearanceOperation.Transfer,
                        request.EffectiveFrom, request.DestinationBedId, target.Bed.RowVersion, ct);
                    if (approved.Error != null) return TransferError(approved.Error, 409);
                    approvedClearance = approved.Clearance;
                }

                var previousEnd = source.EndDate;
                var now = _clock.GetUtcNow().UtcDateTime;
                source.EndDate = today.AddDays(-1);
                source.State = HostelAllocationState.Closed;
                source.UpdatedAt = now;
                source.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(ct);
                var successor = new StudentHostelAllocation
                {
                    TenantId = tenant, ClientRequestId = request.ClientRequestId,
                    StudentId = source.StudentId, StudentEnrollmentId = source.StudentEnrollmentId,
                    HostelBedId = target.Bed.Id, PreviousAllocationId = source.Id,
                    StartDate = today, EndDate = previousEnd, State = HostelAllocationState.Active,
                    MonthlyRent = source.MonthlyRent, SecurityDeposit = source.SecurityDeposit,
                    FeeHeadId = source.FeeHeadId, CreatedAt = now, CreatedBy = _currentUser.UserId
                };
                await _allocations.AddAsync(successor);
                await _unitOfWork.SaveChangesAsync(ct);
                if (approvedClearance != null) approvedClearance.SuccessorAllocationId = successor.Id;
                await _auditLogs.AddAsync(new AuditLog
                {
                    TenantId = tenant, UserId = _currentUser.UserId,
                    Action = "HostelOccupiedTransfer", EntityName = nameof(StudentHostelAllocation),
                    EntityId = successor.Id, OccurredAt = now, CreatedAt = now,
                    CreatedBy = _currentUser.UserId,
                    NewValue = JsonSerializer.Serialize(new
                    {
                        SourceAllocationId = source.Id, SuccessorAllocationId = successor.Id,
                        SourceBedId = old.Bed.Id, DestinationBedId = target.Bed.Id,
                        EffectiveFrom = today, Reason = reason, ClientRequestId = request.ClientRequestId,
                        FinanceClearanceId = request.FinanceClearanceId
                    })
                });
                var success = ApiResponse<TransferOccupiedHostelResultDto>.SuccessResponse(
                    new TransferOccupiedHostelResultDto
                    {
                        SourceAllocationId = source.Id, SuccessorAllocationId = successor.Id,
                        SourceRowVersion = Convert.ToBase64String(source.RowVersion),
                        SuccessorRowVersion = Convert.ToBase64String(successor.RowVersion)
                    }, "Occupied stay transferred with its history and Finance clearance preserved.");
                await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                    request.ClientRequestId, id, requestHash, success, ct);
                await _unitOfWork.SaveChangesAsync(ct);
                _logger.LogInformation("Hostel occupied transfer tenant {TenantId}, source {SourceId}, successor {SuccessorId}",
                    tenant, source.Id, successor.Id);
                return success;
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return TransferError("Stay or bed changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel occupied transfer integrity conflict tenant {TenantId}", tenant);
            return TransferError("Concurrent transfer or reservation conflict.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Hostel occupied transfer aborted tenant {TenantId}", tenant);
            return TransferError("Concurrent transfer conflict. Reload and retry.", 409);
        }
        catch (Exception ex) when (HasHostelSqlSerializationConflict(ex))
        {
            _logger.LogWarning(ex, "Hostel transfer SQL serialization conflict tenant {TenantId}", tenant);
            return TransferError("Concurrent transfer conflict. Reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hostel occupied transfer failed tenant {TenantId}", tenant);
            return TransferError("Occupied transfer could not be completed.", 500);
        }
    }

    private static ApiResponse<TransferOccupiedHostelResultDto> TransferError(string message, int code = 400) =>
        ApiResponse<TransferOccupiedHostelResultDto>.ErrorResponse(message, code);
}
