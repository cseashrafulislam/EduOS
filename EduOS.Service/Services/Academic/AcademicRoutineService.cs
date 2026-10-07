using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.HR;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

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

    public async Task<ApiResponse<IReadOnlyList<RoutineTimeSlotDto>>> GetTimeSlotsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<RoutineTimeSlotDto>>();
        IReadOnlyList<RoutineTimeSlotDto> rows = await _timeSlots.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.IsActive)
            .OrderBy(x => x.StartTime).ThenBy(x => x.DisplayOrder).ThenBy(x => x.Id)
            .Select(x => MapTimeSlot(x)).Take(200).ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<RoutineTimeSlotDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<RoutineTimeSlotDto>> CreateTimeSlotAsync(CreateRoutineTimeSlotDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<RoutineTimeSlotDto>();
        if (request == null || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
            return Error<RoutineTimeSlotDto>("Time-slot name is required.");
        if (request.StartTime < TimeSpan.Zero || request.EndTime <= request.StartTime || request.EndTime >= TimeSpan.FromDays(1))
            return Error<RoutineTimeSlotDto>("Time-slot start and end time are invalid.");

        var tenantId = _currentUser.TenantId;
        var name = request.Name.Trim();
        var start = TimeOnly.FromTimeSpan(request.StartTime);
        var end = TimeOnly.FromTimeSpan(request.EndTime);
        try
        {
            var strategy = _unitOfWork.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var scope = SerializableScope();
                var existing = await _timeSlots.GetQueryable()
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.IsActive &&
                                              (x.Name == name || (x.StartTime == start && x.EndTime == end)), cancellationToken);
                if (existing != null)
                {
                    if (existing.Name != name || existing.StartTime != start || existing.EndTime != end || existing.IsBreak != request.IsBreak)
                        return Error<RoutineTimeSlotDto>("An active time slot already uses this name or exact time range.", 409);
                    scope.Complete();
                    return ApiResponse<RoutineTimeSlotDto>.SuccessResponse(MapTimeSlot(existing), "Time slot already exists.");
                }

                var row = new RoutineTimeSlot
                {
                    TenantId = tenantId,
                    Name = name,
                    StartTime = start,
                    EndTime = end,
                    IsBreak = request.IsBreak,
                    IsActive = true,
                    DisplayOrder = 0,
                    CreatedAt = _clock.GetUtcNow().UtcDateTime,
                    CreatedBy = _currentUser.UserId
                };
                await _timeSlots.AddAsync(row);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                scope.Complete();
                return Created(MapTimeSlot(row), "Time slot created.");
            });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting routine time slot for tenant {TenantId}", tenantId);
            return Error<RoutineTimeSlotDto>("Time slot conflicts with another update. Reload and try again.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Serialized time-slot transaction aborted for tenant {TenantId}", tenantId);
            return Error<RoutineTimeSlotDto>("Time slot conflicts with another update. Reload and try again.", 409);
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
        AssignInstructorDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<InstructorAssignmentDto>();
        if (request == null || request.AcademicBatchId <= 0 || request.SubjectId <= 0 || request.EmployeeId <= 0)
            return Error<InstructorAssignmentDto>("Batch, subject and instructor are required.");
        if (request.IsClassAdvisor)
            return Error<InstructorAssignmentDto>("Class-advisor assignment is not represented by the final instructor-assignment model.", 409);

        var tenantId = _currentUser.TenantId;
        try
        {
            var strategy = _unitOfWork.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var scope = SerializableScope();
                var batch = await GetBatchAsync(request.AcademicBatchId, cancellationToken);
                if (batch == null) return Error<InstructorAssignmentDto>("Academic batch not found.", 404);
                var term = await ResolveTermAsync(batch, request.AcademicTermId, cancellationToken);
                if (!term.IsValid) return Error<InstructorAssignmentDto>(term.Error!);

                var employee = await _employees.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.EmployeeId &&
                                              x.State == EmployeeState.Active && x.CanTeach, cancellationToken);
                if (employee == null) return Error<InstructorAssignmentDto>("Active instructor not found.", 404);

                var offering = await ResolveOfferingAsync(batch, term.TermId, request.SubjectId, cancellationToken);
                if (offering == null)
                    return Error<InstructorAssignmentDto>("An active subject offering for this batch, term and subject is required before assigning an instructor.", 409);

                var existing = await _assignments.GetQueryable()
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.SubjectOfferingId == offering.Id &&
                                              x.EmployeeId == employee.Id && x.IsActive, cancellationToken);
                if (existing != null)
                {
                    if (existing.IsPrimary != request.IsPrimary)
                        return Error<InstructorAssignmentDto>("Instructor is already assigned with different primary-instructor settings.", 409);
                    var mapped = await MapAssignmentsAsync([existing], cancellationToken);
                    scope.Complete();
                    return ApiResponse<InstructorAssignmentDto>.SuccessResponse(mapped[0], "Instructor assignment already exists.");
                }

                if (request.IsPrimary && await _assignments.GetQueryable().AsNoTracking()
                    .AnyAsync(x => x.TenantId == tenantId && x.SubjectOfferingId == offering.Id && x.IsPrimary && x.IsActive, cancellationToken))
                    return Error<InstructorAssignmentDto>("This subject offering already has a primary instructor.", 409);

                var effective = await ResolveEffectiveRangeAsync(batch, term.TermId, cancellationToken);
                if (!effective.IsValid) return Error<InstructorAssignmentDto>(effective.Error!);

                var row = new InstructorAssignment
                {
                    TenantId = tenantId,
                    SubjectOfferingId = offering.Id,
                    EmployeeId = employee.Id,
                    IsPrimary = request.IsPrimary,
                    EffectiveFrom = effective.From,
                    EffectiveTo = effective.To,
                    IsActive = true,
                    CreatedAt = _clock.GetUtcNow().UtcDateTime,
                    CreatedBy = _currentUser.UserId
                };
                await _assignments.AddAsync(row);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                var result = await MapAssignmentsAsync([row], cancellationToken);
                scope.Complete();
                return Created(result[0], "Instructor assigned.");
            });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting instructor assignment for tenant {TenantId}", tenantId);
            return Error<InstructorAssignmentDto>("Instructor assignment conflicts with another update. Reload and try again.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Serialized instructor-assignment transaction aborted for tenant {TenantId}", tenantId);
            return Error<InstructorAssignmentDto>("Instructor assignment conflicts with another update. Reload and try again.", 409);
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
        CreateRoutineEntryDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<RoutineEntryDto>();
        if (request == null || request.InstructorAssignmentId <= 0 || request.RoutineTimeSlotId <= 0 ||
            !Enum.IsDefined(typeof(DayOfWeek), request.DayOfWeek))
            return Error<RoutineEntryDto>("Routine entry request is invalid.");

        var tenantId = _currentUser.TenantId;
        try
        {
            var strategy = _unitOfWork.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var scope = SerializableScope();
                var assignment = await _assignments.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.InstructorAssignmentId && x.IsActive, cancellationToken);
                if (assignment == null) return Error<RoutineEntryDto>("Instructor assignment not found.", 404);
                var offering = await _offerings.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == assignment.SubjectOfferingId && x.IsActive, cancellationToken);
                if (offering == null) return Error<RoutineEntryDto>("Subject offering is unavailable.", 409);
                var batch = await GetBatchAsync(offering.AcademicBatchId, cancellationToken);
                if (batch == null) return Error<RoutineEntryDto>("Academic batch is unavailable.", 409);
                var slot = await _timeSlots.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.RoutineTimeSlotId && x.IsActive, cancellationToken);
                if (slot == null) return Error<RoutineEntryDto>("Routine time slot not found.", 404);
                if (slot.IsBreak) return Error<RoutineEntryDto>("A break time slot cannot contain a class.", 409);

                Room? room = null;
                if (request.RoomId.HasValue)
                {
                    room = await _rooms.GetQueryable().AsNoTracking()
                        .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.RoomId.Value && x.IsActive, cancellationToken);
                    if (room == null) return Error<RoutineEntryDto>("Room not found.", 404);
                    if (room.CampusId != batch.CampusId)
                        return Error<RoutineEntryDto>("Room belongs to a different campus.", 409);
                    if (room.Capacity <= 0 || (batch.Capacity > 0 && room.Capacity < batch.Capacity))
                        return Error<RoutineEntryDto>("Room capacity is smaller than the academic batch capacity.", 409);
                }

                var replay = await _entries.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.SubjectOfferingId == offering.Id &&
                                              x.InstructorAssignmentId == assignment.Id && x.DayOfWeek == request.DayOfWeek &&
                                              x.RoutineTimeSlotId == slot.Id && x.RoomId == request.RoomId && x.IsActive, cancellationToken);
                if (replay != null)
                {
                    var replayDto = (await MapEntriesAsync([replay], cancellationToken))[0];
                    scope.Complete();
                    return ApiResponse<RoutineEntryDto>.SuccessResponse(replayDto, "Routine entry already exists.");
                }

                var effectiveFrom = assignment.EffectiveFrom;
                var effectiveTo = assignment.EffectiveTo;
                var candidateEntries = await _entries.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenantId && x.DayOfWeek == request.DayOfWeek && x.IsActive &&
                                x.EffectiveFrom <= (effectiveTo ?? DateOnly.MaxValue) &&
                                (x.EffectiveTo == null || x.EffectiveTo >= effectiveFrom))
                    .Take(2000).ToListAsync(cancellationToken);
                if (candidateEntries.Count > 0)
                {
                    var entryOfferingIds = candidateEntries.Select(x => x.SubjectOfferingId).Distinct().ToArray();
                    var entryOfferings = await _offerings.GetQueryable().AsNoTracking()
                        .Where(x => x.TenantId == tenantId && entryOfferingIds.Contains(x.Id))
                        .ToDictionaryAsync(x => x.Id, cancellationToken);
                    var entryAssignmentIds = candidateEntries.Where(x => x.InstructorAssignmentId.HasValue)
                        .Select(x => x.InstructorAssignmentId!.Value).Distinct().ToArray();
                    var entryAssignments = entryAssignmentIds.Length == 0
                        ? new Dictionary<long, InstructorAssignment>()
                        : await _assignments.GetQueryable().AsNoTracking()
                            .Where(x => x.TenantId == tenantId && entryAssignmentIds.Contains(x.Id))
                            .ToDictionaryAsync(x => x.Id, cancellationToken);
                    var entrySlotIds = candidateEntries.Select(x => x.RoutineTimeSlotId).Distinct().ToArray();
                    var entrySlots = await _timeSlots.GetQueryable().AsNoTracking()
                        .Where(x => x.TenantId == tenantId && entrySlotIds.Contains(x.Id))
                        .ToDictionaryAsync(x => x.Id, cancellationToken);

                    foreach (var other in candidateEntries)
                    {
                        if (!entrySlots.TryGetValue(other.RoutineTimeSlotId, out var otherSlot) ||
                            !TimeOverlaps(slot, otherSlot)) continue;
                        if (entryOfferings.TryGetValue(other.SubjectOfferingId, out var otherOffering) &&
                            otherOffering.AcademicBatchId == batch.Id)
                            return Error<RoutineEntryDto>("Academic batch already has a class during this time.", 409);
                        if (other.InstructorAssignmentId.HasValue &&
                            entryAssignments.TryGetValue(other.InstructorAssignmentId.Value, out var otherAssignment) &&
                            otherAssignment.EmployeeId == assignment.EmployeeId)
                            return Error<RoutineEntryDto>("Instructor already has a class during this time.", 409);
                        if (room != null && other.RoomId == room.Id)
                            return Error<RoutineEntryDto>("Room is already occupied during this time.", 409);
                    }
                }

                var row = new RoutineEntry
                {
                    TenantId = tenantId,
                    SubjectOfferingId = offering.Id,
                    RoutineTimeSlotId = slot.Id,
                    InstructorAssignmentId = assignment.Id,
                    RoomId = room?.Id,
                    DayOfWeek = request.DayOfWeek,
                    EffectiveFrom = effectiveFrom,
                    EffectiveTo = effectiveTo,
                    IsActive = true,
                    CreatedAt = _clock.GetUtcNow().UtcDateTime,
                    CreatedBy = _currentUser.UserId
                };
                await _entries.AddAsync(row);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                var result = (await MapEntriesAsync([row], cancellationToken))[0];
                scope.Complete();
                return Created(result, "Routine entry created.");
            });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting routine entry for tenant {TenantId}", tenantId);
            return Error<RoutineEntryDto>("Routine entry conflicts with another update. Reload and try again.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Serialized routine-entry transaction aborted for tenant {TenantId}", tenantId);
            return Error<RoutineEntryDto>("Routine entry conflicts with another update. Reload and try again.", 409);
        }
    }

    public async Task<ApiResponse<RoutineEntryDto>> DeactivateEntryAsync(long id, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<RoutineEntryDto>();
        var row = await _entries.GetQueryable()
            .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id, cancellationToken);
        if (row == null) return Error<RoutineEntryDto>("Routine entry not found.", 404);
        if (!row.IsActive)
        {
            var current = (await MapEntriesAsync([row], cancellationToken))[0];
            return ApiResponse<RoutineEntryDto>.SuccessResponse(current, "Routine entry is already inactive.");
        }

        row.IsActive = false;
        row.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        row.UpdatedBy = _currentUser.UserId;
        _entries.Update(row);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        var mapped = (await MapEntriesAsync([row], cancellationToken))[0];
        return ApiResponse<RoutineEntryDto>.SuccessResponse(mapped, "Routine entry deactivated.");
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
    private static TransactionScope SerializableScope() => new(
        TransactionScopeOption.Required,
        new TransactionOptions { IsolationLevel = IsolationLevel.Serializable },
        TransactionScopeAsyncFlowOption.Enabled);
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
