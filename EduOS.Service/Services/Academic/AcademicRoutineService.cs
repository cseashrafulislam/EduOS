using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.HR;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Academic;

public sealed class AcademicRoutineService : IAcademicRoutineService
{
    private readonly IGenericRepository<RoutineTimeSlot> _timeSlots;
    private readonly IGenericRepository<InstructorAssignment> _assignments;
    private readonly IGenericRepository<RoutineEntry> _entries;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicTerm> _terms;
    private readonly IGenericRepository<SubjectOffering> _offerings;
    private readonly IGenericRepository<CurriculumSubject> _curriculumSubjects;
    private readonly IGenericRepository<Subject> _subjects;
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<Room> _rooms;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<AcademicRoutineService> _logger;

    public AcademicRoutineService(
        IGenericRepository<RoutineTimeSlot> timeSlots,
        IGenericRepository<InstructorAssignment> assignments,
        IGenericRepository<RoutineEntry> entries,
        IGenericRepository<AcademicBatch> batches,
        IGenericRepository<AcademicYear> years,
        IGenericRepository<AcademicTerm> terms,
        IGenericRepository<SubjectOffering> offerings,
        IGenericRepository<CurriculumSubject> curriculumSubjects,
        IGenericRepository<Subject> subjects,
        IGenericRepository<Employee> employees,
        IGenericRepository<Room> rooms,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        TimeProvider clock,
        ILogger<AcademicRoutineService> logger)
    {
        _timeSlots = timeSlots;
        _assignments = assignments;
        _entries = entries;
        _batches = batches;
        _years = years;
        _terms = terms;
        _offerings = offerings;
        _curriculumSubjects = curriculumSubjects;
        _subjects = subjects;
        _employees = employees;
        _rooms = rooms;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<AcademicInstructorChoiceDto>>> GetInstructorChoicesAsync(string? search, int take = 50, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<AcademicInstructorChoiceDto>>();
        if (search?.Length > 100) return Error<IReadOnlyList<AcademicInstructorChoiceDto>>("Search is too long.");
        var term = search?.Trim();
        var query = _employees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _currentUser.TenantId && x.CanTeach && x.State == EmployeeState.Active);
        if (!string.IsNullOrEmpty(term))
            query = query.Where(x => x.FullName.StartsWith(term) || x.EmployeeCode.StartsWith(term));
        IReadOnlyList<AcademicInstructorChoiceDto> rows = await query.OrderBy(x => x.FullName).ThenBy(x => x.Id)
            .Select(x => new AcademicInstructorChoiceDto { Id = x.Id, Name = x.FullName, EmployeeCode = x.EmployeeCode })
            .Take(Math.Clamp(take, 1, 100)).ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<AcademicInstructorChoiceDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<RoutineTimeSlotDto>>> GetTimeSlotsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<RoutineTimeSlotDto>>();
        IReadOnlyList<RoutineTimeSlotDto> rows = await _timeSlots.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.IsActive)
            .OrderBy(x => x.StartTime).ThenBy(x => x.DisplayOrder).ThenBy(x => x.Id)
            .Select(x => MapTimeSlot(x)).Take(200).ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<RoutineTimeSlotDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<RoutineTimeSlotDto>> CreateTimeSlotAsync(SaveRoutineTimeSlotRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<RoutineTimeSlotDto>();
        if (request == null || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100 ||
            request.StartTime >= request.EndTime || request.EndTime == default || request.DisplayOrder < 0)
            return Error<RoutineTimeSlotDto>("Invalid time-slot name or time range.");
        var tenant = _currentUser.TenantId;
        var name = request.Name.Trim();
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var existing = await _timeSlots.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && (x.Name == name ||
                    (x.IsActive && x.StartTime == request.StartTime && x.EndTime == request.EndTime)), token);
                if (existing != null)
                {
                    if (existing.Name != name || existing.StartTime != request.StartTime ||
                        existing.EndTime != request.EndTime || existing.IsBreak != request.IsBreak ||
                        existing.IsActive != request.IsActive || existing.DisplayOrder != request.DisplayOrder)
                        return Error<RoutineTimeSlotDto>("Time slot conflicts with an existing slot.", 409);
                    return ApiResponse<RoutineTimeSlotDto>.SuccessResponse(MapTimeSlot(existing), "Time slot already exists.");
                }
                var row = new RoutineTimeSlot
                {
                    TenantId = tenant, Name = name, StartTime = request.StartTime, EndTime = request.EndTime,
                    IsBreak = request.IsBreak, IsActive = request.IsActive, DisplayOrder = request.DisplayOrder,
                    CreatedAt = _clock.GetUtcNow().UtcDateTime, CreatedBy = _currentUser.UserId
                };
                await _timeSlots.AddAsync(row);
                await _unitOfWork.SaveChangesAsync(token);
                return Created(MapTimeSlot(row), "Time slot created.");
            }, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Time-slot write conflict for tenant {TenantId}", tenant);
            return Error<RoutineTimeSlotDto>("Time slot conflicts with another update.", 409);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<InstructorAssignmentDto>>> GetAssignmentsAsync(
        long academicBatchId, long? academicTermId, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<InstructorAssignmentDto>>();
        var batch = await GetBatchAsync(academicBatchId, cancellationToken);
        if (batch == null) return Error<IReadOnlyList<InstructorAssignmentDto>>("Academic batch not found.", 404);
        var term = await ResolveTermAsync(batch, academicTermId, cancellationToken);
        if (!term.IsValid) return Error<IReadOnlyList<InstructorAssignmentDto>>(term.Error!);

        var offeringIds = await GetOfferingIdsAsync(batch.Id, batch.AcademicYearId, term.TermId, cancellationToken);
        if (offeringIds.Count == 0)
            return ApiResponse<IReadOnlyList<InstructorAssignmentDto>>.SuccessResponse(Array.Empty<InstructorAssignmentDto>());

        var query = _assignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && offeringIds.Contains(x.SubjectOfferingId) && x.IsActive);
        if (!IsManager())
        {
            var employeeId = await GetLinkedTeacherIdAsync(cancellationToken);
            if (!employeeId.HasValue) return Denied<IReadOnlyList<InstructorAssignmentDto>>();
            query = query.Where(x => x.EmployeeId == employeeId.Value);
        }

        var assignments = await query.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.EmployeeId).Take(500).ToListAsync(cancellationToken);
        IReadOnlyList<InstructorAssignmentDto> rows = await MapAssignmentsAsync(assignments, cancellationToken);
        return ApiResponse<IReadOnlyList<InstructorAssignmentDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<InstructorAssignmentDto>> AssignInstructorAsync(
        SaveInstructorAssignmentRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<InstructorAssignmentDto>();
        if (request == null || request.SubjectOfferingReference == Guid.Empty || request.EmployeeReference == Guid.Empty ||
            request.EffectiveFrom == default || (request.EffectiveTo.HasValue && request.EffectiveTo < request.EffectiveFrom))
            return Error<InstructorAssignmentDto>("Invalid instructor, subject offering or effective dates.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var offering = await _offerings.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.PublicId == request.SubjectOfferingReference && x.IsActive, token);
                if (offering == null) return Error<InstructorAssignmentDto>("Subject offering not found.", 404);
                var batch = await GetBatchAsync(offering.AcademicBatchId, token);
                if (batch == null || batch.AcademicYearId != offering.AcademicYearId)
                    return Error<InstructorAssignmentDto>("Subject offering batch is unavailable.", 409);
                var employee = await _employees.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.PublicId == request.EmployeeReference &&
                    x.CanTeach && x.State == EmployeeState.Active, token);
                if (employee == null) return Error<InstructorAssignmentDto>("Active instructor not found.", 404);
                var range = await ResolveEffectiveRangeAsync(batch, offering.AcademicTermId, token);
                if (!range.IsValid || request.EffectiveFrom < range.From ||
                    request.EffectiveTo.GetValueOrDefault(range.To ?? DateOnly.MaxValue) >
                    range.To.GetValueOrDefault(DateOnly.MaxValue))
                    return Error<InstructorAssignmentDto>("Assignment dates must belong to the academic year or term.", 409);
                var existing = await _assignments.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.SubjectOfferingId == offering.Id &&
                    x.EmployeeId == employee.Id && x.IsActive, token);
                if (existing != null)
                {
                    if (existing.IsPrimary != request.IsPrimary || existing.EffectiveFrom != request.EffectiveFrom ||
                        existing.EffectiveTo != request.EffectiveTo || existing.IsActive != request.IsActive)
                        return Error<InstructorAssignmentDto>("Instructor assignment already exists with different details.", 409);
                    return ApiResponse<InstructorAssignmentDto>.SuccessResponse(
                        (await MapAssignmentsAsync([existing], token))[0], "Instructor already assigned.");
                }
                if (request.IsPrimary && request.IsActive && await _assignments.GetQueryable().AsNoTracking()
                    .AnyAsync(x => x.TenantId == tenant && x.SubjectOfferingId == offering.Id &&
                        x.IsPrimary && x.IsActive, token))
                    return Error<InstructorAssignmentDto>("Subject offering already has a primary instructor.", 409);
                var row = new InstructorAssignment
                {
                    TenantId = tenant, SubjectOfferingId = offering.Id, EmployeeId = employee.Id,
                    IsPrimary = request.IsPrimary, EffectiveFrom = request.EffectiveFrom,
                    EffectiveTo = request.EffectiveTo, IsActive = request.IsActive,
                    CreatedAt = _clock.GetUtcNow().UtcDateTime, CreatedBy = _currentUser.UserId
                };
                await _assignments.AddAsync(row);
                await _unitOfWork.SaveChangesAsync(token);
                return Created((await MapAssignmentsAsync([row], token))[0], "Instructor assigned.");
            }, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Instructor assignment conflict for tenant {TenantId}", tenant);
            return Error<InstructorAssignmentDto>("Instructor assignment conflicts with another update.", 409);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<RoutineEntryDto>>> GetBatchTimetableAsync(
        long academicBatchId, long? academicTermId, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<RoutineEntryDto>>();
        var batch = await GetBatchAsync(academicBatchId, cancellationToken);
        if (batch == null) return Error<IReadOnlyList<RoutineEntryDto>>("Academic batch not found.", 404);
        var term = await ResolveTermAsync(batch, academicTermId, cancellationToken);
        if (!term.IsValid) return Error<IReadOnlyList<RoutineEntryDto>>(term.Error!);

        var offeringIds = await GetOfferingIdsAsync(batch.Id, batch.AcademicYearId, term.TermId, cancellationToken);
        if (!IsManager())
        {
            var employeeId = await GetLinkedTeacherIdAsync(cancellationToken);
            if (!employeeId.HasValue) return Denied<IReadOnlyList<RoutineEntryDto>>();
            var accessible = await _assignments.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == _currentUser.TenantId && offeringIds.Contains(x.SubjectOfferingId) &&
                               x.EmployeeId == employeeId.Value && x.IsActive, cancellationToken);
            if (!accessible) return Denied<IReadOnlyList<RoutineEntryDto>>();
        }

        var entries = await _entries.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && offeringIds.Contains(x.SubjectOfferingId) && x.IsActive)
            .OrderBy(x => x.DayOfWeek).ThenBy(x => x.RoutineTimeSlotId).Take(1000).ToListAsync(cancellationToken);
        IReadOnlyList<RoutineEntryDto> rows = await MapEntriesAsync(entries, cancellationToken);
        return ApiResponse<IReadOnlyList<RoutineEntryDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<RoutineEntryDto>>> GetTeacherTimetableAsync(
        long employeeId, long academicYearId, long? academicTermId, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<RoutineEntryDto>>();
        var tenantId = _currentUser.TenantId;
        var employee = await _employees.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == employeeId &&
                                      x.State == EmployeeState.Active && x.CanTeach, cancellationToken);
        if (employee == null) return Error<IReadOnlyList<RoutineEntryDto>>("Instructor not found.", 404);
        if (!IsManager() && employee.UserId != _currentUser.UserId) return Denied<IReadOnlyList<RoutineEntryDto>>();

        if (!await _years.GetQueryable().AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.Id == academicYearId && x.IsActive, cancellationToken))
            return Error<IReadOnlyList<RoutineEntryDto>>("Academic year not found.", 404);
        if (academicTermId.HasValue && !await _terms.GetQueryable().AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.Id == academicTermId.Value &&
                           x.AcademicYearId == academicYearId && x.IsActive, cancellationToken))
            return Error<IReadOnlyList<RoutineEntryDto>>("Academic term does not belong to the selected academic year.");

        var offeringIds = await _offerings.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.AcademicYearId == academicYearId &&
                        x.AcademicTermId == academicTermId && x.IsActive)
            .Select(x => x.Id).Take(1000).ToListAsync(cancellationToken);
        var assignmentIds = await _assignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && offeringIds.Contains(x.SubjectOfferingId) &&
                        x.EmployeeId == employee.Id && x.IsActive)
            .Select(x => x.Id).Take(1000).ToListAsync(cancellationToken);
        var entries = assignmentIds.Count == 0
            ? new List<RoutineEntry>()
            : await _entries.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.InstructorAssignmentId.HasValue &&
                            assignmentIds.Contains(x.InstructorAssignmentId.Value) && x.IsActive)
                .OrderBy(x => x.DayOfWeek).ThenBy(x => x.RoutineTimeSlotId).Take(1000).ToListAsync(cancellationToken);
        IReadOnlyList<RoutineEntryDto> rows = await MapEntriesAsync(entries, cancellationToken);
        return ApiResponse<IReadOnlyList<RoutineEntryDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<RoutineEntryDto>> CreateEntryAsync(
        SaveRoutineEntryRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<RoutineEntryDto>();
        if (request == null || request.SubjectOfferingReference == Guid.Empty || request.RoutineTimeSlotId <= 0 ||
            !Enum.IsDefined(request.DayOfWeek) || request.EffectiveFrom == default ||
            (request.EffectiveTo.HasValue && request.EffectiveTo < request.EffectiveFrom) ||
            (request.InstructorAssignmentId.HasValue && request.InstructorAssignmentId <= 0))
            return Error<RoutineEntryDto>("Invalid routine entry.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var offering = await _offerings.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.PublicId == request.SubjectOfferingReference && x.IsActive, token);
                if (offering == null) return Error<RoutineEntryDto>("Subject offering not found.", 404);
                var batch = await GetBatchAsync(offering.AcademicBatchId, token);
                if (batch == null || batch.AcademicYearId != offering.AcademicYearId)
                    return Error<RoutineEntryDto>("Academic batch is unavailable.", 409);
                var range = await ResolveEffectiveRangeAsync(batch, offering.AcademicTermId, token);
                if (!range.IsValid || request.EffectiveFrom < range.From ||
                    request.EffectiveTo.GetValueOrDefault(range.To ?? DateOnly.MaxValue) >
                    range.To.GetValueOrDefault(DateOnly.MaxValue))
                    return Error<RoutineEntryDto>("Routine dates must belong to the academic year or term.", 409);
                var slot = await _timeSlots.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.RoutineTimeSlotId && x.IsActive, token);
                if (slot == null) return Error<RoutineEntryDto>("Active time slot not found.", 404);
                if (slot.IsBreak) return Error<RoutineEntryDto>("Cannot schedule a class during a break.", 409);

                InstructorAssignment? assignment = null;
                if (request.InstructorAssignmentId.HasValue)
                {
                    assignment = await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && x.Id == request.InstructorAssignmentId.Value &&
                        x.SubjectOfferingId == offering.Id && x.IsActive, token);
                    if (assignment == null) return Error<RoutineEntryDto>("Instructor assignment not found for this subject offering.", 409);
                    if (request.EffectiveFrom < assignment.EffectiveFrom ||
                        request.EffectiveTo.GetValueOrDefault(DateOnly.MaxValue) >
                        assignment.EffectiveTo.GetValueOrDefault(DateOnly.MaxValue))
                        return Error<RoutineEntryDto>("Routine dates must be covered by instructor assignment.", 409);
                }
                Room? room = null;
                if (request.RoomId.HasValue)
                {
                    room = await _rooms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && x.Id == request.RoomId && x.IsActive, token);
                    if (room == null) return Error<RoutineEntryDto>("Room not found.", 404);
                    if (room.CampusId != batch.CampusId || room.Capacity <= 0 ||
                        (batch.Capacity > 0 && room.Capacity < batch.Capacity))
                        return Error<RoutineEntryDto>("Room capacity or campus is incompatible with this batch.", 409);
                }
                var duplicate = await _entries.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.SubjectOfferingId == offering.Id &&
                    x.RoutineTimeSlotId == slot.Id && x.DayOfWeek == request.DayOfWeek &&
                    x.EffectiveFrom == request.EffectiveFrom && x.EffectiveTo == request.EffectiveTo &&
                    x.InstructorAssignmentId == request.InstructorAssignmentId &&
                    x.RoomId == request.RoomId && x.IsActive, token);
                if (duplicate != null)
                    return ApiResponse<RoutineEntryDto>.SuccessResponse(
                        (await MapEntriesAsync([duplicate], token))[0], "Routine entry already exists.");
                var relevant = await _entries.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.DayOfWeek == request.DayOfWeek && x.IsActive &&
                    x.EffectiveFrom <= request.EffectiveTo.GetValueOrDefault(DateOnly.MaxValue) &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= request.EffectiveFrom))
                    .Take(2000).ToListAsync(token);
                if (relevant.Count == 2000) return Error<RoutineEntryDto>("Schedule conflict check exceeded its safe limit.", 409);
                if (relevant.Count > 0)
                {
                    var others = relevant.Select(x => x.SubjectOfferingId).Distinct().ToArray();
                    var offerings = await _offerings.GetQueryable().AsNoTracking()
                        .Where(x => x.TenantId == tenant && others.Contains(x.Id))
                        .ToDictionaryAsync(x => x.Id, token);
                    var assignmentIds = relevant.Where(x => x.InstructorAssignmentId.HasValue)
                        .Select(x => x.InstructorAssignmentId!.Value).Distinct().ToArray();
                    var assigned = await _assignments.GetQueryable().AsNoTracking()
                        .Where(x => x.TenantId == tenant && assignmentIds.Contains(x.Id))
                        .ToDictionaryAsync(x => x.Id, token);
                    var slotIds = relevant.Select(x => x.RoutineTimeSlotId).Distinct().ToArray();
                    var slots = await _timeSlots.GetQueryable().AsNoTracking()
                        .Where(x => x.TenantId == tenant && slotIds.Contains(x.Id))
                        .ToDictionaryAsync(x => x.Id, token);
                    foreach (var other in relevant)
                    {
                        if (!slots.TryGetValue(other.RoutineTimeSlotId, out var otherSlot) ||
                            !TimeOverlaps(slot, otherSlot)) continue;
                        if (offerings.TryGetValue(other.SubjectOfferingId, out var otherOffering) &&
                            otherOffering.AcademicBatchId == batch.Id)
                            return Error<RoutineEntryDto>("Batch already has class at this time.", 409);
                        if (assignment != null && other.InstructorAssignmentId.HasValue &&
                            assigned.TryGetValue(other.InstructorAssignmentId.Value, out var otherAssignment) &&
                            assignment.EmployeeId == otherAssignment.EmployeeId)
                            return Error<RoutineEntryDto>("Instructor already has class at this time.", 409);
                        if (room != null && other.RoomId == room.Id)
                            return Error<RoutineEntryDto>("Room is occupied at this time.", 409);
                    }
                }
                var row = new RoutineEntry
                {
                    TenantId = tenant, SubjectOfferingId = offering.Id, RoutineTimeSlotId = slot.Id,
                    InstructorAssignmentId = request.InstructorAssignmentId, RoomId = request.RoomId,
                    DayOfWeek = request.DayOfWeek, EffectiveFrom = request.EffectiveFrom,
                    EffectiveTo = request.EffectiveTo, IsActive = request.IsActive,
                    CreatedAt = _clock.GetUtcNow().UtcDateTime, CreatedBy = _currentUser.UserId
                };
                await _entries.AddAsync(row);
                await _unitOfWork.SaveChangesAsync(token);
                return Created((await MapEntriesAsync([row], token))[0], "Routine entry created.");
            }, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Routine entry conflict for tenant {TenantId}", tenant);
            return Error<RoutineEntryDto>("Routine entry conflicts with another update.", 409);
        }
    }

    public async Task<ApiResponse<RoutineEntryDto>> DeactivateEntryAsync(long id, string rowVersion,
        CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<RoutineEntryDto>();
        if (id <= 0 || string.IsNullOrWhiteSpace(rowVersion))
            return Error<RoutineEntryDto>("Entry and row version are required.");
        byte[] version;
        try { version = Convert.FromBase64String(rowVersion); }
        catch (FormatException) { return Error<RoutineEntryDto>("Invalid row version."); }
        if (version.Length == 0) return Error<RoutineEntryDto>("Invalid row version.");
        var row = await _entries.GetQueryable().FirstOrDefaultAsync(x =>
            x.TenantId == _currentUser.TenantId && x.Id == id, ct);
        if (row == null) return Error<RoutineEntryDto>("Routine entry not found.", 404);
        if (row.RowVersion.Length != version.Length ||
            !CryptographicOperations.FixedTimeEquals(row.RowVersion, version))
            return Error<RoutineEntryDto>("Routine entry was changed. Reload and retry.", 409);
        if (!row.IsActive)
            return ApiResponse<RoutineEntryDto>.SuccessResponse((await MapEntriesAsync([row], ct))[0], "Routine entry is inactive.");
        row.IsActive = false;
        row.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        row.UpdatedBy = _currentUser.UserId;
        _entries.Update(row);
        try { await _unitOfWork.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Error<RoutineEntryDto>("Routine entry changed concurrently.", 409); }
        return ApiResponse<RoutineEntryDto>.SuccessResponse((await MapEntriesAsync([row], ct))[0], "Routine entry deactivated.");
    }

    private async Task<AcademicBatch?> GetBatchAsync(long id, CancellationToken cancellationToken) =>
        id <= 0 ? null : await _batches.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id && x.IsActive, cancellationToken);

    private async Task<List<long>> GetOfferingIdsAsync(long batchId, long academicYearId, long? termId, CancellationToken cancellationToken) =>
        await _offerings.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.AcademicBatchId == batchId &&
                        x.AcademicYearId == academicYearId && x.AcademicTermId == termId && x.IsActive)
            .Select(x => x.Id).Take(1000).ToListAsync(cancellationToken);

    private async Task<SubjectOffering?> ResolveOfferingAsync(
        AcademicBatch batch, long? termId, long subjectId, CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        var curriculumSubjectIds = await _curriculumSubjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.SubjectId == subjectId &&
                        x.AcademicLevelId == batch.AcademicLevelId && x.IsActive)
            .Select(x => x.Id).Take(100).ToListAsync(cancellationToken);
        if (curriculumSubjectIds.Count == 0) return null;
        return await _offerings.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.AcademicBatchId == batch.Id &&
                        x.AcademicYearId == batch.AcademicYearId && x.AcademicTermId == termId &&
                        curriculumSubjectIds.Contains(x.CurriculumSubjectId) && x.IsActive)
            .OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<(bool IsValid, long? TermId, string? Error)> ResolveTermAsync(
        AcademicBatch batch, long? requestedTermId, CancellationToken cancellationToken)
    {
        var termId = requestedTermId ?? batch.AcademicTermId;
        if (batch.AcademicTermId.HasValue && termId != batch.AcademicTermId)
            return (false, null, "Academic term does not match the selected batch.");
        if (termId.HasValue && !await _terms.GetQueryable().AsNoTracking()
            .AnyAsync(x => x.TenantId == _currentUser.TenantId && x.Id == termId.Value &&
                           x.AcademicYearId == batch.AcademicYearId && x.IsActive, cancellationToken))
            return (false, null, "Academic term does not belong to the selected academic year.");
        return (true, termId, null);
    }

    private async Task<(bool IsValid, DateOnly From, DateOnly? To, string? Error)> ResolveEffectiveRangeAsync(
        AcademicBatch batch, long? termId, CancellationToken cancellationToken)
    {
        if (termId.HasValue)
        {
            var term = await _terms.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == termId.Value &&
                                          x.AcademicYearId == batch.AcademicYearId && x.IsActive, cancellationToken);
            if (term == null) return (false, default, null, "Academic term not found.");
            return (true, batch.StartDate.HasValue && batch.StartDate.Value > term.StartDate ? batch.StartDate.Value : term.StartDate,
                    batch.EndDate.HasValue && batch.EndDate.Value < term.EndDate ? batch.EndDate.Value : term.EndDate, null);
        }

        var year = await _years.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == batch.AcademicYearId && x.IsActive, cancellationToken);
        if (year == null) return (false, default, null, "Academic year not found.");
        return (true, batch.StartDate ?? year.StartDate, batch.EndDate ?? year.EndDate, null);
    }

    private async Task<List<InstructorAssignmentDto>> MapAssignmentsAsync(
        IReadOnlyList<InstructorAssignment> assignments, CancellationToken cancellationToken)
    {
        if (assignments.Count == 0) return [];
        var tenantId = _currentUser.TenantId;
        var employeeIds = assignments.Select(x => x.EmployeeId).Distinct().ToArray();
        var employees = await _employees.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && employeeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        return assignments.Select(x =>
        {
            employees.TryGetValue(x.EmployeeId, out var employee);
            return new InstructorAssignmentDto
            {
                Id = x.Id,
                SubjectOfferingId = x.SubjectOfferingId,
                EmployeeId = x.EmployeeId,
                EmployeeReference = employee?.PublicId ?? Guid.Empty,
                EmployeeCode = employee?.EmployeeCode ?? string.Empty,
                EmployeeName = employee?.FullName ?? string.Empty,
                IsPrimary = x.IsPrimary,
                EffectiveFrom = x.EffectiveFrom,
                EffectiveTo = x.EffectiveTo,
                IsActive = x.IsActive,
                RowVersion = Convert.ToBase64String(x.RowVersion)
            };
        }).ToList();
    }

    private async Task<List<RoutineEntryDto>> MapEntriesAsync(
        IReadOnlyList<RoutineEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return [];
        var tenantId = _currentUser.TenantId;
        var offeringIds = entries.Select(x => x.SubjectOfferingId).Distinct().ToArray();
        var slotIds = entries.Select(x => x.RoutineTimeSlotId).Distinct().ToArray();
        var roomIds = entries.Where(x => x.RoomId.HasValue).Select(x => x.RoomId!.Value).Distinct().ToArray();

        var offerings = await _offerings.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && offeringIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var curriculumSubjectIds = offerings.Values.Select(x => x.CurriculumSubjectId).Distinct().ToArray();
        var curriculumSubjects = await _curriculumSubjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && curriculumSubjectIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var subjectIds = curriculumSubjects.Values.Select(x => x.SubjectId).Distinct().ToArray();
        var subjects = await _subjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && subjectIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var slots = await _timeSlots.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && slotIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var rooms = roomIds.Length == 0
            ? new Dictionary<long, Room>()
            : await _rooms.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && roomIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        return entries.Select(x =>
        {
            offerings.TryGetValue(x.SubjectOfferingId, out var offering);
            CurriculumSubject? curriculumSubject = null;
            Subject? subject = null;
            if (offering != null && curriculumSubjects.TryGetValue(offering.CurriculumSubjectId, out curriculumSubject))
                subjects.TryGetValue(curriculumSubject.SubjectId, out subject);
            slots.TryGetValue(x.RoutineTimeSlotId, out var slot);
            Room? room = null;
            if (x.RoomId.HasValue) rooms.TryGetValue(x.RoomId.Value, out room);
            return new RoutineEntryDto
            {
                Id = x.Id,
                SubjectOfferingId = x.SubjectOfferingId,
                SubjectOfferingReference = offering?.PublicId ?? Guid.Empty,
                SubjectName = subject?.Name ?? string.Empty,
                RoutineTimeSlotId = x.RoutineTimeSlotId,
                TimeSlotName = slot?.Name ?? string.Empty,
                RoomId = x.RoomId,
                RoomName = room?.Name,
                DayOfWeek = x.DayOfWeek,
                EffectiveFrom = x.EffectiveFrom,
                EffectiveTo = x.EffectiveTo,
                IsActive = x.IsActive,
                RowVersion = Convert.ToBase64String(x.RowVersion)
            };
        }).OrderBy(x => x.DayOfWeek).ThenBy(x => slots.TryGetValue(x.RoutineTimeSlotId, out var slot) ? slot.StartTime : default)
          .ThenBy(x => x.Id).ToList();
    }

    private Task<long?> GetLinkedTeacherIdAsync(CancellationToken cancellationToken) =>
        _employees.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.UserId == _currentUser.UserId &&
                        x.State == EmployeeState.Active && x.CanTeach)
            .Select(x => (long?)x.Id).FirstOrDefaultAsync(cancellationToken);

    private static bool TimeOverlaps(RoutineTimeSlot a, RoutineTimeSlot b) => a.StartTime < b.EndTime && a.EndTime > b.StartTime;
    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (IsManager() || _currentUser.IsInRole("Teacher"));
    private bool CanManage() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && IsManager();
    private bool IsManager() => _currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal") || _currentUser.IsInRole("VicePrincipal");
    private static RoutineTimeSlotDto MapTimeSlot(RoutineTimeSlot x) => new()
    {
        Id = x.Id,
        Name = x.Name,
        StartTime = x.StartTime,
        EndTime = x.EndTime,
        IsBreak = x.IsBreak,
        IsActive = x.IsActive,
        DisplayOrder = x.DisplayOrder,
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };
    private static ApiResponse<T> Created<T>(T data, string message) => new() { Success = true, StatusCode = 201, Message = message, Data = data };
    private static ApiResponse<T> Error<T>(string message, int statusCode = 400) => ApiResponse<T>.ErrorResponse(message, statusCode);
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Academic routine access is required.", 403);
}
