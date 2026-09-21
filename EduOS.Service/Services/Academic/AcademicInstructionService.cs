using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Employees;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Transactions;

namespace EduOS.Service.Services.Academic;

public sealed class AcademicInstructionService : IAcademicInstructionService
{
    private readonly IGenericRepository<Substitution> _substitutions;
    private readonly IGenericRepository<LessonPlan> _lessonPlans;
    private readonly IGenericRepository<RoutineEntry> _entries;
    private readonly IGenericRepository<InstructorAssignment> _assignments;
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicTerm> _terms;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<AcademicInstructionService> _logger;

    public AcademicInstructionService(
        IGenericRepository<Substitution> substitutions,
        IGenericRepository<LessonPlan> lessonPlans,
        IGenericRepository<RoutineEntry> entries,
        IGenericRepository<InstructorAssignment> assignments,
        IGenericRepository<Employee> employees,
        IGenericRepository<AcademicYear> years,
        IGenericRepository<AcademicTerm> terms,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        TimeProvider clock,
        ILogger<AcademicInstructionService> logger)
    {
        _substitutions = substitutions;
        _lessonPlans = lessonPlans;
        _entries = entries;
        _assignments = assignments;
        _employees = employees;
        _years = years;
        _terms = terms;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<RoutineSubstitutionDto>>> GetSubstitutionsAsync(DateTime fromDate, DateTime toDate, long? academicBatchId, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<RoutineSubstitutionDto>>();
        var range = NormalizeRange(fromDate, toDate, 93);
        if (!range.Success || (academicBatchId.HasValue && academicBatchId.Value <= 0)) return Error<IReadOnlyList<RoutineSubstitutionDto>>(range.Error ?? "Academic batch, when supplied, must be positive.");
        var query = SubstitutionQuery().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.RoutineEntryId.HasValue && x.Date >= range.From && x.Date <= range.To);
        if (academicBatchId.HasValue) query = query.Where(x => x.AcademicBatchId == academicBatchId.Value);
        if (!IsManager())
        {
            var teacherId = await GetLinkedTeacherIdAsync(cancellationToken);
            if (!teacherId.HasValue) return Denied<IReadOnlyList<RoutineSubstitutionDto>>();
            query = query.Where(x => x.OriginalTeacherId == teacherId.Value || x.SubstituteTeacherId == teacherId.Value);
        }
        IReadOnlyList<RoutineSubstitutionDto> rows = (await query.OrderBy(x => x.Date).ThenBy(x => x.RoutineTimeSlot!.StartTime).ToListAsync(cancellationToken)).Select(MapSubstitution).ToList();
        return ApiResponse<IReadOnlyList<RoutineSubstitutionDto>>.SuccessResponse(rows);
    }

    public Task<ApiResponse<RoutineSubstitutionDto>> CreateSubstitutionAsync(CreateRoutineSubstitutionDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<RoutineSubstitutionDto>());
        if (request == null || request.ClientRequestId == Guid.Empty || request.RoutineEntryId <= 0 || request.SubstituteTeacherId <= 0 || request.Date == default || TooLong(request.Reason, 1000))
            return Task.FromResult(Error<RoutineSubstitutionDto>("A valid request ID, routine entry, date and substitute instructor are required."));
        return ExecuteWriteAsync("create routine substitution", async () =>
        {
            var tenantId = _currentUser.TenantId;
            var date = request.Date.Date;
            var reason = Trim(request.Reason);
            var replay = await SubstitutionQuery().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (replay != null)
            {
                if (replay.RoutineEntryId != request.RoutineEntryId || replay.Date.Date != date || replay.SubstituteTeacherId != request.SubstituteTeacherId || replay.Reason != reason)
                    return Error<RoutineSubstitutionDto>("Client request ID was already used for a different substitution.", 409);
                return ApiResponse<RoutineSubstitutionDto>.SuccessResponse(MapSubstitution(replay), "Routine substitution already exists.");
            }

            var entry = await RoutineEntryQuery().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.RoutineEntryId && x.IsActive, cancellationToken);
            if (entry?.AcademicBatch == null || entry.RoutineTimeSlot == null || entry.Subject == null || entry.Employee == null)
                return Error<RoutineSubstitutionDto>("Active routine entry not found.", 404);
            if (date.DayOfWeek != entry.DayOfWeek) return Error<RoutineSubstitutionDto>("Substitution date does not match the routine day.", 409);
            var dateError = await ValidateAcademicDateAsync(entry.AcademicYearId, entry.AcademicTermId, date, date, cancellationToken);
            if (dateError != null) return Error<RoutineSubstitutionDto>(dateError, 409);

            var substitute = await _employees.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.SubstituteTeacherId && x.IsActive && x.IsTeacher, cancellationToken);
            if (substitute == null) return Error<RoutineSubstitutionDto>("Active substitute instructor not found.", 404);
            if (substitute.Id == entry.EmployeeId) return Error<RoutineSubstitutionDto>("Original and substitute instructor must be different.", 409);

