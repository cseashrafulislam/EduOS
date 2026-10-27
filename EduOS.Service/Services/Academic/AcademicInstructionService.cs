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

    /// <summary>Read-only offering selector for lesson authoring; teacher visibility is limited to assigned offerings.</summary>
    public async Task<ApiResponse<PagedResult<LessonOfferingOptionDto>>> GetLessonOfferingChoicesAsync(
        long academicBatchId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<PagedResult<LessonOfferingOptionDto>>();
        if (academicBatchId <= 0 || page < 1 || pageSize is < 1 or > 100 || search?.Length > 100)
            return Error<PagedResult<LessonOfferingOptionDto>>("Invalid lesson offering filters or page.");
        var tenant = _currentUser.TenantId;
        if (!await _academicBatches.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == academicBatchId && x.IsActive && !x.IsDeleted, ct))
            return Error<PagedResult<LessonOfferingOptionDto>>("Active academic batch not found.", 404);

        var offerings = _subjectOfferings.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AcademicBatchId == academicBatchId && x.IsActive && !x.IsDeleted);
        if (!IsManager())
        {
            var teacherId = await GetLinkedTeacherIdAsync(ct);
            if (!teacherId.HasValue) return Denied<PagedResult<LessonOfferingOptionDto>>();
            var assignedIds = _instructorAssignments.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.EmployeeId == teacherId.Value && x.IsActive && !x.IsDeleted)
                .Select(x => x.SubjectOfferingId);
            offerings = offerings.Where(x => assignedIds.Contains(x.Id));
        }
        var query = from offering in offerings
                    join curriculumSubject in _curriculumSubjects.GetQueryable().AsNoTracking()
                        on offering.CurriculumSubjectId equals curriculumSubject.Id
                    join subject in _subjects.GetQueryable().AsNoTracking()
                        on curriculumSubject.SubjectId equals subject.Id
                    where curriculumSubject.TenantId == tenant && !curriculumSubject.IsDeleted &&
                          subject.TenantId == tenant && !subject.IsDeleted
                    select new LessonOfferingOptionDto
                    {
                        Id = offering.Id, Reference = offering.PublicId,
                        SubjectName = subject.Name, SubjectCode = subject.Code,
                        Code = offering.Code, IsActive = offering.IsActive
                    };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.SubjectName.StartsWith(term) ||
                x.SubjectCode.StartsWith(term) || x.Code.StartsWith(term));
        }
        var total = await query.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue)
            return Error<PagedResult<LessonOfferingOptionDto>>("Page exceeds supported range.");
        var rows = await query.OrderBy(x => x.SubjectName).ThenBy(x => x.Id)
            .Skip((int)offset).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<LessonOfferingOptionDto>>.SuccessResponse(
            new PagedResult<LessonOfferingOptionDto>
            { Items = rows, TotalCount = total, Page = page, PageSize = pageSize });
    }

    // Legacy bounded-list API remains compatible; the workspace uses the paged register.
    public async Task<ApiResponse<IReadOnlyList<SubstitutionDto>>> GetSubstitutionsAsync(
        DateOnly fromDate, DateOnly toDate, long? academicBatchId, CancellationToken ct = default)
    {
        var result = await GetSubstitutionsPageAsync(fromDate, toDate, academicBatchId, 1, 500, ct);
        return result.Success
            ? ApiResponse<IReadOnlyList<SubstitutionDto>>.SuccessResponse(result.Data!.Items)
            : Error<IReadOnlyList<SubstitutionDto>>(result.Message, result.StatusCode);
    }

    public async Task<ApiResponse<PagedResult<SubstitutionDto>>> GetSubstitutionsPageAsync(
        DateOnly fromDate, DateOnly toDate, long? academicBatchId, int page, int pageSize,
        CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<PagedResult<SubstitutionDto>>();
        if (fromDate == default || toDate == default || toDate < fromDate ||
            toDate.DayNumber - fromDate.DayNumber > 92 || academicBatchId is <= 0 ||
            page < 1 || pageSize is < 1 or > 500)
            return Error<PagedResult<SubstitutionDto>>("Invalid substitution date range, academic batch or page.");
        var tenant = _currentUser.TenantId;
        var q = _substitutions.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.Date >= fromDate && x.Date <= toDate && !x.IsDeleted);
        if (academicBatchId.HasValue)
        {
            var ids = from routine in _routineEntries.GetQueryable().AsNoTracking()
                join offer in _subjectOfferings.GetQueryable().AsNoTracking()
                    on routine.SubjectOfferingId equals offer.Id
                where routine.TenantId == tenant && offer.TenantId == tenant &&
                    offer.AcademicBatchId == academicBatchId.Value
                select routine.Id;
            q = q.Where(x => ids.Contains(x.RoutineEntryId));
        }
        var teacherId = !IsManager() ? await GetLinkedTeacherIdAsync(ct) : null;
        if (!IsManager() && !teacherId.HasValue) return Denied<PagedResult<SubstitutionDto>>();
        if (!IsManager())
        {
            var assignedRoutineIds = from routine in _routineEntries.GetQueryable().AsNoTracking()
                join assignment in _instructorAssignments.GetQueryable().AsNoTracking()
                    on routine.InstructorAssignmentId equals (long?)assignment.Id
                where routine.TenantId == tenant && assignment.TenantId == tenant &&
                    assignment.EmployeeId == teacherId!.Value
                select routine.Id;
            q = q.Where(x => x.SubstituteEmployeeId == teacherId!.Value ||
                assignedRoutineIds.Contains(x.RoutineEntryId));
        }
        // Scope both the total count and the page before materialization.
        var total = await q.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue)
            return Error<PagedResult<SubstitutionDto>>("Page exceeds supported range.");
        var rows = await q.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Skip((int)offset).Take(pageSize).ToListAsync(ct);
        var context = await LoadRoutineContextAsync(rows.Select(x => x.RoutineEntryId), ct);
        var ids2 = rows.Select(x => x.SubstituteEmployeeId).Distinct().ToArray();
        var names = await _employees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && ids2.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        // Never silently drop historical rows after Count/Skip/Take: a missing routine, slot,
        // offering or subject would otherwise produce short pages and incorrect totals.
        // Preserve the database's newest-first page order for stable navigation and exports.
        IReadOnlyList<SubstitutionDto> result = rows.Select(x =>
        {
            var dto = MapSubstitution(x, names.GetValueOrDefault(x.SubstituteEmployeeId));
            if (!context.TryGetValue(x.RoutineEntryId, out var info))
            {
                dto.ContextUnavailable = true;
                return dto;
            }
            dto.BatchName = info.BatchName;
            dto.SubjectName = info.SubjectName;
            dto.TimeSlotName = info.SlotName;
            dto.OriginalTeacherName = info.OriginalEmployeeName;
            return dto;
        }).ToList();
        return ApiResponse<PagedResult<SubstitutionDto>>.SuccessResponse(new PagedResult<SubstitutionDto>
        {
            Items = result.ToList(), TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public Task<ApiResponse<SubstitutionDto>> CreateSubstitutionAsync(
        CreateSubstitutionRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<SubstitutionDto>());
        if (request == null || request.ClientRequestId == Guid.Empty || request.RoutineEntryId <= 0 ||
            request.Date == default || request.SubstituteEmployeeReference == Guid.Empty ||
            TooLong(request.Reason, 500))
            return Task.FromResult(Error<SubstitutionDto>("Valid substitution, date and instructor are required."));
        return ExecuteWriteAsync("create substitution", async () =>
        {
            var tenant = _currentUser.TenantId;
            var employee = await _employees.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.SubstituteEmployeeReference &&
                x.CanTeach && x.State == EmployeeState.Active && !x.IsDeleted, ct);
            if (employee == null) return Error<SubstitutionDto>("Active substitute instructor not found.", 404);
            var replay = await _substitutions.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, ct);
            if (replay != null)
            {
                if (replay.RoutineEntryId != request.RoutineEntryId || replay.Date != request.Date ||
                    replay.SubstituteEmployeeId != employee.Id || replay.Reason != Trim(request.Reason))
                    return Error<SubstitutionDto>("Request ID was used for different substitution details.", 409);
                return ApiResponse<SubstitutionDto>.SuccessResponse(
                    MapSubstitution(replay, employee.FullName), "Substitution already exists.");
            }
            var routine = await _routineEntries.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.RoutineEntryId && x.IsActive && !x.IsDeleted, ct);
            if (routine == null) return Error<SubstitutionDto>("Active routine entry not found.", 404);
            if (routine.DayOfWeek != request.Date.DayOfWeek || routine.EffectiveFrom > request.Date ||
                routine.EffectiveTo.HasValue && routine.EffectiveTo.Value < request.Date)
                return Error<SubstitutionDto>("Substitution date is outside the routine schedule.", 409);
            var info = await LoadRoutineContextAsync([routine.Id], ct);
            if (!info.TryGetValue(routine.Id, out var context) || context.OriginalEmployeeId <= 0)
                return Error<SubstitutionDto>("Routine instructor is not assigned.", 409);
            var dateError = await ValidateAcademicDateAsync(context.AcademicYearId, context.AcademicTermId,
                request.Date, request.Date, ct);
            if (dateError != null) return Error<SubstitutionDto>(dateError, 409);
            if (context.OriginalEmployeeId == employee.Id)
                return Error<SubstitutionDto>("Substitute cannot be the original instructor.", 409);
            if (await _substitutions.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.RoutineEntryId == routine.Id &&
                x.Date == request.Date && !x.IsCancelled && !x.IsDeleted, ct))
                return Error<SubstitutionDto>("Active substitution already exists for this routine and date.", 409);
            if (await HasInstructorConflictAsync(employee.Id, routine.Id, routine.RoutineTimeSlotId, request.Date, ct))
                return Error<SubstitutionDto>("Substitute instructor is already scheduled for this time.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            var row = new Substitution
            {
                TenantId = tenant, ClientRequestId = request.ClientRequestId,
                RoutineEntryId = routine.Id, Date = request.Date,
                SubstituteEmployeeId = employee.Id, Reason = Trim(request.Reason),
                CreatedAt = now, CreatedBy = _currentUser.UserId
            };
            await _substitutions.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return Created(MapSubstitution(row, employee.FullName), "Substitution created.");
        });
    }

    public Task<ApiResponse<SubstitutionDto>> CancelSubstitutionAsync(
        long id, CancelSubstitutionRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<SubstitutionDto>());
        if (id <= 0 || request == null || !TryDecodeRowVersion(request.RowVersion, out var version))
            return Task.FromResult(Error<SubstitutionDto>("Substitution and row version are required."));
        return ExecuteWriteAsync("cancel substitution", async () =>
        {
            var row = await _substitutions.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _currentUser.TenantId && x.Id == id && !x.IsDeleted, ct);
            if (row == null) return Error<SubstitutionDto>("Substitution not found.", 404);
            if (!VersionsMatch(row.RowVersion, version)) return Stale<SubstitutionDto>();
            var instructor = await _employees.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == _currentUser.TenantId && x.Id == row.SubstituteEmployeeId)
                .Select(x => x.FullName).FirstOrDefaultAsync(ct);
            if (row.IsCancelled)
                return ApiResponse<SubstitutionDto>.SuccessResponse(MapSubstitution(row, instructor), "Substitution is already cancelled.");
            row.IsCancelled = true;
            row.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            row.UpdatedBy = _currentUser.UserId;
            _substitutions.Update(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return ApiResponse<SubstitutionDto>.SuccessResponse(MapSubstitution(row, instructor), "Substitution cancelled.");
        });
    }

    public async Task<ApiResponse<PagedResult<LessonPlanDto>>> GetLessonPlansAsync(
        long? academicBatchId, DateOnly? fromDate, DateOnly? toDate, int page, int pageSize,
        CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<PagedResult<LessonPlanDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || academicBatchId is <= 0 ||
            fromDate.HasValue != toDate.HasValue ||
            fromDate.HasValue && (toDate < fromDate ||
                toDate.Value.DayNumber - fromDate.Value.DayNumber > 366))
            return Error<PagedResult<LessonPlanDto>>("Invalid academic batch, page or date range.");
        var tenant = _currentUser.TenantId;
        var q = _lessonPlans.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted);
        if (academicBatchId.HasValue)
        {
            var ids = _subjectOfferings.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AcademicBatchId == academicBatchId.Value)
                .Select(x => x.Id);
            q = q.Where(x => ids.Contains(x.SubjectOfferingId));
        }
        if (fromDate.HasValue) q = q.Where(x => x.LessonDate >= fromDate.Value && x.LessonDate <= toDate!.Value);
        if (!IsManager())
        {
            var teacherId = await GetLinkedTeacherIdAsync(ct);
            if (!teacherId.HasValue) return Denied<PagedResult<LessonPlanDto>>();
            q = q.Where(x => x.EmployeeId == teacherId.Value);
        }
        var total = await q.CountAsync(ct);
        var skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue) return Error<PagedResult<LessonPlanDto>>("Requested page is too large.");
        var list = await q.OrderByDescending(x => x.LessonDate).ThenByDescending(x => x.Id)
            .Skip((int)skip).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<LessonPlanDto>>.SuccessResponse(new PagedResult<LessonPlanDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = await MapLessonPlansAsync(list, ct)
        });
    }

    public Task<ApiResponse<LessonPlanDto>> CreateLessonPlanAsync(
        SaveLessonPlanRequestDto request, CancellationToken ct = default)
    {
        if (!CanRead()) return Task.FromResult(Denied<LessonPlanDto>());
        var err = ValidateLessonInput(request);
        if (err != null || request!.ClientRequestId == Guid.Empty || request.SubjectOfferingReference == Guid.Empty)
            return Task.FromResult(Error<LessonPlanDto>(err ?? "Valid request and offering references are required."));
        return ExecuteWriteAsync("create lesson plan", async () =>
        {
            var tenant = _currentUser.TenantId;
            var offering = await _subjectOfferings.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.SubjectOfferingReference &&
                x.IsActive && !x.IsDeleted, ct);
            if (offering == null) return Error<LessonPlanDto>("Subject offering not found.", 404);
            var dateError = await ValidateAcademicDateAsync(offering.AcademicYearId, offering.AcademicTermId,
                request.LessonDate, request.LessonDate, ct);
            if (dateError != null) return Error<LessonPlanDto>(dateError, 409);
            var assignments = await _instructorAssignments.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && x.SubjectOfferingId == offering.Id && x.IsActive &&
                    x.EffectiveFrom <= request.LessonDate &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= request.LessonDate))
                .OrderByDescending(x => x.IsPrimary).Take(100).ToListAsync(ct);
            var teacherId = await GetLinkedTeacherIdAsync(ct);
            var assignment = IsManager() ? assignments.FirstOrDefault(x => x.IsPrimary) :
                assignments.FirstOrDefault(x => x.EmployeeId == teacherId);
            if (assignment == null)
                return Error<LessonPlanDto>("No eligible instructor assigned for this date.", 409);
            var existing = await _lessonPlans.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.SubjectOfferingId == offering.Id &&
                x.EmployeeId == assignment.EmployeeId && x.LessonDate == request.LessonDate &&
                x.Title == request.Title.Trim() && !x.IsDeleted, ct);
            if (existing != null)
            {
                if (SameLesson(existing, request))
                {
                    var previous = await MapLessonPlansAsync([existing], ct);
                    return ApiResponse<LessonPlanDto>.SuccessResponse(previous[0], "Lesson plan already exists.");
                }
                return Error<LessonPlanDto>("Lesson plan already exists with different content.", 409);
            }
            var now = _clock.GetUtcNow().UtcDateTime;
            var row = new LessonPlan
            {
                TenantId = tenant, PublicId = Guid.NewGuid(), SubjectOfferingId = offering.Id,
                EmployeeId = assignment.EmployeeId, LessonDate = request.LessonDate,
                Title = request.Title.Trim(), Objectives = Trim(request.Objectives),
                Content = Trim(request.Content), Resources = Trim(request.Resources),
                State = LessonPlanState.Draft, CreatedAt = now, CreatedBy = _currentUser.UserId
            };
            await _lessonPlans.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(ct);
            var result = await MapLessonPlansAsync([row], ct);
            return Created(result[0], "Lesson plan created.");
        });
    }

    public Task<ApiResponse<LessonPlanDto>> UpdateLessonPlanAsync(long id,
        SaveLessonPlanRequestDto request, CancellationToken ct = default)
    {
        if (!CanRead()) return Task.FromResult(Denied<LessonPlanDto>());
        var error = ValidateLessonInput(request);
        if (id <= 0 || error != null || request == null ||
            !TryDecodeRowVersion(request.RowVersion, out var version))
            return Task.FromResult(Error<LessonPlanDto>(error ?? "Lesson plan and row version are required."));
        return ExecuteWriteAsync("update lesson plan", async () =>
        {
            var row = await _lessonPlans.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _currentUser.TenantId && x.Id == id && !x.IsDeleted, ct);
            if (row == null || !await OwnsTeacherAsync(row.EmployeeId, ct))
                return Error<LessonPlanDto>("Lesson plan not found.", 404);
            if (!VersionsMatch(row.RowVersion, version)) return Stale<LessonPlanDto>();
            if (row.State is not (LessonPlanState.Draft or LessonPlanState.Rejected))
                return Error<LessonPlanDto>("Only draft or rejected lesson plans may be edited.", 409);
            var offering = await _subjectOfferings.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == _currentUser.TenantId && x.Id == row.SubjectOfferingId &&
                x.PublicId == request.SubjectOfferingReference && x.IsActive, ct);
            if (offering == null)
                return Error<LessonPlanDto>("Changing a lesson plan's subject offering is not supported.", 409);
            var dateError = await ValidateAcademicDateAsync(offering.AcademicYearId, offering.AcademicTermId,
                request.LessonDate, request.LessonDate, ct);
            if (dateError != null) return Error<LessonPlanDto>(dateError, 409);
            if (await _lessonPlans.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == _currentUser.TenantId && x.Id != id &&
                x.SubjectOfferingId == row.SubjectOfferingId && x.EmployeeId == row.EmployeeId &&
                x.LessonDate == request.LessonDate && x.Title == request.Title.Trim() && !x.IsDeleted, ct))
                return Error<LessonPlanDto>("Another lesson plan uses the same title and date.", 409);
            row.LessonDate = request.LessonDate; row.Title = request.Title.Trim();
            row.Objectives = Trim(request.Objectives); row.Content = Trim(request.Content);
            row.Resources = Trim(request.Resources); row.State = LessonPlanState.Draft;
            row.ReviewedAt = null; row.ReviewedByUserId = null; row.CompletedAt = null;
            Touch(row); _lessonPlans.Update(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return ApiResponse<LessonPlanDto>.SuccessResponse(
                (await MapLessonPlansAsync([row], ct))[0], "Lesson plan updated.");
        });
    }

    public Task<ApiResponse<LessonPlanDto>> SubmitLessonPlanAsync(
        long id, string rowVersion, CancellationToken ct = default)
    {
        if (!CanRead()) return Task.FromResult(Denied<LessonPlanDto>());
        if (id <= 0 || !TryDecodeRowVersion(rowVersion, out var expected))
            return Task.FromResult(Error<LessonPlanDto>("Lesson plan and row version are required."));
        return ExecuteWriteAsync("submit lesson plan", async () =>
        {
            var row = await _lessonPlans.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _currentUser.TenantId && x.Id == id && !x.IsDeleted, ct);
            if (row == null || !await OwnsTeacherAsync(row.EmployeeId, ct))
                return Error<LessonPlanDto>("Lesson plan not found.", 404);
            if (!VersionsMatch(row.RowVersion, expected)) return Stale<LessonPlanDto>();
            if (row.State is not (LessonPlanState.Draft or LessonPlanState.Rejected))
                return Error<LessonPlanDto>("Only draft or rejected plans can be submitted.", 409);
            row.State = LessonPlanState.Submitted; row.ReviewedAt = null; row.ReviewedByUserId = null;
            Touch(row); _lessonPlans.Update(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return ApiResponse<LessonPlanDto>.SuccessResponse(
                (await MapLessonPlansAsync([row], ct))[0], "Lesson plan submitted.");
        });
    }

    public Task<ApiResponse<LessonPlanDto>> ReviewLessonPlanAsync(
        long id, ReviewLessonPlanRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<LessonPlanDto>());
        if (id <= 0 || request == null || (!request.Approve && string.IsNullOrWhiteSpace(request.Note)) ||
            TooLong(request.Note, 1000) || !TryDecodeRowVersion(request.RowVersion, out var version))
            return Task.FromResult(Error<LessonPlanDto>("A decision, row version and rejection note are required."));
        return ExecuteWriteAsync("review lesson plan", async () =>
        {
            var row = await _lessonPlans.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _currentUser.TenantId && x.Id == id && !x.IsDeleted, ct);
            if (row == null) return Error<LessonPlanDto>("Lesson plan not found.", 404);
            if (!VersionsMatch(row.RowVersion, version)) return Stale<LessonPlanDto>();
            if (row.State != LessonPlanState.Submitted)
                return Error<LessonPlanDto>("Only submitted lesson plans can be reviewed.", 409);
            row.State = request.Approve ? LessonPlanState.Approved : LessonPlanState.Rejected;
            row.ReviewedAt = _clock.GetUtcNow().UtcDateTime;
            row.ReviewedByUserId = _currentUser.UserId;
            Touch(row); _lessonPlans.Update(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return ApiResponse<LessonPlanDto>.SuccessResponse(
                (await MapLessonPlansAsync([row], ct))[0], request.Approve ? "Lesson plan approved." : "Lesson plan rejected.");
        });
    }

    public Task<ApiResponse<LessonPlanDto>> StartLessonPlanAsync(
        long id, AdvanceLessonPlanRequestDto request, CancellationToken ct = default) =>
        AdvanceLessonPlanAsync(id, request, LessonPlanState.Approved, LessonPlanState.InProgress, ct);

    public Task<ApiResponse<LessonPlanDto>> CompleteLessonPlanAsync(
        long id, AdvanceLessonPlanRequestDto request, CancellationToken ct = default) =>
        AdvanceLessonPlanAsync(id, request, LessonPlanState.InProgress, LessonPlanState.Completed, ct);

    private Task<ApiResponse<LessonPlanDto>> AdvanceLessonPlanAsync(
        long id, AdvanceLessonPlanRequestDto request, LessonPlanState requiredState,
        LessonPlanState targetState, CancellationToken ct)
    {
        // Administrative review does not authorize an administrator to claim a lesson was taught.
        if (!CanRead() || !_currentUser.IsInRole("Teacher"))
            return Task.FromResult(Denied<LessonPlanDto>());
        if (id <= 0 || request == null || !TryDecodeRowVersion(request.RowVersion, out var expected))
            return Task.FromResult(Error<LessonPlanDto>("Lesson plan and row version are required."));

        return ExecuteWriteAsync("advance lesson delivery", async () =>
        {
            var tenant = _currentUser.TenantId;
            var teacherId = await GetLinkedTeacherIdAsync(ct);
            var row = await _lessonPlans.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
            if (row == null || !teacherId.HasValue || row.EmployeeId != teacherId.Value)
                return Error<LessonPlanDto>("Lesson plan not found.", 404);

            // Repeated delivery of an already-applied transition is read-only and retry safe.
            if (row.State == targetState)
                return ApiResponse<LessonPlanDto>.SuccessResponse(
                    (await MapLessonPlansAsync([row], ct))[0], "Lesson transition already applied.");
            if (!VersionsMatch(row.RowVersion, expected)) return Stale<LessonPlanDto>();
            if (row.State != requiredState)
                return Error<LessonPlanDto>("Lesson delivery requires the preceding approved state.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            if (row.LessonDate > DateOnly.FromDateTime(now))
                return Error<LessonPlanDto>("Future lessons cannot be started or completed.", 409);

            var offering = await _subjectOfferings.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == row.SubjectOfferingId && !x.IsDeleted, ct);
            if (offering == null) return Error<LessonPlanDto>("Subject offering not found.", 404);
            var dateError = await ValidateAcademicDateAsync(offering.AcademicYearId, offering.AcademicTermId,
                row.LessonDate, row.LessonDate, ct);
            if (dateError != null) return Error<LessonPlanDto>(dateError, 409);
            var assigned = await _instructorAssignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.SubjectOfferingId == row.SubjectOfferingId &&
                x.EmployeeId == teacherId.Value && x.IsActive && !x.IsDeleted &&
                x.EffectiveFrom <= row.LessonDate &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= row.LessonDate), ct);
            if (!assigned) return Error<LessonPlanDto>("Instructor is not eligible for this lesson date.", 409);

            row.State = targetState;
            if (targetState == LessonPlanState.Completed) row.CompletedAt = now;
            Touch(row);
            _lessonPlans.Update(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return ApiResponse<LessonPlanDto>.SuccessResponse(
                (await MapLessonPlansAsync([row], ct))[0],
                targetState == LessonPlanState.Completed ? "Lesson completed." : "Lesson started.");
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

    private async Task<ApiResponse<T>> ExecuteWriteAsync<T>(string operation, Func<Task<ApiResponse<T>>> action)
    {
        try { return await _unitOfWork.ExecuteInTransactionAsync(_ => action()); }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent academic instruction update {Operation} tenant {TenantId}", operation, _currentUser.TenantId);
            return Stale<T>();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Academic instruction update conflict {Operation} tenant {TenantId}", operation, _currentUser.TenantId);
            return Error<T>("Academic instruction conflicts with another update.", 409);
        }
    }

    private void Touch(LessonPlan row)
    {
        row.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        row.UpdatedBy = _currentUser.UserId;
    }

    private static string? ValidateLessonInput(SaveLessonPlanRequestDto? request)
    {
        if (request == null || request.LessonDate == default ||
            string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 250 ||
            TooLong(request.Objectives, 4000) || TooLong(request.Content, 4000) ||
            TooLong(request.Resources, 2000))
            return "Lesson date, title or content is invalid.";
        return null;
    }

    private static bool SameLesson(LessonPlan row, SaveLessonPlanRequestDto request) =>
        row.LessonDate == request.LessonDate && row.Title == request.Title.Trim() &&
        row.Objectives == Trim(request.Objectives) && row.Content == Trim(request.Content) &&
        row.Resources == Trim(request.Resources);



    private static SubstitutionDto MapSubstitution(Substitution row, string? employeeName) => new()
    {
        Id = row.Id, RoutineEntryId = row.RoutineEntryId, Date = row.Date,
        SubstituteEmployeeId = row.SubstituteEmployeeId,
        SubstituteEmployeeName = employeeName ?? string.Empty,
        Reason = row.Reason, IsCancelled = row.IsCancelled,
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
