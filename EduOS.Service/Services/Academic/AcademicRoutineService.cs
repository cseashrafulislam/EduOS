using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Employees;
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
    private readonly IGenericRepository<AcademicTerm> _terms;
    private readonly IGenericRepository<AcademicCurriculum> _curricula;
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
        IGenericRepository<AcademicTerm> terms,
        IGenericRepository<AcademicCurriculum> curricula,
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
        _terms = terms;
        _curricula = curricula;
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
        var tenantId = _currentUser.TenantId;
        IReadOnlyList<RoutineTimeSlotDto> rows = await _timeSlots.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.StartTime).ThenBy(x => x.DisplayOrder)
            .Select(x => new RoutineTimeSlotDto { Id = x.Id, Name = x.Name, StartTime = x.StartTime, EndTime = x.EndTime, IsBreak = x.IsBreak, IsActive = x.IsActive })
            .ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<RoutineTimeSlotDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<RoutineTimeSlotDto>> CreateTimeSlotAsync(CreateRoutineTimeSlotDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<RoutineTimeSlotDto>();
        if (request == null || string.IsNullOrWhiteSpace(request.Name)) return Error<RoutineTimeSlotDto>("Time-slot name is required.");
        if (request.StartTime < TimeSpan.Zero || request.EndTime <= request.StartTime || request.EndTime >= TimeSpan.FromDays(1)) return Error<RoutineTimeSlotDto>("Time-slot start and end time are invalid.");
        var tenantId = _currentUser.TenantId;
        var name = request.Name.Trim();
        try
        {
            var strategy = _unitOfWork.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
            using var scope = SerializableScope();
            var existing = await _timeSlots.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.IsActive && (x.Name == name || (x.StartTime == request.StartTime && x.EndTime == request.EndTime)), cancellationToken);
            if (existing != null)
            {
                if (existing.Name != name || existing.StartTime != request.StartTime || existing.EndTime != request.EndTime || existing.IsBreak != request.IsBreak) return Error<RoutineTimeSlotDto>("An active time slot already uses this name or time range.", 409);
                scope.Complete();
                return ApiResponse<RoutineTimeSlotDto>.SuccessResponse(Map(existing), "Time slot already exists.");
            }
            var row = new RoutineTimeSlot { TenantId = tenantId, Name = name, StartTime = request.StartTime, EndTime = request.EndTime, IsBreak = request.IsBreak, IsActive = true, CreatedAt = _clock.GetUtcNow().UtcDateTime, CreatedBy = _currentUser.UserId };
            await _timeSlots.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            scope.Complete();
            return Created(Map(row), "Time slot created.");
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

    public async Task<ApiResponse<IReadOnlyList<InstructorAssignmentDto>>> GetAssignmentsAsync(long academicBatchId, long? academicTermId, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<InstructorAssignmentDto>>();
        var tenantId = _currentUser.TenantId;
        var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == academicBatchId && x.IsActive, cancellationToken);
        if (batch == null) return Error<IReadOnlyList<InstructorAssignmentDto>>("Academic batch not found.", 404);
        var term = await ResolveTermAsync(batch, academicTermId, cancellationToken);
        if (!term.IsValid) return Error<IReadOnlyList<InstructorAssignmentDto>>(term.Error!);
        var employeeId = await GetLinkedTeacherIdAsync(cancellationToken);
        if (!IsManager() && !employeeId.HasValue) return Denied<IReadOnlyList<InstructorAssignmentDto>>();
        var query = AssignmentQuery().AsNoTracking().Where(x => x.TenantId == tenantId && x.AcademicBatchId == batch.Id && x.AcademicYearId == batch.AcademicYearId && x.AcademicTermId == term.TermId && x.IsActive);
        if (!IsManager()) query = query.Where(x => x.EmployeeId == employeeId!.Value);
        var assignments = await query.OrderBy(x => x.Subject!.DisplayOrder).ThenBy(x => x.Subject!.Name).ThenBy(x => x.Employee!.FullName).ToListAsync(cancellationToken);
        IReadOnlyList<InstructorAssignmentDto> rows = assignments.Select(MapAssignment).ToList();
        return ApiResponse<IReadOnlyList<InstructorAssignmentDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<InstructorAssignmentDto>> AssignInstructorAsync(AssignInstructorDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<InstructorAssignmentDto>();
        if (request == null || request.AcademicBatchId <= 0 || request.SubjectId <= 0 || request.EmployeeId <= 0) return Error<InstructorAssignmentDto>("Batch, subject and instructor are required.");
        var tenantId = _currentUser.TenantId;
        try
        {
            var strategy = _unitOfWork.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
            using var scope = SerializableScope();
            var batch = await _batches.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.AcademicBatchId && x.IsActive, cancellationToken);
            if (batch == null) return Error<InstructorAssignmentDto>("Academic batch not found.", 404);
            var term = await ResolveTermAsync(batch, request.AcademicTermId, cancellationToken);
            if (!term.IsValid) return Error<InstructorAssignmentDto>(term.Error!);
            var subject = await _subjects.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.SubjectId && x.IsActive, cancellationToken);
            if (subject == null) return Error<InstructorAssignmentDto>("Subject not found.", 404);
            var employee = await _employees.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.EmployeeId && x.IsActive && x.IsTeacher, cancellationToken);
            if (employee == null) return Error<InstructorAssignmentDto>("Active instructor not found.", 404);
            var curriculumError = await ValidateCurriculumSubjectAsync(batch, subject.Id, cancellationToken);
            if (curriculumError != null) return Error<InstructorAssignmentDto>(curriculumError, 409);
            var existing = await AssignmentQuery().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.AcademicBatchId == batch.Id && x.SubjectId == subject.Id && x.EmployeeId == employee.Id && x.AcademicYearId == batch.AcademicYearId && x.AcademicTermId == term.TermId && x.IsActive, cancellationToken);
            if (existing != null)
            {
                if (existing.IsPrimary != request.IsPrimary || existing.IsClassAdvisor != request.IsClassAdvisor) return Error<InstructorAssignmentDto>("Instructor is already assigned with different assignment settings.", 409);
                scope.Complete();
                return ApiResponse<InstructorAssignmentDto>.SuccessResponse(MapAssignment(existing), "Instructor assignment already exists.");
            }
            if (request.IsPrimary && await _assignments.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.AcademicBatchId == batch.Id && x.SubjectId == subject.Id && x.AcademicYearId == batch.AcademicYearId && x.AcademicTermId == term.TermId && x.IsPrimary && x.IsActive, cancellationToken)) return Error<InstructorAssignmentDto>("This subject already has a primary instructor for the selected batch and term.", 409);
            var row = new InstructorAssignment { TenantId = tenantId, AcademicBatchId = batch.Id, SubjectId = subject.Id, EmployeeId = employee.Id, AcademicYearId = batch.AcademicYearId, AcademicTermId = term.TermId, IsPrimary = request.IsPrimary, IsClassAdvisor = request.IsClassAdvisor, IsActive = true, CreatedAt = _clock.GetUtcNow().UtcDateTime, CreatedBy = _currentUser.UserId, AcademicBatch = batch, Subject = subject, Employee = employee };
            await _assignments.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            scope.Complete();
            return Created(MapAssignment(row), "Instructor assigned.");
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

    public async Task<ApiResponse<IReadOnlyList<RoutineEntryDto>>> GetBatchTimetableAsync(long academicBatchId, long? academicTermId, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<RoutineEntryDto>>();
        var tenantId = _currentUser.TenantId;
        var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == academicBatchId && x.IsActive, cancellationToken);
        if (batch == null) return Error<IReadOnlyList<RoutineEntryDto>>("Academic batch not found.", 404);
        var term = await ResolveTermAsync(batch, academicTermId, cancellationToken);
        if (!term.IsValid) return Error<IReadOnlyList<RoutineEntryDto>>(term.Error!);
        if (!IsManager())
        {
            var employeeId = await GetLinkedTeacherIdAsync(cancellationToken);
            if (!employeeId.HasValue || !await _assignments.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.AcademicBatchId == batch.Id && x.AcademicYearId == batch.AcademicYearId && x.AcademicTermId == term.TermId && x.EmployeeId == employeeId.Value && x.IsActive, cancellationToken)) return Denied<IReadOnlyList<RoutineEntryDto>>();
        }
        var rows = await MapEntriesAsync(EntryQuery().Where(x => x.TenantId == tenantId && x.AcademicBatchId == batch.Id && x.AcademicYearId == batch.AcademicYearId && x.AcademicTermId == term.TermId && x.IsActive), cancellationToken);
        return ApiResponse<IReadOnlyList<RoutineEntryDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<RoutineEntryDto>>> GetTeacherTimetableAsync(long employeeId, long academicYearId, long? academicTermId, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<RoutineEntryDto>>();
        var tenantId = _currentUser.TenantId;
        var employee = await _employees.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == employeeId && x.IsActive && x.IsTeacher, cancellationToken);
        if (employee == null) return Error<IReadOnlyList<RoutineEntryDto>>("Instructor not found.", 404);
        if (!IsManager() && employee.UserId != _currentUser.UserId) return Denied<IReadOnlyList<RoutineEntryDto>>();
        if (academicTermId.HasValue && !await _terms.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Id == academicTermId.Value && x.AcademicYearId == academicYearId && x.IsActive, cancellationToken)) return Error<IReadOnlyList<RoutineEntryDto>>("Academic term does not belong to the selected academic year.");
        var rows = await MapEntriesAsync(EntryQuery().Where(x => x.TenantId == tenantId && x.EmployeeId == employee.Id && x.AcademicYearId == academicYearId && x.AcademicTermId == academicTermId && x.IsActive), cancellationToken);
        return ApiResponse<IReadOnlyList<RoutineEntryDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<RoutineEntryDto>> CreateEntryAsync(CreateRoutineEntryDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<RoutineEntryDto>();
        if (request == null || request.InstructorAssignmentId <= 0 || request.RoutineTimeSlotId <= 0 || !Enum.IsDefined(typeof(DayOfWeek), request.DayOfWeek)) return Error<RoutineEntryDto>("Routine entry request is invalid.");
        var tenantId = _currentUser.TenantId;
        try
        {
            var strategy = _unitOfWork.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
            using var scope = SerializableScope();
            var assignment = await AssignmentQuery().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.InstructorAssignmentId && x.IsActive, cancellationToken);
            if (assignment == null || assignment.AcademicBatch == null || assignment.Subject == null || assignment.Employee == null) return Error<RoutineEntryDto>("Instructor assignment not found.", 404);
            var slot = await _timeSlots.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.RoutineTimeSlotId && x.IsActive, cancellationToken);
            if (slot == null) return Error<RoutineEntryDto>("Routine time slot not found.", 404);
            if (slot.IsBreak) return Error<RoutineEntryDto>("A break time slot cannot contain a class.", 409);
            Room? room = null;
            if (request.RoomId.HasValue)
            {
                room = await _rooms.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.RoomId.Value && x.IsActive, cancellationToken);
                if (room == null) return Error<RoutineEntryDto>("Room not found.", 404);
                if (room.CampusId.HasValue && room.CampusId.Value != assignment.AcademicBatch.CampusId) return Error<RoutineEntryDto>("Room belongs to a different campus.", 409);
                if (room.Capacity <= 0) return Error<RoutineEntryDto>("Room capacity is not configured.", 409);
                if (assignment.AcademicBatch.Capacity > 0 && room.Capacity < assignment.AcademicBatch.Capacity) return Error<RoutineEntryDto>("Room capacity is smaller than the academic batch capacity.", 409);
            }
            var replay = await EntryQuery().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.AcademicBatchId == assignment.AcademicBatchId && x.AcademicYearId == assignment.AcademicYearId && x.AcademicTermId == assignment.AcademicTermId && x.SubjectId == assignment.SubjectId && x.EmployeeId == assignment.EmployeeId && x.DayOfWeek == request.DayOfWeek && x.RoutineTimeSlotId == slot.Id && x.RoomId == request.RoomId && x.IsActive, cancellationToken);
            if (replay != null)
            {
                var replayDto = await MapEntryAsync(replay, cancellationToken);
                scope.Complete();
                return ApiResponse<RoutineEntryDto>.SuccessResponse(replayDto, "Routine entry already exists.");
            }
            var conflicts = await _entries.GetQueryable().AsNoTracking().Include(x => x.RoutineTimeSlot)
                .Where(x => x.TenantId == tenantId && x.AcademicYearId == assignment.AcademicYearId && x.DayOfWeek == request.DayOfWeek && x.IsActive && (x.AcademicTermId == assignment.AcademicTermId || x.AcademicTermId == null || assignment.AcademicTermId == null) && x.RoutineTimeSlot != null && x.RoutineTimeSlot.StartTime < slot.EndTime && x.RoutineTimeSlot.EndTime > slot.StartTime && (x.AcademicBatchId == assignment.AcademicBatchId || x.EmployeeId == assignment.EmployeeId || (room != null && x.RoomId == room.Id)))
                .Select(x => new { x.AcademicBatchId, x.EmployeeId, x.RoomId })
                .ToListAsync(cancellationToken);
            if (conflicts.Any(x => x.AcademicBatchId == assignment.AcademicBatchId)) return Error<RoutineEntryDto>("Academic batch already has a class during this time.", 409);
            if (conflicts.Any(x => x.EmployeeId == assignment.EmployeeId)) return Error<RoutineEntryDto>("Instructor already has a class during this time.", 409);
            if (room != null && conflicts.Any(x => x.RoomId == room.Id)) return Error<RoutineEntryDto>("Room is already occupied during this time.", 409);
            var row = new RoutineEntry { TenantId = tenantId, AcademicBatchId = assignment.AcademicBatchId, RoutineTimeSlotId = slot.Id, DayOfWeek = request.DayOfWeek, SubjectId = assignment.SubjectId, EmployeeId = assignment.EmployeeId, RoomId = room?.Id, AcademicYearId = assignment.AcademicYearId, AcademicTermId = assignment.AcademicTermId, Remarks = Trim(request.Remarks), IsActive = true, CreatedAt = _clock.GetUtcNow().UtcDateTime, CreatedBy = _currentUser.UserId, AcademicBatch = assignment.AcademicBatch, RoutineTimeSlot = slot, Subject = assignment.Subject, Employee = assignment.Employee };
            await _entries.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            scope.Complete();
            return Created(MapEntry(row, room?.Name), "Routine entry created.");
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
        var tenantId = _currentUser.TenantId;
        var row = await EntryQuery().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (row == null) return Error<RoutineEntryDto>("Routine entry not found.", 404);
        if (!row.IsActive) return ApiResponse<RoutineEntryDto>.SuccessResponse(await MapEntryAsync(row, cancellationToken), "Routine entry is already inactive.");
        row.IsActive = false;
        row.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        row.UpdatedBy = _currentUser.UserId;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ApiResponse<RoutineEntryDto>.SuccessResponse(await MapEntryAsync(row, cancellationToken), "Routine entry deactivated.");
    }

    private IQueryable<InstructorAssignment> AssignmentQuery() => _assignments.GetQueryable().Include(x => x.AcademicBatch).Include(x => x.Subject).Include(x => x.Employee).Include(x => x.AcademicYear).Include(x => x.AcademicTerm);
    private IQueryable<RoutineEntry> EntryQuery() => _entries.GetQueryable().Include(x => x.AcademicBatch).Include(x => x.RoutineTimeSlot).Include(x => x.Subject).Include(x => x.Employee);

    private async Task<IReadOnlyList<RoutineEntryDto>> MapEntriesAsync(IQueryable<RoutineEntry> query, CancellationToken cancellationToken)
    {
        var entries = await query.AsNoTracking().OrderBy(x => x.DayOfWeek).ThenBy(x => x.RoutineTimeSlot!.StartTime).ThenBy(x => x.AcademicBatch!.Name).ToListAsync(cancellationToken);
        var roomIds = entries.Where(x => x.RoomId.HasValue).Select(x => x.RoomId!.Value).Distinct().ToList();
        var roomNames = roomIds.Count == 0 ? new Dictionary<long, string>() : await _rooms.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && roomIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        return entries.Select(x => MapEntry(x, x.RoomId.HasValue && roomNames.TryGetValue(x.RoomId.Value, out var name) ? name : null)).ToList();
    }

    private async Task<RoutineEntryDto> MapEntryAsync(RoutineEntry entry, CancellationToken cancellationToken)
    {
        string? roomName = null;
        if (entry.RoomId.HasValue) roomName = await _rooms.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.Id == entry.RoomId.Value).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken);
        return MapEntry(entry, roomName);
    }

    private async Task<(bool IsValid, long? TermId, string? Error)> ResolveTermAsync(AcademicBatch batch, long? requestedTermId, CancellationToken cancellationToken)
    {
        var termId = requestedTermId ?? batch.AcademicTermId;
        if (batch.AcademicTermId.HasValue && termId != batch.AcademicTermId) return (false, null, "Academic term does not match the selected batch.");
        if (termId.HasValue && !await _terms.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == _currentUser.TenantId && x.Id == termId.Value && x.AcademicYearId == batch.AcademicYearId && x.IsActive, cancellationToken)) return (false, null, "Academic term does not belong to the selected academic year.");
        return (true, termId, null);
    }

    private async Task<string?> ValidateCurriculumSubjectAsync(AcademicBatch batch, long subjectId, CancellationToken cancellationToken)
    {
        var curriculumIds = await _curricula.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.AcademicProgramId == batch.AcademicProgramId && x.IsActive && (x.IsCurrent || x.EffectiveFromAcademicYearId == batch.AcademicYearId)).Select(x => x.Id).ToListAsync(cancellationToken);
        if (curriculumIds.Count == 0) return "An active curriculum is not configured for the academic batch.";
        var allowed = await _curriculumSubjects.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == _currentUser.TenantId && curriculumIds.Contains(x.AcademicCurriculumId) && x.AcademicLevelId == batch.AcademicLevelId && x.SubjectId == subjectId && x.IsActive && (!x.AcademicTrackId.HasValue || x.AcademicTrackId == batch.AcademicTrackId) && (!x.MediumId.HasValue || x.MediumId == batch.MediumId), cancellationToken);
        return allowed ? null : "Subject is not part of the active curriculum for this batch.";
    }

    private async Task<long?> GetLinkedTeacherIdAsync(CancellationToken cancellationToken) => await _employees.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.UserId == _currentUser.UserId && x.IsActive && x.IsTeacher).Select(x => (long?)x.Id).FirstOrDefaultAsync(cancellationToken);
    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (IsManager() || _currentUser.IsInRole("Teacher"));
    private bool CanManage() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && IsManager();
    private bool IsManager() => _currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal") || _currentUser.IsInRole("VicePrincipal");
    private static TransactionScope SerializableScope() => new(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static RoutineTimeSlotDto Map(RoutineTimeSlot x) => new() { Id = x.Id, Name = x.Name, StartTime = x.StartTime, EndTime = x.EndTime, IsBreak = x.IsBreak, IsActive = x.IsActive };
    private static InstructorAssignmentDto MapAssignment(InstructorAssignment x) => new() { Id = x.Id, AcademicBatchId = x.AcademicBatchId, BatchName = x.AcademicBatch?.Name ?? string.Empty, SubjectId = x.SubjectId, SubjectName = x.Subject?.Name ?? string.Empty, EmployeeId = x.EmployeeId, EmployeeName = x.Employee?.FullName ?? string.Empty, AcademicYearId = x.AcademicYearId, AcademicTermId = x.AcademicTermId, IsPrimary = x.IsPrimary, IsClassAdvisor = x.IsClassAdvisor, IsActive = x.IsActive };
    private static RoutineEntryDto MapEntry(RoutineEntry x, string? roomName) => new() { Id = x.Id, AcademicBatchId = x.AcademicBatchId, BatchName = x.AcademicBatch?.Name ?? string.Empty, SubjectId = x.SubjectId, SubjectName = x.Subject?.Name ?? string.Empty, EmployeeId = x.EmployeeId, EmployeeName = x.Employee?.FullName ?? string.Empty, AcademicYearId = x.AcademicYearId, AcademicTermId = x.AcademicTermId, DayOfWeek = x.DayOfWeek, RoutineTimeSlotId = x.RoutineTimeSlotId, TimeSlotName = x.RoutineTimeSlot?.Name ?? string.Empty, StartTime = x.RoutineTimeSlot?.StartTime ?? default, EndTime = x.RoutineTimeSlot?.EndTime ?? default, RoomId = x.RoomId, RoomName = roomName, Remarks = x.Remarks, IsActive = x.IsActive };
    private static ApiResponse<T> Created<T>(T data, string message) => new() { Success = true, StatusCode = 201, Message = message, Data = data };
    private static ApiResponse<T> Error<T>(string message, int statusCode = 400) => ApiResponse<T>.ErrorResponse(message, statusCode);
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Academic routine access is required.", 403);
}
