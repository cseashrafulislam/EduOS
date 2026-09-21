using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Academics;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Transactions;

namespace EduOS.Service.Services.Academic;

public sealed class AcademicEnrollmentService : IAcademicEnrollmentService
{
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<StudentSubjectRegistration> _registrations;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Guardian> _guardians;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicCurriculum> _curricula;
    private readonly IGenericRepository<CurriculumSubject> _curriculumSubjects;
    private readonly IGenericRepository<RoutineEntry> _routineEntries;
    private readonly IGenericRepository<Room> _rooms;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<AcademicEnrollmentService> _logger;

    public AcademicEnrollmentService(
        IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<StudentSubjectRegistration> registrations,
        IGenericRepository<Student> students,
        IGenericRepository<Guardian> guardians,
        IGenericRepository<AcademicBatch> batches,
        IGenericRepository<AcademicYear> years,
        IGenericRepository<AcademicCurriculum> curricula,
        IGenericRepository<CurriculumSubject> curriculumSubjects,
        IGenericRepository<RoutineEntry> routineEntries,
        IGenericRepository<Room> rooms,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        TimeProvider clock,
        ILogger<AcademicEnrollmentService> logger)
    {
        _enrollments = enrollments;
        _registrations = registrations;
        _students = students;
        _guardians = guardians;
        _batches = batches;
        _years = years;
        _curricula = curricula;
        _curriculumSubjects = curriculumSubjects;
        _routineEntries = routineEntries;
        _rooms = rooms;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<AcademicStudentEnrollmentDto>> GetCurrentAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        if (!CanRead() || studentReference == Guid.Empty) return NotFound<AcademicStudentEnrollmentDto>();
        var student = await GetAuthorizedStudentAsync(studentReference, cancellationToken);
        if (student == null) return NotFound<AcademicStudentEnrollmentDto>();
        var enrollment = await EnrollmentQuery().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id && x.IsCurrent && x.IsActive)
            .OrderByDescending(x => x.EnrollmentDate).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (enrollment == null) return NotFound<AcademicStudentEnrollmentDto>();
        var subjects = await GetRegistrationDtosAsync(enrollment.Id, cancellationToken);
        return ApiResponse<AcademicStudentEnrollmentDto>.SuccessResponse(MapEnrollment(enrollment, subjects));
    }

    public Task<ApiResponse<AcademicStudentEnrollmentDto>> EnrollAsync(CreateAcademicStudentEnrollmentDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicStudentEnrollmentDto>());
        if (request == null || request.ClientRequestId == Guid.Empty || request.StudentReference == Guid.Empty || request.AcademicBatchId <= 0 || string.IsNullOrWhiteSpace(request.RollNo))
            return Task.FromResult(Error<AcademicStudentEnrollmentDto>("Request ID, student, academic batch and roll are required."));
        return ExecuteWriteAsync("enrol student", async () =>
        {
            var tenantId = _currentUser.TenantId;
            var roll = request.RollNo.Trim();
            var replay = await EnrollmentQuery().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (replay != null)
            {
                if (replay.Student?.PublicId != request.StudentReference || replay.AcademicBatchId != request.AcademicBatchId || !string.Equals(replay.RollNo, roll, StringComparison.OrdinalIgnoreCase) || request.EnrollmentDate.HasValue && replay.EnrollmentDate.Date != request.EnrollmentDate.Value.Date)
                    return Error<AcademicStudentEnrollmentDto>("Client request ID was already used for a different enrollment.", 409);
                var replaySubjects = await GetRegistrationDtosAsync(replay.Id, cancellationToken);
                return ApiResponse<AcademicStudentEnrollmentDto>.SuccessResponse(MapEnrollment(replay, replaySubjects), "Academic enrollment already exists.");
            }
            var student = await _students.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == request.StudentReference && x.IsActive, cancellationToken);
            if (student == null) return Error<AcademicStudentEnrollmentDto>("Student not found.", 404);
            var batch = await _batches.GetQueryable().Include(x => x.AcademicYear).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.AcademicBatchId && x.IsActive, cancellationToken);
            if (batch?.AcademicYear == null) return Error<AcademicStudentEnrollmentDto>("Academic batch not found.", 404);
            var enrollmentDate = (request.EnrollmentDate ?? _clock.GetUtcNow().UtcDateTime).Date;
            if (enrollmentDate < batch.AcademicYear.StartDate.Date || enrollmentDate > batch.AcademicYear.EndDate.Date || batch.StartDate.HasValue && enrollmentDate < batch.StartDate.Value.Date || batch.EndDate.HasValue && enrollmentDate > batch.EndDate.Value.Date)
                return Error<AcademicStudentEnrollmentDto>("Enrollment date is outside the academic batch period.", 409);
            var current = await _enrollments.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.StudentId == student.Id && x.IsCurrent && x.IsActive, cancellationToken);
            if (current != null)
            {
                if (current.AcademicBatchId == batch.Id && string.Equals(current.RollNo, roll, StringComparison.OrdinalIgnoreCase))
                {
                    var currentRow = await EnrollmentQuery().FirstAsync(x => x.Id == current.Id, cancellationToken);
                    var currentSubjects = await GetRegistrationDtosAsync(current.Id, cancellationToken);
                    return ApiResponse<AcademicStudentEnrollmentDto>.SuccessResponse(MapEnrollment(currentRow, currentSubjects), "Student is already enrolled.");
                }
                return Error<AcademicStudentEnrollmentDto>("Student already has a current academic enrollment.", 409);
            }
            if (await _enrollments.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.AcademicBatchId == batch.Id && x.RollNo == roll && x.IsCurrent && x.IsActive, cancellationToken))
                return Error<AcademicStudentEnrollmentDto>("Roll is already assigned in this academic batch.", 409);
            var occupied = await _enrollments.GetQueryable().CountAsync(x => x.TenantId == tenantId && x.AcademicBatchId == batch.Id && x.IsCurrent && x.IsActive && x.EnrollmentStatus == EnrollmentStatus.Active, cancellationToken);
            if (batch.Capacity <= 0 || occupied >= batch.Capacity) return Error<AcademicStudentEnrollmentDto>("Academic batch capacity has been reached.", 409);
            var curriculum = await ResolveCurriculumAsync(batch, cancellationToken);
            if (curriculum == null) return Error<AcademicStudentEnrollmentDto>("No effective curriculum is configured for this academic batch.", 409);
            var curriculumSubjects = await CurriculumSubjectQuery(curriculum.Id, batch).ToListAsync(cancellationToken);
            if (curriculumSubjects.Count == 0) return Error<AcademicStudentEnrollmentDto>("The effective curriculum has no subjects for this academic level.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            var row = new StudentEnrollment
            {
                TenantId = tenantId,
                ClientRequestId = request.ClientRequestId,
                StudentId = student.Id,
                CampusId = batch.CampusId,
                AcademicYearId = batch.AcademicYearId,
                AcademicTermId = batch.AcademicTermId,
                AcademicProgramId = batch.AcademicProgramId,
                AcademicLevelId = batch.AcademicLevelId,
                AcademicBatchId = batch.Id,
                AcademicCurriculumId = curriculum.Id,
                MediumId = batch.MediumId,
                ShiftId = batch.ShiftId,
                AcademicTrackId = batch.AcademicTrackId,
                RollNo = roll,
                EnrollmentDate = enrollmentDate,
                EnrollmentStatus = EnrollmentStatus.Active,
                IsCurrent = true,
                IsActive = true,
                Remarks = Trim(request.Remarks),
                Student = student,
                AcademicBatch = batch,
                AcademicYear = batch.AcademicYear,
                AcademicCurriculum = curriculum
            };
            await _enrollments.AddAsync(row);
            var requiredRegistrations = curriculumSubjects.Where(x => !x.IsOptional).Select(x => CreateRegistration(row, student, batch, curriculum, x, true, SubjectRegistrationStatus.Approved, now, null, request.Remarks, _currentUser.UserId)).ToList();
            await _registrations.AddRangeAsync(requiredRegistrations);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(MapEnrollment(row, requiredRegistrations.Select(MapRegistration).ToList()), "Student enrolled in academic batch.");
        });
    }

    public Task<ApiResponse<StudentSubjectRegistrationDto>> RequestOptionalSubjectAsync(long studentEnrollmentId, RequestOptionalSubjectDto request, CancellationToken cancellationToken = default)
    {
        if (!CanSelfServe()) return Task.FromResult(Denied<StudentSubjectRegistrationDto>());
        if (studentEnrollmentId <= 0 || request == null || request.ClientRequestId == Guid.Empty || request.SubjectId <= 0)
            return Task.FromResult(Error<StudentSubjectRegistrationDto>("Enrollment, subject and client request ID are required."));
        return ExecuteWriteAsync("request optional subject", async () =>
        {
            var tenantId = _currentUser.TenantId;
            var replay = await _registrations.GetQueryable().Include(x => x.Student).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (replay != null)
            {
                if (replay.Student == null || !await CanAccessStudentAsync(replay.Student, cancellationToken)) return NotFound<StudentSubjectRegistrationDto>();
                if (replay.StudentEnrollmentId != studentEnrollmentId || replay.SubjectId != request.SubjectId)
                    return Error<StudentSubjectRegistrationDto>("Client request ID was already used for a different subject request.", 409);
                return ApiResponse<StudentSubjectRegistrationDto>.SuccessResponse(MapRegistration(replay), "Subject request already exists.");
            }
            var enrollment = await EnrollmentQuery().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == studentEnrollmentId && x.IsCurrent && x.IsActive, cancellationToken);
            if (enrollment?.Student == null || !await CanAccessStudentAsync(enrollment.Student, cancellationToken)) return NotFound<StudentSubjectRegistrationDto>();
            var option = await CurriculumSubjectQuery(enrollment.AcademicCurriculumId, enrollment.AcademicBatch!).FirstOrDefaultAsync(x => x.SubjectId == request.SubjectId && x.IsOptional, cancellationToken);
            if (option == null) return Error<StudentSubjectRegistrationDto>("Optional subject is unavailable for this enrollment.", 409);
            var existing = await _registrations.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.StudentEnrollmentId == enrollment.Id && x.SubjectId == request.SubjectId, cancellationToken);
            var now = _clock.GetUtcNow().UtcDateTime;
            if (existing != null)
            {
                if (existing.Status is SubjectRegistrationStatus.Pending or SubjectRegistrationStatus.Approved)
                    return ApiResponse<StudentSubjectRegistrationDto>.SuccessResponse(MapRegistration(existing), "Subject is already registered or pending approval.");
                existing.ClientRequestId = request.ClientRequestId;
                existing.Status = SubjectRegistrationStatus.Pending;
                existing.RequestedAtUtc = now;
                existing.DecidedAtUtc = null;
                existing.DecidedByUserId = null;
                existing.Remarks = Trim(request.Remarks);
                _registrations.Update(existing);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return ApiResponse<StudentSubjectRegistrationDto>.SuccessResponse(MapRegistration(existing), "Optional subject request reopened.");
            }
            var row = CreateRegistration(enrollment, enrollment.Student, enrollment.AcademicBatch!, enrollment.AcademicCurriculum!, option, false, SubjectRegistrationStatus.Pending, now, request.ClientRequestId, request.Remarks, null);
            await _registrations.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(MapRegistration(row), "Optional subject requested for approval.");
        });
    }

    public Task<ApiResponse<StudentSubjectRegistrationDto>> DecideSubjectAsync(long registrationId, DecideSubjectRegistrationDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<StudentSubjectRegistrationDto>());
        if (registrationId <= 0 || request == null || request.Status is not (SubjectRegistrationStatus.Approved or SubjectRegistrationStatus.Rejected) || !TryDecodeRowVersion(request.RowVersion, out var rowVersion))
            return Task.FromResult(Error<StudentSubjectRegistrationDto>("A valid decision and row version are required."));
        return ExecuteWriteAsync("decide subject registration", async () =>
        {
            var row = await _registrations.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == registrationId, cancellationToken);
            if (row == null) return NotFound<StudentSubjectRegistrationDto>();
            if (row.IsRequired) return Error<StudentSubjectRegistrationDto>("Required curriculum subjects do not use elective approval.", 409);
            if (row.Status == request.Status) return ApiResponse<StudentSubjectRegistrationDto>.SuccessResponse(MapRegistration(row), "Subject decision already applied.");
            if (row.Status != SubjectRegistrationStatus.Pending) return Error<StudentSubjectRegistrationDto>("Only pending subject requests can be decided.", 409);
            if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, rowVersion)) return Error<StudentSubjectRegistrationDto>("Subject request changed. Reload and try again.", 409);
            row.Status = request.Status;
            row.DecidedAtUtc = _clock.GetUtcNow().UtcDateTime;
            row.DecidedByUserId = _currentUser.UserId;
            row.Remarks = Trim(request.Remarks) ?? row.Remarks;
            _registrations.Update(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<StudentSubjectRegistrationDto>.SuccessResponse(MapRegistration(row), "Subject registration decision saved.");
        });
    }

    public async Task<ApiResponse<IReadOnlyList<RoutineEntryDto>>> GetTimetableAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        if (!CanRead() || studentReference == Guid.Empty) return NotFound<IReadOnlyList<RoutineEntryDto>>();
        var student = await GetAuthorizedStudentAsync(studentReference, cancellationToken);
        if (student == null) return NotFound<IReadOnlyList<RoutineEntryDto>>();
        var enrollment = await _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id && x.IsCurrent && x.IsActive)
            .OrderByDescending(x => x.EnrollmentDate).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (enrollment == null) return NotFound<IReadOnlyList<RoutineEntryDto>>();
        var subjectIds = await _registrations.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.StudentEnrollmentId == enrollment.Id && x.Status == SubjectRegistrationStatus.Approved).Select(x => x.SubjectId).ToListAsync(cancellationToken);
        if (subjectIds.Count == 0) return ApiResponse<IReadOnlyList<RoutineEntryDto>>.SuccessResponse(Array.Empty<RoutineEntryDto>());
        var entries = await _routineEntries.GetQueryable().AsNoTracking()
            .Include(x => x.AcademicBatch).Include(x => x.RoutineTimeSlot).Include(x => x.Subject).Include(x => x.Employee)
            .Where(x => x.TenantId == _currentUser.TenantId && x.AcademicBatchId == enrollment.AcademicBatchId && x.AcademicYearId == enrollment.AcademicYearId && x.AcademicTermId == enrollment.AcademicTermId && subjectIds.Contains(x.SubjectId) && x.IsActive)
            .OrderBy(x => x.DayOfWeek).ThenBy(x => x.RoutineTimeSlot!.StartTime).ThenBy(x => x.Subject!.Name)
            .ToListAsync(cancellationToken);
        var roomIds = entries.Where(x => x.RoomId.HasValue).Select(x => x.RoomId!.Value).Distinct().ToList();
        var roomNames = await _rooms.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && roomIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        IReadOnlyList<RoutineEntryDto> rows = entries.Select(x => new RoutineEntryDto
        {
            Id = x.Id,
            AcademicBatchId = x.AcademicBatchId,
            BatchName = x.AcademicBatch?.Name ?? string.Empty,
            SubjectId = x.SubjectId,
            SubjectName = x.Subject?.Name ?? string.Empty,
            EmployeeId = x.EmployeeId,
            EmployeeName = x.Employee?.FullName ?? string.Empty,
            AcademicYearId = x.AcademicYearId,
            AcademicTermId = x.AcademicTermId,
            DayOfWeek = x.DayOfWeek,
            RoutineTimeSlotId = x.RoutineTimeSlotId,
            TimeSlotName = x.RoutineTimeSlot?.Name ?? string.Empty,
            StartTime = x.RoutineTimeSlot?.StartTime ?? default,
            EndTime = x.RoutineTimeSlot?.EndTime ?? default,
            RoomId = x.RoomId,
            RoomName = x.RoomId.HasValue && roomNames.TryGetValue(x.RoomId.Value, out var roomName) ? roomName : null,
            Remarks = x.Remarks,
            IsActive = x.IsActive
        }).ToList();
        return ApiResponse<IReadOnlyList<RoutineEntryDto>>.SuccessResponse(rows);
    }

    private IQueryable<StudentEnrollment> EnrollmentQuery() => _enrollments.GetQueryable()
        .Include(x => x.Student).Include(x => x.AcademicBatch).Include(x => x.AcademicCurriculum);

    private IQueryable<CurriculumSubject> CurriculumSubjectQuery(long curriculumId, AcademicBatch batch) => _curriculumSubjects.GetQueryable()
        .Include(x => x.Subject)
        .Where(x => x.TenantId == _currentUser.TenantId && x.AcademicCurriculumId == curriculumId && x.AcademicLevelId == batch.AcademicLevelId && x.IsActive && x.Subject != null && x.Subject.IsActive && (!x.AcademicTrackId.HasValue || x.AcademicTrackId == batch.AcademicTrackId) && (!x.MediumId.HasValue || x.MediumId == batch.MediumId));

    private async Task<AcademicCurriculum?> ResolveCurriculumAsync(AcademicBatch batch, CancellationToken cancellationToken)
    {
        var candidates = await _curricula.GetQueryable().Where(x => x.TenantId == _currentUser.TenantId && x.AcademicProgramId == batch.AcademicProgramId && x.IsActive).ToListAsync(cancellationToken);
        if (candidates.Count == 0) return null;
        var boundaryIds = candidates.SelectMany(x => new[] { x.EffectiveFromAcademicYearId, x.EffectiveToAcademicYearId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var boundaries = await _years.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && boundaryIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        return candidates
            .Where(x => (!x.EffectiveFromAcademicYearId.HasValue || boundaries.TryGetValue(x.EffectiveFromAcademicYearId.Value, out var from) && from.StartDate.Date <= batch.AcademicYear!.EndDate.Date)
                     && (!x.EffectiveToAcademicYearId.HasValue || boundaries.TryGetValue(x.EffectiveToAcademicYearId.Value, out var to) && to.EndDate.Date >= batch.AcademicYear!.StartDate.Date))
            .OrderByDescending(x => x.IsCurrent)
            .ThenByDescending(x => x.EffectiveFromAcademicYearId)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
    }

    private static StudentSubjectRegistration CreateRegistration(StudentEnrollment enrollment, Student student, AcademicBatch batch, AcademicCurriculum curriculum, CurriculumSubject option, bool isRequired, SubjectRegistrationStatus status, DateTime now, Guid? clientRequestId, string? remarks, long? decidedByUserId) => new()
    {
        TenantId = enrollment.TenantId,
        ClientRequestId = clientRequestId,
        StudentEnrollment = enrollment,
        StudentId = student.Id,
        AcademicYearId = batch.AcademicYearId,
        AcademicTermId = batch.AcademicTermId,
        AcademicBatchId = batch.Id,
        AcademicCurriculumId = curriculum.Id,
        CurriculumSubjectId = option.Id,
        SubjectId = option.SubjectId,
        IsRequired = isRequired,
        Status = status,
        RequestedAtUtc = now,
        DecidedAtUtc = status == SubjectRegistrationStatus.Approved ? now : null,
        DecidedByUserId = status == SubjectRegistrationStatus.Approved ? decidedByUserId : null,
        FullMarksSnapshot = option.FullMarks,
        PassMarksSnapshot = option.PassMarks,
        CreditHoursSnapshot = option.CreditHours,
        SubjectCodeSnapshot = option.Subject?.Code ?? string.Empty,
        SubjectNameSnapshot = option.Subject?.Name ?? string.Empty,
        Remarks = Trim(remarks),
        Student = student,
        AcademicBatch = batch,
        AcademicCurriculum = curriculum,
        CurriculumSubject = option,
        Subject = option.Subject
    };

    private async Task<Student?> GetAuthorizedStudentAsync(Guid reference, CancellationToken cancellationToken)
    {
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.PublicId == reference && x.IsActive, cancellationToken);
        return student != null && await CanAccessStudentAsync(student, cancellationToken) ? student : null;
    }

    private async Task<bool> CanAccessStudentAsync(Student student, CancellationToken cancellationToken)
    {
        if (CanManage()) return true;
        if (!CanSelfServe()) return false;
        if (student.UserId == _currentUser.UserId) return true;
        return await _guardians.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id && x.UserId == _currentUser.UserId, cancellationToken);
    }

    private async Task<IReadOnlyList<StudentSubjectRegistrationDto>> GetRegistrationDtosAsync(long enrollmentId, CancellationToken cancellationToken) =>
        (await _registrations.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.StudentEnrollmentId == enrollmentId).OrderByDescending(x => x.IsRequired).ThenBy(x => x.SubjectNameSnapshot).ToListAsync(cancellationToken)).Select(MapRegistration).ToList();

    private async Task<ApiResponse<T>> ExecuteWriteAsync<T>(string operation, Func<Task<ApiResponse<T>>> action)
    {
        try
        {
            var strategy = _unitOfWork.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var scope = SerializableScope();
                var response = await action();
                if (response.Success) scope.Complete();
                return response;
            });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Stale academic enrollment write during {Operation} for tenant {TenantId}", operation, _currentUser.TenantId);
            return Error<T>("Academic enrollment changed. Reload and try again.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting academic enrollment write during {Operation} for tenant {TenantId}", operation, _currentUser.TenantId);
            return Error<T>("Academic enrollment conflicts with another update. Reload and try again.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Serialized academic enrollment write aborted during {Operation} for tenant {TenantId}", operation, _currentUser.TenantId);
            return Error<T>("Academic enrollment conflicts with another update. Reload and try again.", 409);
        }
    }

    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (CanManage() || CanSelfServe());
    private bool CanSelfServe() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (_currentUser.IsInRole("Student") || _currentUser.IsInRole("Guardian") || _currentUser.IsInRole("Parent"));
    private bool CanManage() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (_currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal") || _currentUser.IsInRole("VicePrincipal"));
    private static TransactionScope SerializableScope() => new(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);
    private static bool TryDecodeRowVersion(string value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { bytes = Convert.FromBase64String(value); return bytes.Length > 0; }
        catch (FormatException) { return false; }
    }
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static AcademicStudentEnrollmentDto MapEnrollment(StudentEnrollment x, IReadOnlyList<StudentSubjectRegistrationDto> subjects) => new() { Id = x.Id, StudentReference = x.Student?.PublicId ?? Guid.Empty, StudentName = x.Student?.FullName ?? string.Empty, CampusId = x.CampusId, AcademicYearId = x.AcademicYearId, AcademicTermId = x.AcademicTermId, AcademicProgramId = x.AcademicProgramId, AcademicLevelId = x.AcademicLevelId, AcademicBatchId = x.AcademicBatchId, BatchName = x.AcademicBatch?.Name ?? string.Empty, AcademicCurriculumId = x.AcademicCurriculumId, CurriculumName = x.AcademicCurriculum?.Name ?? string.Empty, RollNo = x.RollNo, EnrollmentDate = x.EnrollmentDate, EnrollmentStatus = x.EnrollmentStatus, IsCurrent = x.IsCurrent, IsActive = x.IsActive, RowVersion = Convert.ToBase64String(x.RowVersion), Subjects = subjects };
    private static StudentSubjectRegistrationDto MapRegistration(StudentSubjectRegistration x) => new() { Id = x.Id, StudentEnrollmentId = x.StudentEnrollmentId, SubjectId = x.SubjectId, SubjectCode = x.SubjectCodeSnapshot, SubjectName = x.SubjectNameSnapshot, FullMarks = x.FullMarksSnapshot, PassMarks = x.PassMarksSnapshot, CreditHours = x.CreditHoursSnapshot, IsRequired = x.IsRequired, Status = x.Status, RequestedAtUtc = x.RequestedAtUtc, DecidedAtUtc = x.DecidedAtUtc, Remarks = x.Remarks, RowVersion = Convert.ToBase64String(x.RowVersion) };
    private static ApiResponse<T> Created<T>(T data, string message) => new() { Success = true, StatusCode = 201, Message = message, Data = data };
    private static ApiResponse<T> Error<T>(string message, int statusCode = 400) => ApiResponse<T>.ErrorResponse(message, statusCode);
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Academic enrollment access is required.", 403);
    private static ApiResponse<T> NotFound<T>() => ApiResponse<T>.ErrorResponse("Academic enrollment not found.", 404);
}
