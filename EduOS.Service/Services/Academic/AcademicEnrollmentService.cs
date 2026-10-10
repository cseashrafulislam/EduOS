using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Academic;

public sealed class AcademicEnrollmentService : IAcademicEnrollmentService
{
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<StudentSubjectRegistration> _registrations;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Guardian> _guardians;
    private readonly IGenericRepository<StudentGuardian> _studentGuardians;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicCurriculum> _curricula;
    private readonly IGenericRepository<CurriculumSubject> _curriculumSubjects;
    private readonly IGenericRepository<SubjectOffering> _offerings;
    private readonly IGenericRepository<Subject> _subjects;
    private readonly IGenericRepository<RoutineEntry> _routineEntries;
    private readonly IGenericRepository<RoutineTimeSlot> _timeSlots;
    private readonly IGenericRepository<Room> _rooms;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<AcademicEnrollmentService> _logger;

    public AcademicEnrollmentService(IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<StudentSubjectRegistration> registrations,
        IGenericRepository<Student> students, IGenericRepository<Guardian> guardians,
        IGenericRepository<StudentGuardian> studentGuardians,
        IGenericRepository<AcademicBatch> batches, IGenericRepository<AcademicYear> years,
        IGenericRepository<AcademicCurriculum> curricula, IGenericRepository<CurriculumSubject> curriculumSubjects,
        IGenericRepository<SubjectOffering> offerings, IGenericRepository<Subject> subjects,
        IGenericRepository<RoutineEntry> routineEntries, IGenericRepository<RoutineTimeSlot> timeSlots,
        IGenericRepository<Room> rooms, IUnitOfWork unitOfWork,
        ICurrentUserService currentUser, TimeProvider clock, ILogger<AcademicEnrollmentService> logger)
    {
        _enrollments = enrollments; _registrations = registrations;
        _students = students; _guardians = guardians; _studentGuardians = studentGuardians;
        _batches = batches; _years = years; _curricula = curricula;
        _curriculumSubjects = curriculumSubjects; _offerings = offerings; _subjects = subjects;
        _routineEntries = routineEntries; _timeSlots = timeSlots; _rooms = rooms;
        _uow = unitOfWork; _user = currentUser; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<StudentEnrollmentDto>> GetCurrentAsync(Guid studentReference,
        CancellationToken ct = default)
    {
        if (!CanRead() || studentReference == Guid.Empty) return NotFound<StudentEnrollmentDto>();
        var student = await AuthorizedStudentAsync(studentReference, ct);
        if (student == null) return NotFound<StudentEnrollmentDto>();
        var enrollment = await _enrollments.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
            x.StudentId == student.Id && x.IsCurrent && x.State == EnrollmentState.Active)
            .OrderByDescending(x => x.EnrollmentDate).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        return enrollment == null ? NotFound<StudentEnrollmentDto>() :
            ApiResponse<StudentEnrollmentDto>.SuccessResponse(await BuildEnrollmentDtoAsync(enrollment, student, ct));
    }