            var existingTarget = await SubstitutionQuery().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.RoutineEntryId == entry.Id && x.Date == date && x.IsActive, cancellationToken);
            if (existingTarget != null)
            {
                if (existingTarget.SubstituteTeacherId == substitute.Id && existingTarget.Reason == reason)
                    return ApiResponse<RoutineSubstitutionDto>.SuccessResponse(MapSubstitution(existingTarget), "Routine substitution already exists.");
                return Error<RoutineSubstitutionDto>("This routine entry already has an active substitution for the selected date.", 409);
            }

            var releasedEntryIds = await _substitutions.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Date == date && x.OriginalTeacherId == substitute.Id && x.IsActive && x.RoutineEntryId.HasValue)
                .Select(x => x.RoutineEntryId!.Value).ToListAsync(cancellationToken);
            var routineConflict = await _entries.GetQueryable().AsNoTracking().Include(x => x.RoutineTimeSlot)
                .AnyAsync(x => x.TenantId == tenantId && x.Id != entry.Id && x.EmployeeId == substitute.Id && x.AcademicYearId == entry.AcademicYearId && x.DayOfWeek == date.DayOfWeek && x.IsActive && !releasedEntryIds.Contains(x.Id) && (x.AcademicTermId == entry.AcademicTermId || x.AcademicTermId == null || entry.AcademicTermId == null) && x.RoutineTimeSlot != null && x.RoutineTimeSlot.StartTime < entry.RoutineTimeSlot.EndTime && x.RoutineTimeSlot.EndTime > entry.RoutineTimeSlot.StartTime, cancellationToken);
            if (routineConflict) return Error<RoutineSubstitutionDto>("Substitute instructor already has a routine during this time.", 409);
            var substitutionConflict = await _substitutions.GetQueryable().AsNoTracking().Include(x => x.RoutineTimeSlot)
                .AnyAsync(x => x.TenantId == tenantId && x.Date == date && x.SubstituteTeacherId == substitute.Id && x.IsActive && x.RoutineTimeSlot != null && x.RoutineTimeSlot.StartTime < entry.RoutineTimeSlot.EndTime && x.RoutineTimeSlot.EndTime > entry.RoutineTimeSlot.StartTime, cancellationToken);
            if (substitutionConflict) return Error<RoutineSubstitutionDto>("Substitute instructor already covers another class during this time.", 409);

            var row = new Substitution
            {
                TenantId = tenantId,
                ClientRequestId = request.ClientRequestId,
                Date = date,
                OriginalTeacherId = entry.EmployeeId,
                SubstituteTeacherId = substitute.Id,
                ClassId = entry.Subject.ClassId,
                SubjectId = entry.SubjectId,
                RoutineEntryId = entry.Id,
                AcademicBatchId = entry.AcademicBatchId,
                RoutineTimeSlotId = entry.RoutineTimeSlotId,
                AcademicYearId = entry.AcademicYearId,
                AcademicTermId = entry.AcademicTermId,
                Period = entry.RoutineTimeSlot.Name,
                Reason = reason,
                IsActive = true,
                OriginalTeacher = entry.Employee,
                SubstituteTeacher = substitute,
                Subject = entry.Subject,
                RoutineEntry = entry,
                AcademicBatch = entry.AcademicBatch,
                RoutineTimeSlot = entry.RoutineTimeSlot,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
                CreatedBy = _currentUser.UserId
            };
            await _substitutions.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(MapSubstitution(row), "Routine substitution created.");
        });
    }

    public Task<ApiResponse<RoutineSubstitutionDto>> CancelSubstitutionAsync(long id, CancelRoutineSubstitutionDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<RoutineSubstitutionDto>());
        if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.Reason) || TooLong(request.Reason, 1000) || !TryDecodeRowVersion(request.RowVersion, out var rowVersion))
            return Task.FromResult(Error<RoutineSubstitutionDto>("A valid substitution, row version and cancellation reason are required."));
        return ExecuteWriteAsync("cancel routine substitution", async () =>
        {
            var row = await SubstitutionQuery().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id && x.RoutineEntryId.HasValue, cancellationToken);
            if (row == null) return Error<RoutineSubstitutionDto>("Routine substitution not found.", 404);
            if (!row.IsActive)
            {
                if (row.CancellationReason != request.Reason.Trim()) return Error<RoutineSubstitutionDto>("Routine substitution was already cancelled with a different reason.", 409);
                return ApiResponse<RoutineSubstitutionDto>.SuccessResponse(MapSubstitution(row), "Routine substitution is already cancelled.");
            }
            if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, rowVersion)) return Stale<RoutineSubstitutionDto>();
            row.IsActive = false;
            row.CancelledAt = _clock.GetUtcNow().UtcDateTime;
            row.CancelledBy = _currentUser.UserId;
            row.CancellationReason = request.Reason.Trim();
            row.UpdatedAt = row.CancelledAt;
            row.UpdatedBy = _currentUser.UserId;
            _substitutions.Update(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<RoutineSubstitutionDto>.SuccessResponse(MapSubstitution(row), "Routine substitution cancelled.");
        });
    }

    public async Task<ApiResponse<IReadOnlyList<LessonPlanDto>>> GetLessonPlansAsync(long? academicBatchId, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<LessonPlanDto>>();
        if (academicBatchId.HasValue && academicBatchId.Value <= 0) return Error<IReadOnlyList<LessonPlanDto>>("Academic batch, when supplied, must be positive.");
        if (fromDate.HasValue != toDate.HasValue) return Error<IReadOnlyList<LessonPlanDto>>("Both lesson-plan range dates are required.");
        var query = LessonPlanQuery().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.InstructorAssignmentId.HasValue && x.IsActive);
        if (academicBatchId.HasValue) query = query.Where(x => x.AcademicBatchId == academicBatchId.Value);
        if (fromDate.HasValue && toDate.HasValue)
        {
            var range = NormalizeRange(fromDate.Value, toDate.Value, 366);
            if (!range.Success) return Error<IReadOnlyList<LessonPlanDto>>(range.Error!);
            query = query.Where(x => x.StartDate <= range.To && x.EndDate >= range.From);
        }
        if (!IsManager())
        {
            var teacherId = await GetLinkedTeacherIdAsync(cancellationToken);
            if (!teacherId.HasValue) return Denied<IReadOnlyList<LessonPlanDto>>();
            query = query.Where(x => x.TeacherId == teacherId.Value);
        }
        IReadOnlyList<LessonPlanDto> rows = (await query.OrderBy(x => x.StartDate).ThenBy(x => x.ChapterName).ToListAsync(cancellationToken)).Select(MapLessonPlan).ToList();
        return ApiResponse<IReadOnlyList<LessonPlanDto>>.SuccessResponse(rows);
    }

    public Task<ApiResponse<LessonPlanDto>> CreateLessonPlanAsync(CreateLessonPlanDto request, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Task.FromResult(Denied<LessonPlanDto>());
        var inputError = ValidateLessonInput(request?.ChapterName, request?.Topic, request?.StartDate ?? default, request?.EndDate ?? default, request?.Description, request?.LearningObjectives, request?.Resources);
        if (request == null || request.ClientRequestId == Guid.Empty || request.InstructorAssignmentId <= 0 || inputError != null)
            return Task.FromResult(Error<LessonPlanDto>(inputError ?? "Request ID and instructor assignment are required."));
        return ExecuteWriteAsync("create lesson plan", async () =>
        {
            var tenantId = _currentUser.TenantId;
            var start = request.StartDate.Date;
            var end = request.EndDate.Date;
            var chapter = request.ChapterName.Trim();
            var topic = Trim(request.Topic);
            var description = Trim(request.Description);
            var objectives = Trim(request.LearningObjectives);
            var resources = Trim(request.Resources);
            var replay = await LessonPlanQuery().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (replay != null)
            {
                if (!await OwnsTeacherAsync(replay.TeacherId, cancellationToken)) return Error<LessonPlanDto>("Lesson plan not found.", 404);
                if (!SameLesson(replay, request.InstructorAssignmentId, chapter, topic, start, end, description, objectives, resources))
                    return Error<LessonPlanDto>("Client request ID was already used for a different lesson plan.", 409);
                return ApiResponse<LessonPlanDto>.SuccessResponse(MapLessonPlan(replay), "Lesson plan already exists.");
            }

            var assignment = await AssignmentQuery().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.InstructorAssignmentId && x.IsActive, cancellationToken);
            if (assignment?.AcademicBatch == null || assignment.Subject == null || assignment.Employee == null)
                return Error<LessonPlanDto>("Active instructor assignment not found.", 404);
            if (!await OwnsTeacherAsync(assignment.EmployeeId, cancellationToken)) return Error<LessonPlanDto>("Instructor assignment not found.", 404);
            var dateError = await ValidateAcademicDateAsync(assignment.AcademicYearId, assignment.AcademicTermId, start, end, cancellationToken);
            if (dateError != null) return Error<LessonPlanDto>(dateError, 409);
            var naturalKey = BuildLessonNaturalKey(assignment.Id, chapter, start, end);
            var existing = await LessonPlanQuery().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.NaturalKey == naturalKey && x.IsActive, cancellationToken);
            if (existing != null)
            {
                if (SameLesson(existing, assignment.Id, chapter, topic, start, end, description, objectives, resources))
                    return ApiResponse<LessonPlanDto>.SuccessResponse(MapLessonPlan(existing), "Lesson plan already exists.");
                return Error<LessonPlanDto>("A lesson plan already uses this assignment, chapter and date range.", 409);
            }
            var row = new LessonPlan
            {
                TenantId = tenantId,
                ClientRequestId = request.ClientRequestId,
                NaturalKey = naturalKey,
                ClassId = assignment.Subject.ClassId,
                SubjectId = assignment.SubjectId,
                TeacherId = assignment.EmployeeId,
                InstructorAssignmentId = assignment.Id,
                AcademicBatchId = assignment.AcademicBatchId,
                AcademicYearId = assignment.AcademicYearId,
                AcademicTermId = assignment.AcademicTermId,
                ChapterName = chapter,
                Topic = topic,
                StartDate = start,
                EndDate = end,
                Description = description,
                LearningObjectives = objectives,
                Resources = resources,
                Status = LessonPlanStatus.Draft.ToString(),
                ProgressPercent = 0,
                IsActive = true,
                Subject = assignment.Subject,
                Teacher = assignment.Employee,
                InstructorAssignment = assignment,
                AcademicBatch = assignment.AcademicBatch,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
                CreatedBy = _currentUser.UserId
            };
            await _lessonPlans.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(MapLessonPlan(row), "Lesson plan created as draft.");
        });
    }

    public Task<ApiResponse<LessonPlanDto>> UpdateLessonPlanAsync(long id, UpdateLessonPlanDto request, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Task.FromResult(Denied<LessonPlanDto>());
        var inputError = ValidateLessonInput(request?.ChapterName, request?.Topic, request?.StartDate ?? default, request?.EndDate ?? default, request?.Description, request?.LearningObjectives, request?.Resources);
        if (id <= 0 || request == null || inputError != null || !TryDecodeRowVersion(request.RowVersion, out var rowVersion))
            return Task.FromResult(Error<LessonPlanDto>(inputError ?? "A valid lesson plan and row version are required."));
        return ExecuteWriteAsync("update lesson plan", async () =>
        {
            var row = await LessonPlanQuery().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id && x.InstructorAssignmentId.HasValue && x.IsActive, cancellationToken);
            if (row == null || !await OwnsTeacherAsync(row.TeacherId, cancellationToken)) return Error<LessonPlanDto>("Lesson plan not found.", 404);
            var status = ParseStatus(row.Status);
            if (status is not (LessonPlanStatus.Draft or LessonPlanStatus.Rejected)) return Error<LessonPlanDto>("Only draft or rejected lesson plans can be edited.", 409);
            if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, rowVersion)) return Stale<LessonPlanDto>();
            if (!row.AcademicYearId.HasValue) return Error<LessonPlanDto>("Legacy lesson plan has no academic year and cannot use this workflow.", 409);
            var start = request.StartDate.Date;
            var end = request.EndDate.Date;
            var dateError = await ValidateAcademicDateAsync(row.AcademicYearId.Value, row.AcademicTermId, start, end, cancellationToken);
            if (dateError != null) return Error<LessonPlanDto>(dateError, 409);
            var chapter = request.ChapterName.Trim();
            var naturalKey = BuildLessonNaturalKey(row.InstructorAssignmentId!.Value, chapter, start, end);
            if (await _lessonPlans.GetQueryable().AnyAsync(x => x.TenantId == _currentUser.TenantId && x.Id != row.Id && x.NaturalKey == naturalKey && x.IsActive, cancellationToken))
                return Error<LessonPlanDto>("A lesson plan already uses this assignment, chapter and date range.", 409);
            row.NaturalKey = naturalKey;
            row.ChapterName = chapter;
            row.Topic = Trim(request.Topic);
            row.StartDate = start;
            row.EndDate = end;
            row.Description = Trim(request.Description);
            row.LearningObjectives = Trim(request.LearningObjectives);
            row.Resources = Trim(request.Resources);
            row.Status = LessonPlanStatus.Draft.ToString();
            row.ReviewedAt = null;
            row.ReviewedBy = null;
            row.ReviewRemarks = null;
            Touch(row);
            _lessonPlans.Update(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<LessonPlanDto>.SuccessResponse(MapLessonPlan(row), "Lesson plan updated.");
        });
    }

    public Task<ApiResponse<LessonPlanDto>> SubmitLessonPlanAsync(long id, AcademicRowVersionDto request, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Task.FromResult(Denied<LessonPlanDto>());
        if (id <= 0 || request == null || !TryDecodeRowVersion(request.RowVersion, out var rowVersion)) return Task.FromResult(Error<LessonPlanDto>("A valid lesson plan and row version are required."));
        return ExecuteWriteAsync("submit lesson plan", async () =>
        {
            var row = await LessonPlanQuery().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id && x.InstructorAssignmentId.HasValue && x.IsActive, cancellationToken);
            if (row == null || !await OwnsTeacherAsync(row.TeacherId, cancellationToken)) return Error<LessonPlanDto>("Lesson plan not found.", 404);
            var status = ParseStatus(row.Status);
            if (status == LessonPlanStatus.Submitted) return ApiResponse<LessonPlanDto>.SuccessResponse(MapLessonPlan(row), "Lesson plan is already submitted.");
            if (status is not (LessonPlanStatus.Draft or LessonPlanStatus.Rejected)) return Error<LessonPlanDto>("Lesson plan cannot be submitted from its current status.", 409);
            if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, rowVersion)) return Stale<LessonPlanDto>();
            row.Status = LessonPlanStatus.Submitted.ToString();
            row.SubmittedAt = _clock.GetUtcNow().UtcDateTime;
            row.SubmittedBy = _currentUser.UserId;
            row.ReviewedAt = null;
            row.ReviewedBy = null;
            row.ReviewRemarks = null;
            Touch(row);
            _lessonPlans.Update(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<LessonPlanDto>.SuccessResponse(MapLessonPlan(row), "Lesson plan submitted for review.");
        });
    }

    public Task<ApiResponse<LessonPlanDto>> ReviewLessonPlanAsync(long id, LessonPlanReviewDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<LessonPlanDto>());
        if (id <= 0 || request == null || (!request.Approve && string.IsNullOrWhiteSpace(request.Remarks)) || TooLong(request.Remarks, 1000) || !TryDecodeRowVersion(request.RowVersion, out var rowVersion))
            return Task.FromResult(Error<LessonPlanDto>("A valid decision, row version and rejection reason are required."));
        return ExecuteWriteAsync("review lesson plan", async () =>
        {
            var row = await LessonPlanQuery().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id && x.InstructorAssignmentId.HasValue && x.IsActive, cancellationToken);
            if (row == null) return Error<LessonPlanDto>("Lesson plan not found.", 404);
            var target = request.Approve ? LessonPlanStatus.Approved : LessonPlanStatus.Rejected;
            if (ParseStatus(row.Status) == target)
            {
                if (row.ReviewRemarks != Trim(request.Remarks)) return Error<LessonPlanDto>("Lesson plan was already reviewed with different remarks.", 409);
                return ApiResponse<LessonPlanDto>.SuccessResponse(MapLessonPlan(row), $"Lesson plan is already {target.ToString().ToLowerInvariant()}.");
            }
            if (ParseStatus(row.Status) != LessonPlanStatus.Submitted) return Error<LessonPlanDto>("Only submitted lesson plans can be reviewed.", 409);
            if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, rowVersion)) return Stale<LessonPlanDto>();
            row.Status = target.ToString();
            row.ReviewedAt = _clock.GetUtcNow().UtcDateTime;
            row.ReviewedBy = _currentUser.UserId;
            row.ReviewRemarks = Trim(request.Remarks);
            Touch(row);
            _lessonPlans.Update(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<LessonPlanDto>.SuccessResponse(MapLessonPlan(row), request.Approve ? "Lesson plan approved." : "Lesson plan rejected.");
        });
    }

    public Task<ApiResponse<LessonPlanDto>> RecordLessonProgressAsync(long id, LessonPlanProgressDto request, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Task.FromResult(Denied<LessonPlanDto>());
        if (id <= 0 || request == null || request.ProgressPercent is < 0 or > 100 || TooLong(request.Notes, 2000) || !TryDecodeRowVersion(request.RowVersion, out var rowVersion))
            return Task.FromResult(Error<LessonPlanDto>("A valid progress percentage and row version are required."));
        return ExecuteWriteAsync("record lesson progress", async () =>
        {
            var row = await LessonPlanQuery().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id && x.InstructorAssignmentId.HasValue && x.IsActive, cancellationToken);
            if (row == null || !await OwnsTeacherAsync(row.TeacherId, cancellationToken)) return Error<LessonPlanDto>("Lesson plan not found.", 404);
            var status = ParseStatus(row.Status);
            if (status == LessonPlanStatus.Completed && request.ProgressPercent == 100)
            {
                if (row.ProgressNotes != Trim(request.Notes)) return Error<LessonPlanDto>("Lesson plan was completed with different progress notes.", 409);
                return ApiResponse<LessonPlanDto>.SuccessResponse(MapLessonPlan(row), "Lesson plan is already complete.");
            }
            if (status is not (LessonPlanStatus.Approved or LessonPlanStatus.InProgress)) return Error<LessonPlanDto>("Only approved lesson plans can record progress.", 409);
            if (request.ProgressPercent < row.ProgressPercent) return Error<LessonPlanDto>("Lesson progress cannot decrease.", 409);
            if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, rowVersion)) return Stale<LessonPlanDto>();
            row.ProgressPercent = request.ProgressPercent;
            row.ProgressNotes = Trim(request.Notes);
            row.Status = request.ProgressPercent == 100 ? LessonPlanStatus.Completed.ToString() : request.ProgressPercent > 0 ? LessonPlanStatus.InProgress.ToString() : LessonPlanStatus.Approved.ToString();
            row.CompletedAt = request.ProgressPercent == 100 ? _clock.GetUtcNow().UtcDateTime : null;
            Touch(row);
            _lessonPlans.Update(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<LessonPlanDto>.SuccessResponse(MapLessonPlan(row), request.ProgressPercent == 100 ? "Lesson plan completed." : "Lesson progress updated.");
        });
    }

    private IQueryable<Substitution> SubstitutionQuery() => _substitutions.GetQueryable()
        .Include(x => x.RoutineEntry).Include(x => x.AcademicBatch).Include(x => x.Subject)
        .Include(x => x.OriginalTeacher).Include(x => x.SubstituteTeacher).Include(x => x.RoutineTimeSlot);

    private IQueryable<RoutineEntry> RoutineEntryQuery() => _entries.GetQueryable()
        .Include(x => x.AcademicBatch).Include(x => x.RoutineTimeSlot).Include(x => x.Subject).Include(x => x.Employee);

    private IQueryable<InstructorAssignment> AssignmentQuery() => _assignments.GetQueryable()
        .Include(x => x.AcademicBatch).Include(x => x.Subject).Include(x => x.Employee);

    private IQueryable<LessonPlan> LessonPlanQuery() => _lessonPlans.GetQueryable()
        .Include(x => x.AcademicBatch).Include(x => x.Subject).Include(x => x.Teacher).Include(x => x.InstructorAssignment);

    private async Task<string?> ValidateAcademicDateAsync(long academicYearId, long? academicTermId, DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        var year = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == academicYearId && x.IsActive, cancellationToken);
        if (year == null) return "Academic year not found.";
        if (start < year.StartDate.Date || end > year.EndDate.Date) return "Date range is outside the academic year.";
        if (!academicTermId.HasValue) return null;
        var term = await _terms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == academicTermId.Value && x.AcademicYearId == academicYearId && x.IsActive, cancellationToken);
        if (term == null) return "Academic term not found in this academic year.";
        if (!term.StartDate.HasValue || !term.EndDate.HasValue) return "Academic term dates must be configured first.";
        return start < term.StartDate.Value.Date || end > term.EndDate.Value.Date ? "Date range is outside the academic term." : null;
    }

    private async Task<bool> OwnsTeacherAsync(long teacherId, CancellationToken cancellationToken)
    {
        if (IsManager()) return true;
        var linked = await GetLinkedTeacherIdAsync(cancellationToken);
        return linked.HasValue && linked.Value == teacherId;
    }

    private async Task<long?> GetLinkedTeacherIdAsync(CancellationToken cancellationToken) => await _employees.GetQueryable().AsNoTracking()
        .Where(x => x.TenantId == _currentUser.TenantId && x.UserId == _currentUser.UserId && x.IsActive && x.IsTeacher)
        .Select(x => (long?)x.Id).FirstOrDefaultAsync(cancellationToken);

    private Task<ApiResponse<T>> ExecuteWriteAsync<T>(string operation, Func<Task<ApiResponse<T>>> action) => ExecuteAsync(operation, action);

    private async Task<ApiResponse<T>> ExecuteAsync<T>(string operation, Func<Task<ApiResponse<T>>> action)
    {
        try
        {
            var strategy = _unitOfWork.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);
                var response = await action();
                if (response.Success) scope.Complete();
                return response;
            });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Stale academic instruction write during {Operation} for tenant {TenantId}", operation, _currentUser.TenantId);
            return Stale<T>();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting academic instruction write during {Operation} for tenant {TenantId}", operation, _currentUser.TenantId);
            return Error<T>("Academic instruction data conflicts with another update. Reload and try again.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Serialized academic instruction write aborted during {Operation} for tenant {TenantId}", operation, _currentUser.TenantId);
            return Error<T>("Academic instruction data conflicts with another update. Reload and try again.", 409);
        }
    }

    private void Touch(LessonPlan row)
    {
        row.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        row.UpdatedBy = _currentUser.UserId;
    }

    private static string? ValidateLessonInput(string? chapter, string? topic, DateTime start, DateTime end, string? description, string? objectives, string? resources)
    {
        if (string.IsNullOrWhiteSpace(chapter) || chapter.Trim().Length > 500) return "Chapter name is required and cannot exceed 500 characters.";
        if (TooLong(topic, 500) || TooLong(description, 2000) || TooLong(objectives, 2000) || TooLong(resources, 2000)) return "Lesson-plan content exceeds its allowed length.";
        if (start == default || end == default || end.Date < start.Date) return "Lesson-plan date range is invalid.";
        return null;
    }

    private static (bool Success, DateTime From, DateTime To, string? Error) NormalizeRange(DateTime from, DateTime to, int maxDays)
    {
        var start = from.Date;
        var end = to.Date;
        if (from == default || to == default || end < start) return (false, start, end, "Date range is invalid.");
        if ((end - start).TotalDays >= maxDays) return (false, start, end, $"Date range cannot exceed {maxDays} days.");
        return (true, start, end, null);
    }

    private static string BuildLessonNaturalKey(long assignmentId, string chapter, DateTime start, DateTime end)
    {
        var input = $"{assignmentId.ToString(CultureInfo.InvariantCulture)}|{chapter.Trim().ToUpperInvariant()}|{start.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}|{end.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private static bool SameLesson(LessonPlan row, long assignmentId, string chapter, string? topic, DateTime start, DateTime end, string? description, string? objectives, string? resources) =>
        row.InstructorAssignmentId == assignmentId && row.ChapterName == chapter && row.Topic == topic && row.StartDate.Date == start && row.EndDate.Date == end && row.Description == description && row.LearningObjectives == objectives && row.Resources == resources;

    private static LessonPlanStatus ParseStatus(string value)
    {
        return Enum.TryParse<LessonPlanStatus>(value, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : LessonPlanStatus.Unknown;
    }

    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (IsManager() || _currentUser.IsInRole("Teacher"));
    private bool CanManage() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && IsManager();
    private bool IsManager() => _currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal") || _currentUser.IsInRole("VicePrincipal");
    private static bool TooLong(string? value, int max) => value != null && value.Length > max;
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool TryDecodeRowVersion(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { bytes = Convert.FromBase64String(value); return bytes.Length > 0; }
        catch (FormatException) { return false; }
    }

    private static RoutineSubstitutionDto MapSubstitution(Substitution x) => new()
    {
        Id = x.Id,
        Date = x.Date,
        RoutineEntryId = x.RoutineEntryId ?? 0,
        AcademicBatchId = x.AcademicBatchId ?? 0,
        BatchName = x.AcademicBatch?.Name ?? string.Empty,
        SubjectId = x.SubjectId,
        SubjectName = x.Subject?.Name ?? string.Empty,
        OriginalTeacherId = x.OriginalTeacherId,
        OriginalTeacherName = x.OriginalTeacher?.FullName ?? string.Empty,
        SubstituteTeacherId = x.SubstituteTeacherId,
        SubstituteTeacherName = x.SubstituteTeacher?.FullName ?? string.Empty,
        RoutineTimeSlotId = x.RoutineTimeSlotId ?? 0,
        TimeSlotName = x.RoutineTimeSlot?.Name ?? x.Period ?? string.Empty,
        StartTime = x.RoutineTimeSlot?.StartTime ?? default,
        EndTime = x.RoutineTimeSlot?.EndTime ?? default,
        Reason = x.Reason,
        IsActive = x.IsActive,
        CancelledAt = x.CancelledAt,
        CancellationReason = x.CancellationReason,
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };

    private static LessonPlanDto MapLessonPlan(LessonPlan x) => new()
    {
        Id = x.Id,
        InstructorAssignmentId = x.InstructorAssignmentId ?? 0,
        AcademicBatchId = x.AcademicBatchId ?? 0,
        BatchName = x.AcademicBatch?.Name ?? string.Empty,
        SubjectId = x.SubjectId,
        SubjectName = x.Subject?.Name ?? string.Empty,
        TeacherId = x.TeacherId,
        TeacherName = x.Teacher?.FullName ?? string.Empty,
        AcademicYearId = x.AcademicYearId ?? 0,
        AcademicTermId = x.AcademicTermId,
        ChapterName = x.ChapterName,
        Topic = x.Topic,
        StartDate = x.StartDate,
        EndDate = x.EndDate,
        Description = x.Description,
        LearningObjectives = x.LearningObjectives,
        Resources = x.Resources,
        ProgressNotes = x.ProgressNotes,
        Status = ParseStatus(x.Status),
        ProgressPercent = x.ProgressPercent,
        SubmittedAt = x.SubmittedAt,
        ReviewedAt = x.ReviewedAt,
        ReviewedBy = x.ReviewedBy,
        ReviewRemarks = x.ReviewRemarks,
        CompletedAt = x.CompletedAt,
        IsActive = x.IsActive,
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };

    private static ApiResponse<T> Created<T>(T data, string message) => new() { Success = true, StatusCode = 201, Message = message, Data = data };
    private static ApiResponse<T> Error<T>(string message, int statusCode = 400) => ApiResponse<T>.ErrorResponse(message, statusCode);
    private static ApiResponse<T> Stale<T>() => Error<T>("Academic instruction data changed. Reload and try again.", 409);
    private static ApiResponse<T> Denied<T>() => Error<T>("Academic instruction access is required.", 403);
}
