using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Transactions;

namespace EduOS.Service.Services.Students;

public sealed class StudentPromotionService : IStudentPromotionService
{
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<StudentPromotionRecord> _records;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicLevel> _levels;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AcademicCurriculum> _curricula;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<StudentPromotionService> _logger;

    public StudentPromotionService(IGenericRepository<Student> students,
        IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<StudentPromotionRecord> records,
        IGenericRepository<AcademicYear> years, IGenericRepository<AcademicLevel> levels,
        IGenericRepository<AcademicBatch> batches, IGenericRepository<AcademicCurriculum> curricula,
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, TimeProvider clock,
        ILogger<StudentPromotionService> logger)
    {
        _students = students; _enrollments = enrollments; _records = records;
        _years = years; _levels = levels; _batches = batches; _curricula = curricula;
        _uow = unitOfWork; _user = currentUser; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<StudentPromotionOptionsDto>> GetOptionsAsync(
        Guid studentReference, CancellationToken ct = default)
    {
        // Campus-specific principal access is not exposed by ICurrentUserService.
        // Until a canonical campus permission contract exists, only tenant administrators can use this manager UI.
        if (!_user.IsAuthenticated || !_user.IsTenantAdmin || _user.TenantId <= 0)
            return Denied<StudentPromotionOptionsDto>();
        if (studentReference == Guid.Empty)
            return ApiResponse<StudentPromotionOptionsDto>.ErrorResponse("Student reference is required.");
        var tenant = _user.TenantId;
        try
        {
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == studentReference && !x.IsDeleted, ct);
            if (student == null)
                return ApiResponse<StudentPromotionOptionsDto>.ErrorResponse("Student not found.", 404);
            if (student.StatusCode != "Active")
                return ApiResponse<StudentPromotionOptionsDto>.ErrorResponse("Student is not active.", 409);
            var source = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.StudentId == student.Id && x.IsCurrent &&
                x.State == EnrollmentState.Active && !x.IsDeleted, ct);
            if (source == null)
                return ApiResponse<StudentPromotionOptionsDto>.ErrorResponse("Current enrollment not found.", 409);
            var sourceYear = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == source.AcademicYearId, ct);
            var sourceLevel = await _levels.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == source.AcademicLevelId, ct);
            if (sourceYear == null || sourceLevel == null)
                return ApiResponse<StudentPromotionOptionsDto>.ErrorResponse("Current academic placement is unavailable.", 409);
            var years = await _years.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.IsActive && !x.IsDeleted && x.StartDate > sourceYear.StartDate)
                .OrderBy(x => x.StartDate).Take(10).ToListAsync(ct);
            var yearIds = years.Select(x => x.Id).ToArray();
            var levels = await _levels.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.IsActive && !x.IsDeleted && x.AcademicProgramId == sourceLevel.AcademicProgramId &&
                (x.Id == sourceLevel.Id || (sourceLevel.IsPromotable && x.LevelNo > sourceLevel.LevelNo)))
                .ToListAsync(ct);
            var levelIds = levels.Select(x => x.Id).ToArray();
            var batches = await _batches.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.IsActive && !x.IsDeleted && x.CampusId == source.CampusId &&
                x.AcademicProgramId == source.AcademicProgramId &&
                yearIds.Contains(x.AcademicYearId) && levelIds.Contains(x.AcademicLevelId) && x.Capacity > 0)
                .OrderBy(x => x.AcademicYearId).ThenBy(x => x.Name).Take(200).ToListAsync(ct);
            var yearNames = years.ToDictionary(x => x.Id, x => x.Name);
            var levelNames = levels.ToDictionary(x => x.Id, x => x.Name);
            var targets = batches.Select(x => new StudentPromotionTargetOptionDto
            {
                AcademicYearId = x.AcademicYearId, AcademicLevelId = x.AcademicLevelId,
                AcademicBatchId = x.Id, AcademicTrackId = x.AcademicTrackId,
                Decision = x.AcademicLevelId == sourceLevel.Id
                    ? StudentProgressionDecisionType.Repeated : StudentProgressionDecisionType.Promoted,
                Name = yearNames[x.AcademicYearId] + " / " + levelNames[x.AcademicLevelId] + " / " + x.Name
            }).ToList();
            return ApiResponse<StudentPromotionOptionsDto>.SuccessResponse(new StudentPromotionOptionsDto
            {
                StudentReference = student.PublicId, StudentCode = student.StudentCode,
                SourceEnrollmentId = source.Id,
                StudentRowVersion = Convert.ToBase64String(student.RowVersion),
                SourceEnrollmentRowVersion = Convert.ToBase64String(source.RowVersion),
                Targets = targets
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student promotion options failed for tenant {TenantId}", tenant);
            return ApiResponse<StudentPromotionOptionsDto>.ErrorResponse("Promotion options could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<StudentPromotionResultDto>> PromoteAsync(Guid studentReference,
        PromoteStudentWorkflowRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<StudentPromotionResultDto>();
        if (studentReference == Guid.Empty || !IsValid(request) ||
            !TryVersion(request.StudentRowVersion, out var expectedStudent) ||
            !TryVersion(request.SourceEnrollmentRowVersion, out var expectedSource) ||
            !Enum.TryParse<StudentProgressionDecisionType>(request.Decision.ToString(), out var decision))
            return Error("Valid progression request and row versions are required.");
        var tenant = _user.TenantId;
        var roll = request.TargetRoll.Trim();
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
            var student = await _students.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == studentReference, ct);
            if (student == null) return Error("Student not found.", 404);
            var replay = await _records.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.ClientRequestId == request.ClientRequestId, ct);
            if (replay != null)
            {
                if (replay.StudentId != student.Id) return Error("Client request ID has been used for another student.", 409);
                var replayResult = await ExistingResultAsync(replay, student, ct);
                if (!replayResult.Success || replayResult.Data == null ||
                    replayResult.Data.FromEnrollmentId != request.SourceEnrollmentId ||
                    !string.Equals(replay.Note?.Trim() ?? string.Empty, request.Note?.Trim() ?? string.Empty, StringComparison.Ordinal) ||
                    replayResult.Data.AcademicYearId != request.TargetAcademicYearId ||
                    replayResult.Data.AcademicLevelId != request.TargetAcademicLevelId ||
                    replayResult.Data.AcademicBatchId != request.TargetAcademicBatchId ||
                    replayResult.Data.AcademicTrackId != request.TargetAcademicTrackId && request.TargetAcademicTrackId.HasValue ||
                    !string.Equals(replayResult.Data.Roll, roll, StringComparison.OrdinalIgnoreCase) ||
                    replayResult.Data.Decision != request.Decision)
                    return Error("Client request ID is already used for different progression details.", 409);
                return replayResult;
            }
            if (student.StatusCode != "Active")
                return Error("Only an active student can progress.", 409);
            if (!VersionsMatch(student.RowVersion, expectedStudent))
                return Error("Student changed. Reload and retry.", 409);
            var source = await _enrollments.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == request.SourceEnrollmentId && x.StudentId == student.Id, ct);
            if (source == null) return Error("Source enrollment not found.", 404);
            if (!source.IsCurrent || source.State != EnrollmentState.Active)
                return Error("Source enrollment is no longer current.", 409);
            if (!VersionsMatch(source.RowVersion, expectedSource))
                return Error("Source enrollment changed. Reload and retry.", 409);
            if (await _records.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.FromEnrollmentId == source.Id, ct))
                return Error("Source enrollment has already been progressed.", 409);

            var sourceYear = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == source.AcademicYearId, ct);
            var targetYear = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.TargetAcademicYearId && x.IsActive, ct);
            var sourceLevel = await _levels.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == source.AcademicLevelId, ct);
            var targetLevel = await _levels.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.TargetAcademicLevelId && x.IsActive, ct);
            var batch = await _batches.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == request.TargetAcademicBatchId && x.IsActive &&
                x.AcademicYearId == request.TargetAcademicYearId && x.AcademicLevelId == request.TargetAcademicLevelId, ct);
            if (sourceYear == null || targetYear == null || sourceLevel == null || targetLevel == null || batch == null)
                return Error("Academic placement is unavailable.", 409);
            if (targetYear.Id == sourceYear.Id || targetYear.StartDate <= sourceYear.StartDate)
                return Error("Progression must target a later academic year.", 409);
            if (sourceLevel.AcademicProgramId != targetLevel.AcademicProgramId ||
                batch.AcademicProgramId != targetLevel.AcademicProgramId)
                return Error("Cross-program progression requires a separate transfer workflow.", 409);
            if (decision == StudentProgressionDecisionType.Promoted &&
                (targetLevel.LevelNo <= sourceLevel.LevelNo || !sourceLevel.IsPromotable))
                return Error("Promotion must advance from a promotable level.", 409);
            if (decision == StudentProgressionDecisionType.Repeated && targetLevel.Id != sourceLevel.Id)
                return Error("Repeating students must remain at the same academic level.", 409);
            if (request.TargetAcademicTrackId.HasValue && batch.AcademicTrackId != request.TargetAcademicTrackId.Value)
                return Error("Target track does not match the selected batch.", 409);
            if (batch.CampusId != source.CampusId)
                return Error("Campus changes require a transfer workflow.", 409);
            // A progression can be scheduled for the next academic cycle, but the
            // new enrollment must never start outside its own year/batch dates.
            var effectiveDate = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
            var targetDate = effectiveDate < targetYear.StartDate ? targetYear.StartDate : effectiveDate;
            if (batch.StartDate.HasValue && targetDate < batch.StartDate.Value)
                targetDate = batch.StartDate.Value;
            if (targetDate > targetYear.EndDate ||
                batch.EndDate.HasValue && targetDate > batch.EndDate.Value)
                return Error("Target academic year or batch is outside its enrollment period.", 409);
            var curricula = await _curricula.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.IsActive && x.IsCurrent && !x.IsDeleted && x.AcademicProgramId == batch.AcademicProgramId &&
                x.AcademicTrackId == batch.AcademicTrackId && x.MediumId == batch.MediumId &&
                x.EffectiveFrom <= targetDate && (!x.EffectiveTo.HasValue || x.EffectiveTo >= targetDate))
                .Take(2).Select(x => x.Id).ToArrayAsync(ct);
            if (curricula.Length != 1)
                return Error("Target batch must have exactly one matching active curriculum.", 409);

            if (await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.StudentId == student.Id && x.AcademicYearId == targetYear.Id &&
                x.State == EnrollmentState.Active && !x.IsDeleted, ct))
                return Error("Student already has an active enrollment in the target year.", 409);
            // Use the same target-batch rowversion write lock as admission and
            // import before checking seats. Competing allocations cannot both
            // pass capacity validation on a stale batch version.
            batch.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            batch.UpdatedBy = _user.UserId;
            _batches.Update(batch);
            await _uow.SaveChangesAsync(ct);
            if (await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.AcademicBatchId == batch.Id && x.RollNo == roll && x.State == EnrollmentState.Active && x.IsCurrent && !x.IsDeleted, ct))
                return Error("Target roll already belongs to an active student.", 409);
            if (batch.Capacity <= 0 || await _enrollments.GetQueryable().AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && x.AcademicBatchId == batch.Id && x.State == EnrollmentState.Active && x.IsCurrent && !x.IsDeleted, ct) >= batch.Capacity)
                return Error("Target batch is closed or has reached capacity.", 409);

            var now = _clock.GetUtcNow().UtcDateTime;
            source.IsCurrent = false;
            source.State = decision == StudentProgressionDecisionType.Promoted
                ? EnrollmentState.Promoted : EnrollmentState.Completed;
            // Keep the original academic period historically correct when a
            // delayed annual progression is posted in the following year.
            source.EndDate ??= effectiveDate > sourceYear.EndDate ? sourceYear.EndDate : effectiveDate;
            source.UpdatedAt = now; source.UpdatedBy = _user.UserId;
            var target = new StudentEnrollment
            {
                TenantId = tenant, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(),
                StudentId = student.Id, CampusId = batch.CampusId, AcademicYearId = batch.AcademicYearId,
                AcademicTermId = batch.AcademicTermId, AcademicProgramId = batch.AcademicProgramId,
                AcademicLevelId = batch.AcademicLevelId, AcademicBatchId = batch.Id,
                AcademicCurriculumId = curricula[0], AcademicTrackId = batch.AcademicTrackId,
                MediumId = batch.MediumId, ShiftId = batch.ShiftId,
                RollNo = roll, EnrollmentDate = targetDate, IsCurrent = true,
                State = EnrollmentState.Active, CreatedAt = now, CreatedBy = _user.UserId
            };
            await _enrollments.AddAsync(target);
            student.UpdatedAt = now; student.UpdatedBy = _user.UserId;
            await _uow.SaveChangesAsync(ct);

            var record = new StudentPromotionRecord
            {
                TenantId = tenant, ClientRequestId = request.ClientRequestId,
                PublicId = Guid.NewGuid(), StudentId = student.Id,
                FromEnrollmentId = source.Id, ToEnrollmentId = target.Id,
                Decision = decision, ProcessedAt = now, ProcessedByUserId = _user.UserId,
                Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
                CreatedAt = now, CreatedBy = _user.UserId
            };
            await _records.AddAsync(record);
            await _uow.SaveChangesAsync(ct);
            var result = Map(record, student, source, target, false);
            return new ApiResponse<StudentPromotionResultDto>
            {
                Success = true, StatusCode = 201, Message = decision == StudentProgressionDecisionType.Promoted
                    ? "Student promoted." : "Student repeat placement recorded.", Data = result
            };
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Progression concurrency conflict for tenant {TenantId}", tenant);
            return Error("Student or enrollment changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Progression integrity conflict for tenant {TenantId}", tenant);
            return Error("Student progression conflicts with an existing transaction.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Progression serialization conflict for tenant {TenantId}", tenant);
            return Error("Concurrent progression detected. Reload and retry.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student progression failed for tenant {TenantId}", tenant);
            return Error("Student progression could not be processed.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<StudentPromotionHistoryDto>>> GetHistoryAsync(
        Guid studentReference, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<StudentPromotionHistoryDto>>();
        var tenant = _user.TenantId;
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == studentReference, ct);
        if (student == null) return ApiResponse<IReadOnlyList<StudentPromotionHistoryDto>>.ErrorResponse("Student not found.", 404);
        var records = await (from record in _records.GetQueryable().AsNoTracking()
            join source in _enrollments.GetQueryable().AsNoTracking() on record.FromEnrollmentId equals source.Id
            join target in _enrollments.GetQueryable().AsNoTracking() on record.ToEnrollmentId equals target.Id
            where record.TenantId == tenant && source.TenantId == tenant && target.TenantId == tenant &&
                record.StudentId == student.Id
            orderby record.ProcessedAt descending, record.Id descending
            select new { Record = record, Source = source, Target = target }).Take(200).ToListAsync(ct);
        IReadOnlyList<StudentPromotionHistoryDto> result = records.Select(x => new StudentPromotionHistoryDto
        {
            Reference = x.Record.PublicId, Decision = x.Record.Decision,
            FromAcademicYearId = x.Source.AcademicYearId, ToAcademicYearId = x.Target.AcademicYearId,
            FromAcademicLevelId = x.Source.AcademicLevelId, ToAcademicLevelId = x.Target.AcademicLevelId,
            FromAcademicBatchId = x.Source.AcademicBatchId, ToAcademicBatchId = x.Target.AcademicBatchId,
            FromAcademicTrackId = x.Source.AcademicTrackId, ToAcademicTrackId = x.Target.AcademicTrackId,
            FromRoll = x.Source.RollNo, ToRoll = x.Target.RollNo,
            ProcessedAt = x.Record.ProcessedAt, Note = x.Record.Note
        }).ToList();
        return ApiResponse<IReadOnlyList<StudentPromotionHistoryDto>>.SuccessResponse(result);
    }

    /// <summary>
    /// Tenant-scoped, bounded historical progression readback. Enrollment rows preserve
    /// the original rolls and academic placement; no current-enrollment inference.
    /// </summary>
    public async Task<ApiResponse<PagedResult<StudentPromotionHistoryDto>>> GetHistoryPageAsync(
        Guid studentReference, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!CanManage())
            return Denied<PagedResult<StudentPromotionHistoryDto>>();
        if (studentReference == Guid.Empty || page < 1 || pageSize is < 1 or > 100 ||
            page > int.MaxValue / pageSize)
            return ApiResponse<PagedResult<StudentPromotionHistoryDto>>.ErrorResponse(
                "A valid student reference, page and page size (1-100) are required.");

        var tenant = _user.TenantId;
        var studentId = await _students.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.PublicId == studentReference && !x.IsDeleted)
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(ct);
        if (studentId is null)
            return ApiResponse<PagedResult<StudentPromotionHistoryDto>>.ErrorResponse(
                "Student not found.", 404);

        var history =
            from record in _records.GetQueryable().AsNoTracking()
            join source in _enrollments.GetQueryable().AsNoTracking()
                on record.FromEnrollmentId equals source.Id
            join target in _enrollments.GetQueryable().AsNoTracking()
                on record.ToEnrollmentId equals target.Id
            where record.TenantId == tenant && record.StudentId == studentId.Value &&
                  !record.IsDeleted && source.TenantId == tenant && !source.IsDeleted &&
                  target.TenantId == tenant && !target.IsDeleted &&
                  source.StudentId == studentId.Value && target.StudentId == studentId.Value
            select new { Record = record, Source = source, Target = target };

        var total = await history.CountAsync(ct);
        var rows = await history.OrderByDescending(x => x.Record.ProcessedAt)
            .ThenByDescending(x => x.Record.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new StudentPromotionHistoryDto
            {
                Reference = x.Record.PublicId,
                Decision = x.Record.Decision,
                FromAcademicYearId = x.Source.AcademicYearId,
                ToAcademicYearId = x.Target.AcademicYearId,
                FromAcademicLevelId = x.Source.AcademicLevelId,
                ToAcademicLevelId = x.Target.AcademicLevelId,
                FromAcademicBatchId = x.Source.AcademicBatchId,
                ToAcademicBatchId = x.Target.AcademicBatchId,
                FromAcademicTrackId = x.Source.AcademicTrackId,
                ToAcademicTrackId = x.Target.AcademicTrackId,
                FromRoll = x.Source.RollNo,
                ToRoll = x.Target.RollNo,
                ProcessedAt = x.Record.ProcessedAt,
                Note = x.Record.Note
            }).ToListAsync(ct);

        return ApiResponse<PagedResult<StudentPromotionHistoryDto>>.SuccessResponse(
            new PagedResult<StudentPromotionHistoryDto>
            {
                Items = rows, TotalCount = total, Page = page, PageSize = pageSize
            });
    }

    private async Task<ApiResponse<StudentPromotionResultDto>> ExistingResultAsync(
        StudentPromotionRecord record, Student student, CancellationToken ct)
    {
        var rows = await _enrollments.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
            (x.Id == record.FromEnrollmentId || x.Id == record.ToEnrollmentId)).ToListAsync(ct);
        var source = rows.FirstOrDefault(x => x.Id == record.FromEnrollmentId);
        var target = rows.FirstOrDefault(x => x.Id == record.ToEnrollmentId);
        return source == null || target == null
            ? Error("Existing progression record is inconsistent.", 409)
            : ApiResponse<StudentPromotionResultDto>.SuccessResponse(Map(record, student, source, target, true),
                "Student progression already processed.");
    }

    private static StudentPromotionResultDto Map(StudentPromotionRecord record, Student student,
        StudentEnrollment source, StudentEnrollment target, bool already) => new()
    {
        Reference = record.PublicId, StudentReference = student.PublicId, FromEnrollmentId = source.Id,
        ToEnrollmentId = target.Id, Decision = record.Decision,
        AcademicYearId = target.AcademicYearId, AcademicLevelId = target.AcademicLevelId,
        AcademicBatchId = target.AcademicBatchId, AcademicTrackId = target.AcademicTrackId,
        Roll = target.RollNo, ProcessedAt = record.ProcessedAt,
        StudentRowVersion = Convert.ToBase64String(student.RowVersion),
        AlreadyProcessed = already
    };
    private bool CanManage() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal"));
    private static bool IsValid(PromoteStudentWorkflowRequestDto? request) =>
        request != null && request.ClientRequestId != Guid.Empty && request.SourceEnrollmentId > 0 &&
        request.TargetAcademicYearId > 0 && request.TargetAcademicLevelId > 0 && request.TargetAcademicBatchId > 0 &&
        request.TargetAcademicTrackId is not <= 0 && !string.IsNullOrWhiteSpace(request.TargetRoll) &&
        request.TargetRoll.Trim().Length <= 50 && request.Note?.Length <= 500 &&
        Enum.IsDefined(request.Decision);
    private static bool TryVersion(string? value, out byte[] version)
    {
        version = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { version = Convert.FromBase64String(value); return version.Length > 0; }
        catch (FormatException) { return false; }
    }
    private static bool VersionsMatch(byte[] actual, byte[] expected) =>
        actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Student promotion permission required.", 403);
    private static ApiResponse<StudentPromotionResultDto> Error(string message, int code = 400) =>
        ApiResponse<StudentPromotionResultDto>.ErrorResponse(message, code);
}
