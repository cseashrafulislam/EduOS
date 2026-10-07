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
using System.Transactions;

namespace EduOS.Service.Services.Academic;

public sealed class AcademicInstructionService : IAcademicInstructionService
{
    private readonly IGenericRepository<Substitution> _substitutions;
    private readonly IGenericRepository<LessonPlan> _lessonPlans;
    private readonly IGenericRepository<RoutineEntry> _routineEntries;
    private readonly IGenericRepository<RoutineTimeSlot> _routineSlots;
    private readonly IGenericRepository<InstructorAssignment> _instructorAssignments;
    private readonly IGenericRepository<SubjectOffering> _subjectOfferings;
    private readonly IGenericRepository<CurriculumSubject> _curriculumSubjects;
    private readonly IGenericRepository<Subject> _subjects;
    private readonly IGenericRepository<AcademicBatch> _academicBatches;
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
        IGenericRepository<RoutineEntry> routineEntries,
        IGenericRepository<RoutineTimeSlot> routineSlots,
        IGenericRepository<InstructorAssignment> instructorAssignments,
        IGenericRepository<SubjectOffering> subjectOfferings,
        IGenericRepository<CurriculumSubject> curriculumSubjects,
        IGenericRepository<Subject> subjects,
        IGenericRepository<AcademicBatch> academicBatches,
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
        _routineEntries = routineEntries;
        _routineSlots = routineSlots;
        _instructorAssignments = instructorAssignments;
        _subjectOfferings = subjectOfferings;
        _curriculumSubjects = curriculumSubjects;
        _subjects = subjects;
        _academicBatches = academicBatches;
        _employees = employees;
        _years = years;
        _terms = terms;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<RoutineSubstitutionDto>>> GetSubstitutionsAsync(
        DateTime fromDate, DateTime toDate, long? academicBatchId, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<RoutineSubstitutionDto>>();
        var range = NormalizeRange(fromDate, toDate, 93);
        if (!range.Success || (academicBatchId.HasValue && academicBatchId.Value <= 0))
            return Error<IReadOnlyList<RoutineSubstitutionDto>>(range.Error ?? "Academic batch, when supplied, must be positive.");

        var tenantId = _currentUser.TenantId;
        var from = DateOnly.FromDateTime(range.From);
        var to = DateOnly.FromDateTime(range.To);
        var rows = await _substitutions.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Date >= from && x.Date <= to)
            .OrderBy(x => x.Date).ThenBy(x => x.Id).Take(500).ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return ApiResponse<IReadOnlyList<RoutineSubstitutionDto>>.SuccessResponse(Array.Empty<RoutineSubstitutionDto>());

        var context = await LoadRoutineContextAsync(rows.Select(x => x.RoutineEntryId), cancellationToken);
        if (academicBatchId.HasValue)
            rows = rows.Where(x => context.TryGetValue(x.RoutineEntryId, out var item) && item.BatchId == academicBatchId.Value).ToList();

        if (!IsManager())
        {
            var teacherId = await GetLinkedTeacherIdAsync(cancellationToken);
            if (!teacherId.HasValue) return Denied<IReadOnlyList<RoutineSubstitutionDto>>();
            rows = rows.Where(x => x.SubstituteEmployeeId == teacherId.Value ||
                                   (context.TryGetValue(x.RoutineEntryId, out var item) && item.OriginalEmployeeId == teacherId.Value)).ToList();
        }