    public Task<ApiResponse<StudentEnrollmentDto>> EnrollAsync(CreateStudentEnrollmentRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<StudentEnrollmentDto>());
        if (request == null || request.ClientRequestId == Guid.Empty || request.StudentReference == Guid.Empty ||
            request.AcademicBatchId <= 0 || request.AcademicCurriculumId <= 0 || string.IsNullOrWhiteSpace(request.RollNo) ||
            request.RollNo.Trim().Length > 50 || request.Remarks?.Length > 500)
            return Task.FromResult(Error<StudentEnrollmentDto>("Student, batch, roll and request reference are required."));
        return ExecuteWriteAsync("enroll student", async () =>
        {
            var tenant = _user.TenantId;
            var roll = request.RollNo.Trim();
            var replay = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, ct);
            if (replay != null)
            {
                var linked = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == replay.StudentId, ct);
                if (linked == null || linked.PublicId != request.StudentReference ||
                    replay.AcademicBatchId != request.AcademicBatchId || replay.AcademicCurriculumId != request.AcademicCurriculumId ||
                    !string.Equals(replay.RollNo, roll, StringComparison.OrdinalIgnoreCase) ||
                    request.EnrollmentDate != default && replay.EnrollmentDate != request.EnrollmentDate)
                    return Error<StudentEnrollmentDto>("Request ID was previously used for different enrollment data.", 409);
                return ApiResponse<StudentEnrollmentDto>.SuccessResponse(
                    await BuildEnrollmentDtoAsync(replay, linked, ct), "Enrollment already exists.");
            }

            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.StudentReference && x.StatusCode == "Active" && !x.IsDeleted, ct);
            if (student == null) return Error<StudentEnrollmentDto>("Active student not found.", 404);
            var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicBatchId && x.IsActive, ct);
            if (batch == null) return Error<StudentEnrollmentDto>("Active academic batch not found.", 404);
            var year = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == batch.AcademicYearId && x.IsActive, ct);
            if (year == null) return Error<StudentEnrollmentDto>("Academic year is unavailable.", 409);
            var date = request.EnrollmentDate != default
                ? request.EnrollmentDate : DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
            if (date < year.StartDate || date > year.EndDate ||
                batch.StartDate.HasValue && date < batch.StartDate.Value ||
                batch.EndDate.HasValue && date > batch.EndDate.Value)
                return Error<StudentEnrollmentDto>("Enrollment date is outside the academic batch period.", 409);
            var current = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.StudentId == student.Id && x.IsCurrent && x.State == EnrollmentState.Active, ct);
            if (current != null)
            {
                if (current.AcademicBatchId == batch.Id && string.Equals(current.RollNo, roll, StringComparison.OrdinalIgnoreCase) &&
                    current.EnrollmentDate == date)
                    return ApiResponse<StudentEnrollmentDto>.SuccessResponse(
                        await BuildEnrollmentDtoAsync(current, student, ct), "Student already enrolled.");
                return Error<StudentEnrollmentDto>("Student already has a current enrollment.", 409);
            }
            if (await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicBatchId == batch.Id && x.RollNo == roll &&
                x.IsCurrent && x.State == EnrollmentState.Active, ct))
                return Error<StudentEnrollmentDto>("Roll already assigned in this academic batch.", 409);
            var occupied = await _enrollments.GetQueryable().AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && x.AcademicBatchId == batch.Id &&
                x.IsCurrent && x.State == EnrollmentState.Active, ct);
            if (batch.Capacity <= 0 || occupied >= batch.Capacity)
                return Error<StudentEnrollmentDto>("Batch capacity has been reached.", 409);

            var curricula = await _curricula.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AcademicProgramId == batch.AcademicProgramId &&
                x.AcademicTrackId == batch.AcademicTrackId && x.MediumId == batch.MediumId &&
                x.Id == request.AcademicCurriculumId && x.IsCurrent && x.State == EnrollmentState.Active && x.EffectiveFrom <= date &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= date))
                .Take(2).ToListAsync(ct);
            if (curricula.Count != 1)
                return Error<StudentEnrollmentDto>("Exactly one effective curriculum is required for this batch.", 409);
            var curriculum = curricula[0];
            var requiredSubjects = await _curriculumSubjects.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AcademicCurriculumId == curriculum.Id &&
                x.AcademicLevelId == batch.AcademicLevelId && x.IsActive && !x.IsOptional).ToListAsync(ct);
            if (requiredSubjects.Count == 0)
                return Error<StudentEnrollmentDto>("Curriculum must contain required subjects.", 409);
            var requiredIds = requiredSubjects.Select(x => x.Id).ToArray();
            var offerings = await _offerings.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AcademicBatchId == batch.Id && x.IsActive &&
                requiredIds.Contains(x.CurriculumSubjectId)).ToListAsync(ct);
            if (offerings.GroupBy(x => x.CurriculumSubjectId).Any(x => x.Count() != 1) ||
                requiredSubjects.Any(x => offerings.All(y => y.CurriculumSubjectId != x.Id)))
                return Error<StudentEnrollmentDto>("Each required curriculum subject needs exactly one active batch offering.", 409);

            var now = _clock.GetUtcNow().UtcDateTime;
            var entity = new StudentEnrollment
            {
                TenantId = tenant, PublicId = Guid.NewGuid(), ClientRequestId = request.ClientRequestId,
                StudentId = student.Id, CampusId = batch.CampusId, AcademicYearId = batch.AcademicYearId,
                AcademicTermId = batch.AcademicTermId, AcademicProgramId = batch.AcademicProgramId,
                AcademicLevelId = batch.AcademicLevelId, AcademicBatchId = batch.Id,
                AcademicCurriculumId = curriculum.Id, AcademicTrackId = batch.AcademicTrackId,
                MediumId = batch.MediumId, ShiftId = batch.ShiftId, RollNo = roll,
                EnrollmentDate = date, State = EnrollmentState.Active,
                IsCurrent = true, Remarks = Trim(request.Remarks),
                CreatedAt = now, CreatedBy = _user.UserId
            };
            await _enrollments.AddAsync(entity);
            await _uow.SaveChangesAsync(ct);
            foreach (var offering in offerings)
            {
                var item = requiredSubjects.Single(x => x.Id == offering.CurriculumSubjectId);
                await _registrations.AddAsync(new StudentSubjectRegistration
                {
                    TenantId = tenant, ClientRequestId = Guid.NewGuid(),
                    StudentEnrollmentId = entity.Id, SubjectOfferingId = offering.Id,
                    State = SubjectRegistrationState.Approved, RegisteredAt = now,
                    CreditHoursSnapshot = item.CreditHours, ApprovedByUserId = _user.UserId,
                    ApprovedAt = now, CreatedAt = now, CreatedBy = _user.UserId
                });
            }
            await _uow.SaveChangesAsync(ct);
            var result = await BuildEnrollmentDtoAsync(entity, student, ct);
            return Created(result, "Student enrolled in academic batch.");
        });
    }

    public Task<ApiResponse<StudentSubjectRegistrationDto>> RequestOptionalSubjectAsync(long studentEnrollmentId,
        RequestOptionalSubjectDto request, CancellationToken ct = default)
    {
        if (!CanSelfServe() && !CanManage()) return Task.FromResult(Denied<StudentSubjectRegistrationDto>());
        if (studentEnrollmentId <= 0 || request == null || request.ClientRequestId == Guid.Empty || request.SubjectId <= 0)
            return Task.FromResult(Error<StudentSubjectRegistrationDto>("Enrollment, subject and request ID are required."));
        return ExecuteWriteAsync("request optional subject", async () =>
        {
            var tenant = _user.TenantId;
            var replay = await _registrations.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, ct);
            if (replay != null)
            {
                var candidate = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == replay.StudentEnrollmentId, ct);
                var oldStudent = candidate == null ? null : await _students.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == candidate.StudentId, ct);
                if (candidate == null || oldStudent == null || !await CanAccessStudentAsync(oldStudent, ct))
                    return NotFound<StudentSubjectRegistrationDto>();
                var mapped = await GetRegistrationDtosAsync(candidate.Id, ct);
                var old = mapped.FirstOrDefault(x => x.Id == replay.Id);
                if (candidate.Id != studentEnrollmentId || old == null ||
                    !await OfferingMatchesSubjectAsync(replay.SubjectOfferingId, request.SubjectId, ct))
                    return Error<StudentSubjectRegistrationDto>("Request ID was reused for another subject.", 409);
                return ApiResponse<StudentSubjectRegistrationDto>.SuccessResponse(old, "Subject request already exists.");
            }
            var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == studentEnrollmentId && x.IsCurrent &&
                x.IsActive && x.State == EnrollmentState.Active, ct);
            var student = enrollment == null ? null : await _students.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == enrollment.StudentId, ct);
            if (enrollment == null || student == null || !await CanAccessStudentAsync(student, ct))
                return NotFound<StudentSubjectRegistrationDto>();
            var matches = await (from offer in _offerings.GetQueryable().AsNoTracking()
                join item in _curriculumSubjects.GetQueryable().AsNoTracking() on offer.CurriculumSubjectId equals item.Id
                where offer.TenantId == tenant && item.TenantId == tenant && offer.IsActive &&
                    item.IsActive && item.IsOptional && offer.AcademicBatchId == enrollment.AcademicBatchId &&
                    item.AcademicCurriculumId == enrollment.AcademicCurriculumId &&
                    item.AcademicLevelId == enrollment.AcademicLevelId && item.SubjectId == request.SubjectId
                select new { OfferId = offer.Id, item.CreditHours }).Take(2).ToListAsync(ct);
            if (matches.Count != 1) return Error<StudentSubjectRegistrationDto>("Optional subject offering is unavailable or ambiguous.", 409);
            var selected = matches[0];
            var exists = await _registrations.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.StudentEnrollmentId == enrollment.Id && x.SubjectOfferingId == selected.OfferId, ct);
            if (exists) return Error<StudentSubjectRegistrationDto>("Subject registration already exists. Review its current state.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            var entity = new StudentSubjectRegistration
            {
                TenantId = tenant, ClientRequestId = request.ClientRequestId,
                StudentEnrollmentId = enrollment.Id, SubjectOfferingId = selected.OfferId,
                State = SubjectRegistrationState.Pending, RegisteredAt = now,
                CreditHoursSnapshot = selected.CreditHours, Remarks = Trim(request.Remarks),
                CreatedAt = now, CreatedBy = _user.UserId
            };
            await _registrations.AddAsync(entity);
            await _uow.SaveChangesAsync(ct);
            var dtos = await GetRegistrationDtosAsync(enrollment.Id, ct);
            return Created(dtos.Single(x => x.Id == entity.Id), "Optional subject request submitted.");
        });
    }

    public Task<ApiResponse<StudentSubjectRegistrationDto>> DecideSubjectAsync(long registrationId,
        DecideSubjectRegistrationDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<StudentSubjectRegistrationDto>());
        if (registrationId <= 0 || request == null ||
            request.Status is not (SubjectRegistrationStatus.Approved or SubjectRegistrationStatus.Rejected) ||
            !TryVersion(request.RowVersion, out var expected))
            return Task.FromResult(Error<StudentSubjectRegistrationDto>("A valid decision and row version are required."));
        return ExecuteWriteAsync("decide subject registration", async () =>
        {
            var tenant = _user.TenantId;
            var entity = await _registrations.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == registrationId, ct);
            if (entity == null) return NotFound<StudentSubjectRegistrationDto>();
            var offering = await (from offer in _offerings.GetQueryable().AsNoTracking()
                join item in _curriculumSubjects.GetQueryable().AsNoTracking() on offer.CurriculumSubjectId equals item.Id
                where offer.TenantId == tenant && item.TenantId == tenant &&
                    offer.Id == entity.SubjectOfferingId
                select new { item.IsOptional }).FirstOrDefaultAsync(ct);
            if (offering == null || !offering.IsOptional)
                return Error<StudentSubjectRegistrationDto>("Required subjects do not use elective approval.", 409);
            var decision = Enum.Parse<SubjectRegistrationState>(request.Status.ToString());
            if (entity.State == decision)
            {
                var existing = await GetRegistrationDtosAsync(entity.StudentEnrollmentId, ct);
                return ApiResponse<StudentSubjectRegistrationDto>.SuccessResponse(
                    existing.Single(x => x.Id == entity.Id), "Subject decision already applied.");
            }
            if (entity.State != SubjectRegistrationState.Pending)
                return Error<StudentSubjectRegistrationDto>("Only pending subject requests can be decided.", 409);
            if (!VersionsMatch(entity.RowVersion, expected))
                return Error<StudentSubjectRegistrationDto>("Subject request changed. Reload and retry.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            entity.State = decision;
            if (decision == SubjectRegistrationState.Approved)
            {
                entity.ApprovedAt = now;
                entity.ApprovedByUserId = _user.UserId;
            }
            entity.UpdatedAt = now; entity.UpdatedBy = _user.UserId;
            entity.Remarks = Trim(request.Remarks) ?? entity.Remarks;
            await _uow.SaveChangesAsync(ct);
            var dtos = await GetRegistrationDtosAsync(entity.StudentEnrollmentId, ct);
            return ApiResponse<StudentSubjectRegistrationDto>.SuccessResponse(
                dtos.Single(x => x.Id == entity.Id), "Subject decision saved.");
        });
    }

    public async Task<ApiResponse<IReadOnlyList<RoutineEntryDto>>> GetTimetableAsync(Guid studentReference,
        CancellationToken ct = default)
    {
        if (!CanRead() || studentReference == Guid.Empty) return NotFound<IReadOnlyList<RoutineEntryDto>>();
        var student = await AuthorizedStudentAsync(studentReference, ct);
        if (student == null) return NotFound<IReadOnlyList<RoutineEntryDto>>();
        var enrollment = await _enrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.StudentId == student.Id && x.IsCurrent && x.State == EnrollmentState.Active)
            .OrderByDescending(x => x.EnrollmentDate).FirstOrDefaultAsync(ct);
        if (enrollment == null) return NotFound<IReadOnlyList<RoutineEntryDto>>();
        var ids = await _registrations.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
            x.StudentEnrollmentId == enrollment.Id && x.State == SubjectRegistrationState.Approved)
            .Select(x => x.SubjectOfferingId).ToArrayAsync(ct);
        if (ids.Length == 0)
            return ApiResponse<IReadOnlyList<RoutineEntryDto>>.SuccessResponse(Array.Empty<RoutineEntryDto>());
        var tenant = _user.TenantId;
        var rows = await (from routine in _routineEntries.GetQueryable().AsNoTracking()
            join offering in _offerings.GetQueryable().AsNoTracking() on routine.SubjectOfferingId equals offering.Id
            join item in _curriculumSubjects.GetQueryable().AsNoTracking() on offering.CurriculumSubjectId equals item.Id
            join subject in _subjects.GetQueryable().AsNoTracking() on item.SubjectId equals subject.Id
            join slot in _timeSlots.GetQueryable().AsNoTracking() on routine.RoutineTimeSlotId equals slot.Id
            where routine.TenantId == tenant && offering.TenantId == tenant && item.TenantId == tenant &&
                subject.TenantId == tenant && slot.TenantId == tenant &&
                routine.IsActive && offering.IsActive && ids.Contains(offering.Id)
            orderby routine.DayOfWeek, slot.StartTime, subject.Name
            select new { Routine = routine, Offering = offering, SubjectName = subject.Name, TimeSlotName = slot.Name })
            .Take(300).ToListAsync(ct);
        var roomIds = rows.Where(x => x.Routine.RoomId.HasValue).Select(x => x.Routine.RoomId!.Value).Distinct().ToArray();
        var rooms = await _rooms.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            roomIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        IReadOnlyList<RoutineEntryDto> result = rows.Select(x => new RoutineEntryDto
        {
            Id = x.Routine.Id, SubjectOfferingId = x.Offering.Id,
            SubjectOfferingReference = x.Offering.PublicId, SubjectName = x.SubjectName,
            RoutineTimeSlotId = x.Routine.RoutineTimeSlotId, TimeSlotName = x.TimeSlotName,
            RoomId = x.Routine.RoomId, RoomName = x.Routine.RoomId.HasValue
                ? rooms.GetValueOrDefault(x.Routine.RoomId.Value) : null,
            DayOfWeek = x.Routine.DayOfWeek, EffectiveFrom = x.Routine.EffectiveFrom,
            EffectiveTo = x.Routine.EffectiveTo, IsActive = x.Routine.IsActive,
            RowVersion = Convert.ToBase64String(x.Routine.RowVersion)
        }).ToList();
        return ApiResponse<IReadOnlyList<RoutineEntryDto>>.SuccessResponse(result);
    }

    private async Task<bool> OfferingMatchesSubjectAsync(long offeringId, long subjectId, CancellationToken ct) =>
        await (from offering in _offerings.GetQueryable().AsNoTracking()
            join item in _curriculumSubjects.GetQueryable().AsNoTracking() on offering.CurriculumSubjectId equals item.Id
            where offering.TenantId == _user.TenantId && item.TenantId == _user.TenantId &&
                offering.Id == offeringId && item.SubjectId == subjectId
            select offering.Id).AnyAsync(ct);

    private async Task<Student?> AuthorizedStudentAsync(Guid reference, CancellationToken ct)
    {
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == reference && x.StatusCode == "Active" && !x.IsDeleted, ct);
        return student != null && await CanAccessStudentAsync(student, ct) ? student : null;
    }

    private async Task<bool> CanAccessStudentAsync(Student student, CancellationToken ct)
    {
        if (CanManage()) return true;
        if (!CanSelfServe()) return false;
        if (student.UserId == _user.UserId) return true;
        return await (from link in _studentGuardians.GetQueryable().AsNoTracking()
            join guardian in _guardians.GetQueryable().AsNoTracking() on link.GuardianId equals guardian.Id
            where link.TenantId == _user.TenantId && guardian.TenantId == _user.TenantId &&
                link.StudentId == student.Id && guardian.UserId == _user.UserId && guardian.IsActive
            select link.Id).AnyAsync(ct);
    }

    private async Task<StudentEnrollmentDto> BuildEnrollmentDtoAsync(StudentEnrollment entity,
        Student student, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var batchName = await _batches.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.Id == entity.AcademicBatchId).Select(x => x.Name).FirstOrDefaultAsync(ct);
        var curriculumName = await _curricula.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.Id == entity.AcademicCurriculumId).Select(x => x.Name).FirstOrDefaultAsync(ct);
        return new StudentEnrollmentDto
        {
            Id = entity.Id, Reference = entity.PublicId, StudentReference = student.PublicId,
            StudentCode = student.StudentCode, StudentName = student.FullName,
            CampusId = entity.CampusId, AcademicYearId = entity.AcademicYearId, AcademicTermId = entity.AcademicTermId,
            AcademicProgramId = entity.AcademicProgramId, AcademicLevelId = entity.AcademicLevelId,
            AcademicBatchId = entity.AcademicBatchId, AcademicBatchName = batchName ?? string.Empty,
            AcademicCurriculumId = entity.AcademicCurriculumId, AcademicCurriculumName = curriculumName ?? string.Empty,
            RollNo = entity.RollNo, EnrollmentDate = entity.EnrollmentDate, State = entity.State,
            AcademicTrackId = entity.AcademicTrackId, MediumId = entity.MediumId, ShiftId = entity.ShiftId,
            Remarks = entity.Remarks, IsCurrent = entity.IsCurrent,
            RowVersion = Convert.ToBase64String(entity.RowVersion)
        };
    }

    private async Task<IReadOnlyList<StudentSubjectRegistrationDto>> GetRegistrationDtosAsync(long id, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var rows = await (from registration in _registrations.GetQueryable().AsNoTracking()
            join offering in _offerings.GetQueryable().AsNoTracking() on registration.SubjectOfferingId equals offering.Id
            join item in _curriculumSubjects.GetQueryable().AsNoTracking() on offering.CurriculumSubjectId equals item.Id
            join subject in _subjects.GetQueryable().AsNoTracking() on item.SubjectId equals subject.Id
            where registration.TenantId == tenant && offering.TenantId == tenant &&
                item.TenantId == tenant && subject.TenantId == tenant &&
                registration.StudentEnrollmentId == id
            orderby item.IsOptional, subject.Name
            select new { Registration = registration, SubjectCode = subject.Code, SubjectName = subject.Name })
            .Take(300).ToListAsync(ct);
        return rows.Select(x => new StudentSubjectRegistrationDto
        {
            Id = x.Registration.Id, StudentEnrollmentId = x.Registration.StudentEnrollmentId,
            SubjectOfferingId = x.Registration.SubjectOfferingId,
            SubjectCode = x.SubjectCode, SubjectName = x.SubjectName, State = x.Registration.State,
            RegisteredAt = x.Registration.RegisteredAt, ApprovedByUserId = x.Registration.ApprovedByUserId,
            ApprovedAt = x.Registration.ApprovedAt, Remarks = x.Registration.Remarks,
            RowVersion = Convert.ToBase64String(x.Registration.RowVersion)
        }).ToList();
    }

    private async Task<ApiResponse<T>> ExecuteWriteAsync<T>(string operation, Func<Task<ApiResponse<T>>> action)
    {
        try
        {
            return await _uow.ExecuteInTransactionAsync(_ => action());
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Academic enrollment concurrency failure during {Operation} tenant {TenantId}", operation, _user.TenantId);
            return Error<T>("Enrollment changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Academic enrollment conflict during {Operation} tenant {TenantId}", operation, _user.TenantId);
            return Error<T>("Enrollment conflicts with another request.", 409);
        }
    }

    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0 && (CanManage() || CanSelfServe());
    private bool CanSelfServe() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsInRole("Student") || _user.IsInRole("Guardian") || _user.IsInRole("Parent"));
    private bool CanManage() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("VicePrincipal"));
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool TryVersion(string? encoded, out byte[] version)
    {
        version = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(encoded)) return false;
        try { version = Convert.FromBase64String(encoded); return version.Length > 0; }
        catch (FormatException) { return false; }
    }
    private static bool VersionsMatch(byte[] actual, byte[] expected) =>
        actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    private static ApiResponse<T> Created<T>(T data, string message) => new()
    { Success = true, StatusCode = 201, Message = message, Data = data };
    private static ApiResponse<T> Error<T>(string message, int code = 400) => ApiResponse<T>.ErrorResponse(message, code);
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Academic enrollment permission required.", 403);
    private static ApiResponse<T> NotFound<T>() => ApiResponse<T>.ErrorResponse("Academic enrollment not found.", 404);
}