        IReadOnlyList<RoutineSubstitutionDto> result = rows
            .Where(x => context.ContainsKey(x.RoutineEntryId))
            .Select(x => MapSubstitution(x, context[x.RoutineEntryId]))
            .OrderBy(x => x.Date).ThenBy(x => x.StartTime).ThenBy(x => x.Id).ToList();
        return ApiResponse<IReadOnlyList<RoutineSubstitutionDto>>.SuccessResponse(result);
    }

    public Task<ApiResponse<RoutineSubstitutionDto>> CreateSubstitutionAsync(
        CreateRoutineSubstitutionDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<RoutineSubstitutionDto>());
        if (request == null || request.ClientRequestId == Guid.Empty || request.RoutineEntryId <= 0 ||
            request.SubstituteTeacherId <= 0 || request.Date == default || TooLong(request.Reason, 500))
            return Task.FromResult(Error<RoutineSubstitutionDto>("A valid request ID, routine entry, date and substitute instructor are required."));

        return ExecuteWriteAsync("create routine substitution", async () =>
        {
            var tenantId = _currentUser.TenantId;
            var date = DateOnly.FromDateTime(request.Date.Date);
            var replay = await _substitutions.GetQueryable()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (replay != null)
            {
                if (replay.RoutineEntryId != request.RoutineEntryId || replay.Date != date ||
                    replay.SubstituteEmployeeId != request.SubstituteTeacherId || replay.Reason != Trim(request.Reason))
                    return Error<RoutineSubstitutionDto>("Client request ID was already used for a different substitution.", 409);
                var replayContext = await LoadRoutineContextAsync([replay.RoutineEntryId], cancellationToken);
                if (!replayContext.TryGetValue(replay.RoutineEntryId, out var replayInfo))
                    return Error<RoutineSubstitutionDto>("Routine entry for the existing substitution is unavailable.", 409);
                return ApiResponse<RoutineSubstitutionDto>.SuccessResponse(MapSubstitution(replay, replayInfo), "Routine substitution already exists.");
            }

            var entry = await _routineEntries.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.RoutineEntryId && x.IsActive, cancellationToken);
            if (entry == null) return Error<RoutineSubstitutionDto>("Active routine entry not found.", 404);
            if (entry.DayOfWeek != date.DayOfWeek || entry.EffectiveFrom > date || (entry.EffectiveTo.HasValue && entry.EffectiveTo.Value < date))
                return Error<RoutineSubstitutionDto>("Substitution date is outside the active routine schedule.", 409);

            var infoMap = await LoadRoutineContextAsync([entry.Id], cancellationToken);
            if (!infoMap.TryGetValue(entry.Id, out var info) || info.OriginalEmployeeId <= 0)
                return Error<RoutineSubstitutionDto>("Routine instructor assignment is incomplete.", 409);
            var dateError = await ValidateAcademicDateAsync(info.AcademicYearId, info.AcademicTermId, date, date, cancellationToken);
            if (dateError != null) return Error<RoutineSubstitutionDto>(dateError, 409);

            var substitute = await _employees.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.SubstituteTeacherId &&
                                          x.State == EmployeeState.Active && x.CanTeach, cancellationToken);
            if (substitute == null) return Error<RoutineSubstitutionDto>("Active substitute instructor not found.", 404);
            if (substitute.Id == info.OriginalEmployeeId)
                return Error<RoutineSubstitutionDto>("Original and substitute instructor must be different.", 409);

            var existing = await _substitutions.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.RoutineEntryId == entry.Id && x.Date == date && !x.IsCancelled, cancellationToken);
            if (existing != null)
            {
                if (existing.SubstituteEmployeeId == substitute.Id && existing.Reason == Trim(request.Reason))
                    return ApiResponse<RoutineSubstitutionDto>.SuccessResponse(MapSubstitution(existing, info), "Routine substitution already exists.");
                return Error<RoutineSubstitutionDto>("This routine entry already has an active substitution for the selected date.", 409);
            }

            if (await HasInstructorConflictAsync(substitute.Id, entry.Id, entry.RoutineTimeSlotId, date, cancellationToken))
                return Error<RoutineSubstitutionDto>("Substitute instructor already has a class or substitution during this time.", 409);

            var row = new Substitution
            {
                TenantId = tenantId,
                ClientRequestId = request.ClientRequestId,
                RoutineEntryId = entry.Id,
                Date = date,
                SubstituteEmployeeId = substitute.Id,
                Reason = Trim(request.Reason),
                IsCancelled = false,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
                CreatedBy = _currentUser.UserId
            };
            await _substitutions.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(MapSubstitution(row, info), "Routine substitution created.");
        });
    }

    public Task<ApiResponse<RoutineSubstitutionDto>> CancelSubstitutionAsync(
        long id, CancelRoutineSubstitutionDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<RoutineSubstitutionDto>());
        if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.Reason) ||
            TooLong(request.Reason, 1000) || !TryDecodeRowVersion(request.RowVersion, out var rowVersion))
            return Task.FromResult(Error<RoutineSubstitutionDto>("A valid substitution, row version and cancellation reason are required."));

        return ExecuteWriteAsync("cancel routine substitution", async () =>
        {
            var row = await _substitutions.GetQueryable()
                .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id, cancellationToken);
            if (row == null) return Error<RoutineSubstitutionDto>("Routine substitution not found.", 404);
            var context = await LoadRoutineContextAsync([row.RoutineEntryId], cancellationToken);
            if (!context.TryGetValue(row.RoutineEntryId, out var info))
                return Error<RoutineSubstitutionDto>("Routine entry for the substitution is unavailable.", 409);
            if (row.IsCancelled)
            {
                var already = MapSubstitution(row, info);
                already.CancellationReason = request.Reason.Trim();
                return ApiResponse<RoutineSubstitutionDto>.SuccessResponse(already, "Routine substitution is already cancelled.");
            }
            if (!VersionsMatch(row.RowVersion, rowVersion)) return Stale<RoutineSubstitutionDto>();

            row.IsCancelled = true;
            row.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            row.UpdatedBy = _currentUser.UserId;
            _substitutions.Update(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var dto = MapSubstitution(row, info);
            dto.CancelledAt = row.UpdatedAt;
            dto.CancellationReason = request.Reason.Trim();
            return ApiResponse<RoutineSubstitutionDto>.SuccessResponse(dto, "Routine substitution cancelled.");
        });
    }

    public async Task<ApiResponse<IReadOnlyList<LessonPlanDto>>> GetLessonPlansAsync(
        long? academicBatchId, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<LessonPlanDto>>();
        if (academicBatchId.HasValue && academicBatchId.Value <= 0)
            return Error<IReadOnlyList<LessonPlanDto>>("Academic batch, when supplied, must be positive.");
        if (fromDate.HasValue != toDate.HasValue)
            return Error<IReadOnlyList<LessonPlanDto>>("Both lesson-plan range dates are required.");

        var tenantId = _currentUser.TenantId;
        var query = _lessonPlans.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId);
        if (fromDate.HasValue && toDate.HasValue)
        {
            var range = NormalizeRange(fromDate.Value, toDate.Value, 366);
            if (!range.Success) return Error<IReadOnlyList<LessonPlanDto>>(range.Error!);
            var from = DateOnly.FromDateTime(range.From);
            var to = DateOnly.FromDateTime(range.To);
            query = query.Where(x => x.LessonDate >= from && x.LessonDate <= to);
        }
        if (!IsManager())
        {
            var employeeId = await GetLinkedTeacherIdAsync(cancellationToken);
            if (!employeeId.HasValue) return Denied<IReadOnlyList<LessonPlanDto>>();
            query = query.Where(x => x.EmployeeId == employeeId.Value);
        }

        var plans = await query.OrderBy(x => x.LessonDate).ThenBy(x => x.Title).Take(500).ToListAsync(cancellationToken);
        if (academicBatchId.HasValue)
        {
            var offeringIds = await _subjectOfferings.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.AcademicBatchId == academicBatchId.Value && x.IsActive)
                .Select(x => x.Id).Take(500).ToListAsync(cancellationToken);
            plans = plans.Where(x => offeringIds.Contains(x.SubjectOfferingId)).ToList();
        }

        var mapped = await MapLessonPlansAsync(plans, cancellationToken);
        return ApiResponse<IReadOnlyList<LessonPlanDto>>.SuccessResponse(mapped);
    }

    public Task<ApiResponse<LessonPlanDto>> CreateLessonPlanAsync(
        CreateLessonPlanDto request, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Task.FromResult(Denied<LessonPlanDto>());
        var inputError = ValidateLessonInput(request);
        if (inputError != null) return Task.FromResult(Error<LessonPlanDto>(inputError));

        return ExecuteWriteAsync("create lesson plan", async () =>
        {
            var tenantId = _currentUser.TenantId;
            var assignment = await _instructorAssignments.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.InstructorAssignmentId && x.IsActive, cancellationToken);
            if (assignment == null) return Error<LessonPlanDto>("Active instructor assignment not found.", 404);
            if (!await OwnsTeacherAsync(assignment.EmployeeId, cancellationToken))
                return Error<LessonPlanDto>("Instructor assignment not found.", 404);

            var offering = await _subjectOfferings.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == assignment.SubjectOfferingId && x.IsActive, cancellationToken);
            if (offering == null) return Error<LessonPlanDto>("Subject offering is unavailable.", 409);

            var lessonDate = DateOnly.FromDateTime(request.StartDate.Date);
            var dateError = await ValidateAcademicDateAsync(offering.AcademicYearId, offering.AcademicTermId, lessonDate, lessonDate, cancellationToken);
            if (dateError != null) return Error<LessonPlanDto>(dateError, 409);

            var title = request.ChapterName.Trim();
            var content = BuildLessonContent(request.Topic, request.Description);
            var existing = await _lessonPlans.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.SubjectOfferingId == offering.Id &&
                                          x.EmployeeId == assignment.EmployeeId && x.LessonDate == lessonDate && x.Title == title, cancellationToken);
            if (existing != null)
            {
                if (SameLesson(existing, request, content))
                {
                    var replay = await MapLessonPlansAsync([existing], cancellationToken);
                    return ApiResponse<LessonPlanDto>.SuccessResponse(replay[0], "Lesson plan already exists.");
                }
                return Error<LessonPlanDto>("A lesson plan already uses this instructor, subject, date and title.", 409);
            }

            var now = _clock.GetUtcNow().UtcDateTime;
            var row = new LessonPlan
            {
                TenantId = tenantId,
                PublicId = Guid.NewGuid(),
                SubjectOfferingId = offering.Id,
                EmployeeId = assignment.EmployeeId,
                LessonDate = lessonDate,
                Title = title,
                Objectives = Trim(request.LearningObjectives),
                Content = content,
                Resources = Trim(request.Resources),
                State = LessonPlanState.Draft,
                CreatedAt = now,
                CreatedBy = _currentUser.UserId
            };
            await _lessonPlans.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var mapped = await MapLessonPlansAsync([row], cancellationToken);
            return Created(mapped[0], "Lesson plan created as draft.");
        });
    }

    public Task<ApiResponse<LessonPlanDto>> UpdateLessonPlanAsync(
        long id, UpdateLessonPlanDto request, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Task.FromResult(Denied<LessonPlanDto>());
        if (id <= 0 || request == null || !TryDecodeRowVersion(request.RowVersion, out var rowVersion))
            return Task.FromResult(Error<LessonPlanDto>("A valid lesson plan and row version are required."));
        var inputError = ValidateLessonInput(request);
        if (inputError != null) return Task.FromResult(Error<LessonPlanDto>(inputError));

        return ExecuteWriteAsync("update lesson plan", async () =>
        {
            var row = await _lessonPlans.GetQueryable()
                .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id, cancellationToken);
            if (row == null || !await OwnsTeacherAsync(row.EmployeeId, cancellationToken))
                return Error<LessonPlanDto>("Lesson plan not found.", 404);
            if (row.State is not (LessonPlanState.Draft or LessonPlanState.Rejected))
                return Error<LessonPlanDto>("Only draft or rejected lesson plans can be edited.", 409);
            if (!VersionsMatch(row.RowVersion, rowVersion)) return Stale<LessonPlanDto>();

            var offering = await _subjectOfferings.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == row.SubjectOfferingId, cancellationToken);
            if (offering == null) return Error<LessonPlanDto>("Subject offering is unavailable.", 409);
            var lessonDate = DateOnly.FromDateTime(request.StartDate.Date);
            var dateError = await ValidateAcademicDateAsync(offering.AcademicYearId, offering.AcademicTermId, lessonDate, lessonDate, cancellationToken);
            if (dateError != null) return Error<LessonPlanDto>(dateError, 409);

            var title = request.ChapterName.Trim();
            if (await _lessonPlans.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == _currentUser.TenantId && x.Id != row.Id && x.SubjectOfferingId == row.SubjectOfferingId &&
                    x.EmployeeId == row.EmployeeId && x.LessonDate == lessonDate && x.Title == title, cancellationToken))
                return Error<LessonPlanDto>("A lesson plan already uses this instructor, subject, date and title.", 409);

            row.LessonDate = lessonDate;
            row.Title = title;
            row.Objectives = Trim(request.LearningObjectives);
            row.Content = BuildLessonContent(request.Topic, request.Description);
            row.Resources = Trim(request.Resources);
            row.State = LessonPlanState.Draft;
            row.ReviewedAt = null;
            row.ReviewedByUserId = null;
            row.CompletedAt = null;
            Touch(row);
            _lessonPlans.Update(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var mapped = await MapLessonPlansAsync([row], cancellationToken);
            return ApiResponse<LessonPlanDto>.SuccessResponse(mapped[0], "Lesson plan updated.");
        });
    }

    public Task<ApiResponse<LessonPlanDto>> SubmitLessonPlanAsync(
        long id, AcademicRowVersionDto request, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Task.FromResult(Denied<LessonPlanDto>());
        if (id <= 0 || request == null || !TryDecodeRowVersion(request.RowVersion, out var rowVersion))
            return Task.FromResult(Error<LessonPlanDto>("A valid lesson plan and row version are required."));

        return ExecuteWriteAsync("submit lesson plan", async () =>
        {
            var row = await _lessonPlans.GetQueryable()
                .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id, cancellationToken);
            if (row == null || !await OwnsTeacherAsync(row.EmployeeId, cancellationToken))
                return Error<LessonPlanDto>("Lesson plan not found.", 404);
            if (row.State == LessonPlanState.Submitted)
            {
                var current = await MapLessonPlansAsync([row], cancellationToken);
                return ApiResponse<LessonPlanDto>.SuccessResponse(current[0], "Lesson plan is already submitted.");
            }
            if (row.State is not (LessonPlanState.Draft or LessonPlanState.Rejected))
                return Error<LessonPlanDto>("Lesson plan cannot be submitted from its current status.", 409);
            if (!VersionsMatch(row.RowVersion, rowVersion)) return Stale<LessonPlanDto>();

            row.State = LessonPlanState.Submitted;
            row.ReviewedAt = null;
            row.ReviewedByUserId = null;
            Touch(row);
            _lessonPlans.Update(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var mapped = await MapLessonPlansAsync([row], cancellationToken);
            return ApiResponse<LessonPlanDto>.SuccessResponse(mapped[0], "Lesson plan submitted for review.");
        });
    }

    public Task<ApiResponse<LessonPlanDto>> ReviewLessonPlanAsync(
        long id, LessonPlanReviewDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<LessonPlanDto>());
        if (id <= 0 || request == null || (!request.Approve && string.IsNullOrWhiteSpace(request.Remarks)) ||
            TooLong(request.Remarks, 1000) || !TryDecodeRowVersion(request.RowVersion, out var rowVersion))
            return Task.FromResult(Error<LessonPlanDto>("A valid decision, row version and rejection reason are required."));

        return ExecuteWriteAsync("review lesson plan", async () =>
        {
            var row = await _lessonPlans.GetQueryable()
                .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id, cancellationToken);
            if (row == null) return Error<LessonPlanDto>("Lesson plan not found.", 404);
            var target = request.Approve ? LessonPlanState.Approved : LessonPlanState.Rejected;
            if (row.State == target)
            {
                var current = await MapLessonPlansAsync([row], cancellationToken);
                return ApiResponse<LessonPlanDto>.SuccessResponse(current[0], $"Lesson plan is already {target.ToString().ToLowerInvariant()}.");
            }
            if (row.State != LessonPlanState.Submitted)
                return Error<LessonPlanDto>("Only submitted lesson plans can be reviewed.", 409);
            if (!VersionsMatch(row.RowVersion, rowVersion)) return Stale<LessonPlanDto>();

            row.State = target;
            row.ReviewedAt = _clock.GetUtcNow().UtcDateTime;
            row.ReviewedByUserId = _currentUser.UserId;
            Touch(row);
            _lessonPlans.Update(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var mapped = await MapLessonPlansAsync([row], cancellationToken);
            return ApiResponse<LessonPlanDto>.SuccessResponse(mapped[0], request.Approve ? "Lesson plan approved." : "Lesson plan rejected.");
        });
    }

    public Task<ApiResponse<LessonPlanDto>> RecordLessonProgressAsync(
        long id, LessonPlanProgressDto request, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Task.FromResult(Denied<LessonPlanDto>());
        if (id <= 0 || request == null || request.ProgressPercent is < 0 or > 100 ||
            TooLong(request.Notes, 2000) || !TryDecodeRowVersion(request.RowVersion, out var rowVersion))
            return Task.FromResult(Error<LessonPlanDto>("A valid progress percentage and row version are required."));

        return ExecuteWriteAsync("record lesson progress", async () =>
        {
            var row = await _lessonPlans.GetQueryable()
                .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id, cancellationToken);
            if (row == null || !await OwnsTeacherAsync(row.EmployeeId, cancellationToken))
                return Error<LessonPlanDto>("Lesson plan not found.", 404);
            if (row.State == LessonPlanState.Completed && request.ProgressPercent == 100)
            {
                var current = await MapLessonPlansAsync([row], cancellationToken);
                return ApiResponse<LessonPlanDto>.SuccessResponse(current[0], "Lesson plan is already complete.");
            }
            if (row.State is not (LessonPlanState.Approved or LessonPlanState.InProgress))
                return Error<LessonPlanDto>("Only approved or in-progress lesson plans can record progress.", 409);
            if (!VersionsMatch(row.RowVersion, rowVersion)) return Stale<LessonPlanDto>();

            row.State = request.ProgressPercent == 100 ? LessonPlanState.Completed :
                        request.ProgressPercent > 0 ? LessonPlanState.InProgress : LessonPlanState.Approved;
            row.CompletedAt = request.ProgressPercent == 100 ? _clock.GetUtcNow().UtcDateTime : null;
            Touch(row);
            _lessonPlans.Update(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var mapped = await MapLessonPlansAsync([row], cancellationToken);
            return ApiResponse<LessonPlanDto>.SuccessResponse(mapped[0], request.ProgressPercent == 100 ? "Lesson plan completed." : "Lesson progress updated.");
        });
    }

    private async Task<bool> HasInstructorConflictAsync(
        long employeeId, long targetRoutineEntryId, long targetSlotId, DateOnly date, CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        var targetSlot = await _routineSlots.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == targetSlotId, cancellationToken);
        if (targetSlot == null) return true;

        var assignmentIds = await _instructorAssignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.IsActive &&
                        x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date))
            .Select(x => x.Id).Take(200).ToListAsync(cancellationToken);
        var routines = assignmentIds.Count == 0
            ? new List<RoutineEntry>()
            : await _routineEntries.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Id != targetRoutineEntryId && x.InstructorAssignmentId.HasValue &&
                            assignmentIds.Contains(x.InstructorAssignmentId.Value) && x.DayOfWeek == date.DayOfWeek && x.IsActive &&
                            x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date))
                .Take(200).ToListAsync(cancellationToken);
        var otherSlotIds = routines.Select(x => x.RoutineTimeSlotId).Distinct().ToArray();
        var otherSlots = otherSlotIds.Length == 0
            ? new Dictionary<long, RoutineTimeSlot>()
            : await _routineSlots.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && otherSlotIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (routines.Any(x => otherSlots.TryGetValue(x.RoutineTimeSlotId, out var slot) &&
                              slot.StartTime < targetSlot.EndTime && slot.EndTime > targetSlot.StartTime))
            return true;

        var substitutions = await _substitutions.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.SubstituteEmployeeId == employeeId && x.Date == date && !x.IsCancelled)
            .Take(200).ToListAsync(cancellationToken);
        if (substitutions.Count == 0) return false;
        var substitutionContext = await LoadRoutineContextAsync(substitutions.Select(x => x.RoutineEntryId), cancellationToken);
        return substitutions.Any(x => substitutionContext.TryGetValue(x.RoutineEntryId, out var info) &&
                                      info.StartTime < targetSlot.EndTime && info.EndTime > targetSlot.StartTime);
    }

    private async Task<Dictionary<long, RoutineContext>> LoadRoutineContextAsync(
        IEnumerable<long> routineEntryIds, CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        var ids = routineEntryIds.Distinct().Take(500).ToArray();
        if (ids.Length == 0) return new Dictionary<long, RoutineContext>();

        var entries = await _routineEntries.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && ids.Contains(x.Id)).ToListAsync(cancellationToken);
        var offeringIds = entries.Select(x => x.SubjectOfferingId).Distinct().ToArray();
        var slotIds = entries.Select(x => x.RoutineTimeSlotId).Distinct().ToArray();
        var assignmentIds = entries.Where(x => x.InstructorAssignmentId.HasValue).Select(x => x.InstructorAssignmentId!.Value).Distinct().ToArray();
        var offerings = await _subjectOfferings.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && offeringIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var curriculumIds = offerings.Values.Select(x => x.CurriculumSubjectId).Distinct().ToArray();
        var curriculum = await _curriculumSubjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && curriculumIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var subjectIds = curriculum.Values.Select(x => x.SubjectId).Distinct().ToArray();
        var subjects = await _subjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && subjectIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var batchIds = offerings.Values.Select(x => x.AcademicBatchId).Distinct().ToArray();
        var batches = await _academicBatches.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && batchIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var slots = await _routineSlots.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && slotIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var assignments = assignmentIds.Length == 0
            ? new Dictionary<long, InstructorAssignment>()
            : await _instructorAssignments.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && assignmentIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
        var employeeIds = assignments.Values.Select(x => x.EmployeeId).Distinct().ToArray();
        var employees = employeeIds.Length == 0
            ? new Dictionary<long, Employee>()
            : await _employees.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && employeeIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var result = new Dictionary<long, RoutineContext>();
        foreach (var entry in entries)
        {
            if (!offerings.TryGetValue(entry.SubjectOfferingId, out var offering) ||
                !curriculum.TryGetValue(offering.CurriculumSubjectId, out var curriculumSubject) ||
                !subjects.TryGetValue(curriculumSubject.SubjectId, out var subject) ||
                !batches.TryGetValue(offering.AcademicBatchId, out var batch) ||
                !slots.TryGetValue(entry.RoutineTimeSlotId, out var slot))
                continue;

            long originalEmployeeId = 0;
            string originalEmployeeName = string.Empty;
            if (entry.InstructorAssignmentId.HasValue &&
                assignments.TryGetValue(entry.InstructorAssignmentId.Value, out var assignment))
            {
                originalEmployeeId = assignment.EmployeeId;
                if (employees.TryGetValue(assignment.EmployeeId, out var employee))
                    originalEmployeeName = employee.FullName;
            }

            result[entry.Id] = new RoutineContext(
                batch.Id, batch.Name, offering.AcademicYearId, offering.AcademicTermId,
                subject.Id, subject.Name, originalEmployeeId, originalEmployeeName,
                slot.Id, slot.Name, slot.StartTime, slot.EndTime);
        }
        return result;
    }

    private async Task<List<LessonPlanDto>> MapLessonPlansAsync(
        IReadOnlyList<LessonPlan> plans, CancellationToken cancellationToken)
    {
        if (plans.Count == 0) return [];
        var tenantId = _currentUser.TenantId;
        var offeringIds = plans.Select(x => x.SubjectOfferingId).Distinct().ToArray();
        var employeeIds = plans.Select(x => x.EmployeeId).Distinct().ToArray();
        var offerings = await _subjectOfferings.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && offeringIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var curriculumIds = offerings.Values.Select(x => x.CurriculumSubjectId).Distinct().ToArray();
        var curriculum = await _curriculumSubjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && curriculumIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var subjectIds = curriculum.Values.Select(x => x.SubjectId).Distinct().ToArray();
        var subjects = await _subjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && subjectIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var employees = await _employees.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && employeeIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);

        return plans.Select(x =>
        {
            string subjectName = string.Empty;
            if (offerings.TryGetValue(x.SubjectOfferingId, out var offering) &&
                curriculum.TryGetValue(offering.CurriculumSubjectId, out var curriculumSubject) &&
                subjects.TryGetValue(curriculumSubject.SubjectId, out var subject))
                subjectName = subject.Name;
            employees.TryGetValue(x.EmployeeId, out var employee);
            return new LessonPlanDto
            {
                Id = x.Id,
                Reference = x.PublicId,
                SubjectOfferingId = x.SubjectOfferingId,
                SubjectName = subjectName,
                EmployeeId = x.EmployeeId,
                EmployeeName = employee?.FullName ?? string.Empty,
                LessonDate = x.LessonDate,
                Title = x.Title,
                Objectives = x.Objectives,
                Content = x.Content,
                Resources = x.Resources,
                State = x.State,
                ReviewedByUserId = x.ReviewedByUserId,
                ReviewedAt = x.ReviewedAt,
                CompletedAt = x.CompletedAt,
                RowVersion = Convert.ToBase64String(x.RowVersion)
            };
        }).ToList();
    }

    private async Task<string?> ValidateAcademicDateAsync(
        long academicYearId, long? academicTermId, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var year = await _years.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == academicYearId && x.IsActive, cancellationToken);
        if (year == null) return "Academic year not found.";
        if (start < year.StartDate || end > year.EndDate) return "Date range is outside the academic year.";
        if (!academicTermId.HasValue) return null;
        var term = await _terms.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == academicTermId.Value &&
                                      x.AcademicYearId == academicYearId && x.IsActive, cancellationToken);
        if (term == null) return "Academic term not found in this academic year.";
        return start < term.StartDate || end > term.EndDate ? "Date range is outside the academic term." : null;
    }

    private async Task<bool> OwnsTeacherAsync(long employeeId, CancellationToken cancellationToken)
    {
        if (IsManager()) return true;
        var linked = await GetLinkedTeacherIdAsync(cancellationToken);
        return linked.HasValue && linked.Value == employeeId;
    }

    private Task<long?> GetLinkedTeacherIdAsync(CancellationToken cancellationToken) =>
        _employees.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.UserId == _currentUser.UserId &&
                        x.State == EmployeeState.Active && x.CanTeach)
            .Select(x => (long?)x.Id).FirstOrDefaultAsync(cancellationToken);

    private Task<ApiResponse<T>> ExecuteWriteAsync<T>(string operation, Func<Task<ApiResponse<T>>> action) =>
        ExecuteAsync(operation, action);

    private async Task<ApiResponse<T>> ExecuteAsync<T>(string operation, Func<Task<ApiResponse<T>>> action)
    {
        try
        {
            var strategy = _unitOfWork.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var scope = new TransactionScope(
                    TransactionScopeOption.Required,
                    new TransactionOptions { IsolationLevel = IsolationLevel.Serializable },
                    TransactionScopeAsyncFlowOption.Enabled);
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

    private static string? ValidateLessonInput(CreateLessonPlanDto? request)
    {
        if (request == null || request.ClientRequestId == Guid.Empty || request.InstructorAssignmentId <= 0)
            return "Request ID and instructor assignment are required.";
        return ValidateLessonInput(request.ChapterName, request.StartDate, request.EndDate, request.Topic, request.Description, request.LearningObjectives, request.Resources);
    }

    private static string? ValidateLessonInput(UpdateLessonPlanDto? request)
    {
        if (request == null) return "Lesson plan is required.";
        return ValidateLessonInput(request.ChapterName, request.StartDate, request.EndDate, request.Topic, request.Description, request.LearningObjectives, request.Resources);
    }

    private static string? ValidateLessonInput(
        string? chapter, DateTime start, DateTime end, string? topic, string? description, string? objectives, string? resources)
    {
        if (string.IsNullOrWhiteSpace(chapter) || chapter.Trim().Length > 250)
            return "Chapter name is required and cannot exceed 250 characters.";
        if (start == default || end == default || start.Date != end.Date)
            return "The final lesson-plan model stores one lesson date per plan; start and end date must be the same day.";
        if (TooLong(topic, 500) || TooLong(description, 3000) || TooLong(objectives, 4000) || TooLong(resources, 2000))
            return "Lesson-plan content exceeds its allowed length.";
        if ((BuildLessonContent(topic, description)?.Length ?? 0) > 4000)
            return "Combined topic and description cannot exceed 4000 characters.";
        return null;
    }

    private static string? BuildLessonContent(string? topic, string? description)
    {
        var cleanTopic = Trim(topic);
        var cleanDescription = Trim(description);
        if (cleanTopic == null) return cleanDescription;
        if (cleanDescription == null) return $"Topic: {cleanTopic}";
        return $"Topic: {cleanTopic}\n\n{cleanDescription}";
    }

    private static bool SameLesson(LessonPlan row, CreateLessonPlanDto request, string? content) =>
        row.Title == request.ChapterName.Trim() &&
        row.LessonDate == DateOnly.FromDateTime(request.StartDate.Date) &&
        row.Objectives == Trim(request.LearningObjectives) &&
        row.Content == content &&
        row.Resources == Trim(request.Resources);

    private static (bool Success, DateTime From, DateTime To, string? Error) NormalizeRange(DateTime from, DateTime to, int maxDays)
    {
        var start = from.Date;
        var end = to.Date;
        if (from == default || to == default || end < start) return (false, start, end, "Date range is invalid.");
        if ((end - start).TotalDays >= maxDays) return (false, start, end, $"Date range cannot exceed {maxDays} days.");
        return (true, start, end, null);
    }

    private static RoutineSubstitutionDto MapSubstitution(Substitution row, RoutineContext info) => new()
    {
        Id = row.Id,
        Date = row.Date.ToDateTime(TimeOnly.MinValue),
        RoutineEntryId = row.RoutineEntryId,
        AcademicBatchId = info.BatchId,
        BatchName = info.BatchName,
        SubjectId = info.SubjectId,
        SubjectName = info.SubjectName,
        OriginalTeacherId = info.OriginalEmployeeId,
        OriginalTeacherName = info.OriginalEmployeeName,
        SubstituteTeacherId = row.SubstituteEmployeeId,
        SubstituteTeacherName = string.Empty,
        RoutineTimeSlotId = info.SlotId,
        TimeSlotName = info.SlotName,
        StartTime = info.StartTime.ToTimeSpan(),
        EndTime = info.EndTime.ToTimeSpan(),
        Reason = row.Reason,
        IsActive = !row.IsCancelled,
        CancelledAt = row.IsCancelled ? row.UpdatedAt : null,
        CancellationReason = null,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 &&
        (IsManager() || _currentUser.IsInRole("Teacher"));
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

    private static bool VersionsMatch(byte[] left, byte[] right) =>
        left.Length == right.Length && left.Length > 0 && CryptographicOperations.FixedTimeEquals(left, right);

    private static ApiResponse<T> Created<T>(T data, string message) => new()
    {
        Success = true,
        StatusCode = 201,
        Message = message,
        Data = data
    };

    private static ApiResponse<T> Error<T>(string message, int statusCode = 400) => ApiResponse<T>.ErrorResponse(message, statusCode);
    private static ApiResponse<T> Stale<T>() => Error<T>("Academic instruction data changed. Reload and try again.", 409);
    private static ApiResponse<T> Denied<T>() => Error<T>("Academic instruction access is required.", 403);

    private sealed record RoutineContext(
        long BatchId,
        string BatchName,
        long AcademicYearId,
        long? AcademicTermId,
        long SubjectId,
        string SubjectName,
        long OriginalEmployeeId,
        string OriginalEmployeeName,
        long SlotId,
        string SlotName,
        TimeOnly StartTime,
        TimeOnly EndTime);
}
