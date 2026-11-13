using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Academic;

public sealed partial class AcademicEnrollmentService
{
    public async Task<ApiResponse<IReadOnlyList<AcademicCurriculumDto>>> GetEligibleSectionCurriculaAsync(
        long sectionId, long academicYearId, DateOnly date, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<AcademicCurriculumDto>>();
        if (sectionId <= 0 || academicYearId <= 0 || date == default || _sections == null ||
            _uow is not EduOSDbContext db)
            return Error<IReadOnlyList<AcademicCurriculumDto>>("Select an academic year and class section.");
        var tenant = _user.TenantId;
        var section = await _sections.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == sectionId && x.IsActive && !x.IsDeleted, ct);
        var level = section == null ? null : await db.AcademicLevels.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == section.AcademicLevelId && x.IsActive && !x.IsDeleted, ct);
        var year = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == academicYearId && x.IsActive && !x.IsDeleted, ct);
        if (section == null || level == null || year == null ||
            year.CampusId.HasValue && year.CampusId != section.CampusId ||
            date < year.StartDate || date > year.EndDate ||
            !await db.Campuses.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == section.CampusId && x.IsActive && !x.IsDeleted, ct))
            return Error<IReadOnlyList<AcademicCurriculumDto>>("Class, campus or academic year is unavailable.", 409);
        var rows = await _curricula.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AcademicProgramId == level.AcademicProgramId &&
            x.IsCurrent && x.IsActive && !x.IsDeleted && x.EffectiveFrom <= date &&
            (!x.EffectiveTo.HasValue || x.EffectiveTo >= date))
            .OrderByDescending(x => x.VersionNo).ThenBy(x => x.Id).Take(40)
            .Select(x => new AcademicCurriculumDto
            {
                Id = x.Id, AcademicProgramId = x.AcademicProgramId,
                AcademicTrackId = x.AcademicTrackId, MediumId = x.MediumId,
                Name = x.Name, Code = x.Code, VersionNo = x.VersionNo,
                EffectiveFrom = x.EffectiveFrom, EffectiveTo = x.EffectiveTo,
                IsCurrent = x.IsCurrent, IsActive = x.IsActive,
                RowVersion = Convert.ToBase64String(x.RowVersion)
            }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<AcademicCurriculumDto>>.SuccessResponse(rows);
    }

    private Task<ApiResponse<StudentEnrollmentDto>> EnrollInSectionAsync(
        CreateStudentEnrollmentRequestDto request, CancellationToken ct)
    {
        if (request.ClientRequestId == Guid.Empty || request.StudentReference == Guid.Empty ||
            request.AcademicBatchId != null || request.AcademicSectionId is null or <= 0 ||
            request.AcademicYearId is null or <= 0 || request.AcademicTermId is <= 0 ||
            request.AcademicCurriculumId <= 0 || string.IsNullOrWhiteSpace(request.RollNo) ||
            request.RollNo.Trim().Length > 50 || request.Remarks?.Length > 500)
            return Task.FromResult(Error<StudentEnrollmentDto>(
                "Student, year, section, curriculum, roll and request ID are required."));
        if (_sections == null || _uow is not EduOSDbContext)
            return Task.FromResult(Error<StudentEnrollmentDto>("Academic section is unavailable.", 503));

        return ExecuteWriteAsync("enroll directly into section", async () =>
        {
            var db = (EduOSDbContext)_uow;
            var tenant = _user.TenantId;
            var roll = request.RollNo.Trim();
            var replay = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, ct);
            if (replay != null)
            {
                var linked = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == replay.StudentId && !x.IsDeleted, ct);
                if (linked == null || linked.PublicId != request.StudentReference ||
                    replay.AcademicBatchId != null || replay.AcademicYearId != request.AcademicYearId ||
                    replay.AcademicSectionId != request.AcademicSectionId ||
                    replay.AcademicTermId != request.AcademicTermId ||
                    replay.AcademicCurriculumId != request.AcademicCurriculumId ||
                    replay.RollNo != roll ||
                    request.EnrollmentDate != default && replay.EnrollmentDate != request.EnrollmentDate)
                    return Error<StudentEnrollmentDto>("Request ID already used with different data.", 409);
                return ApiResponse<StudentEnrollmentDto>.SuccessResponse(
                    await BuildEnrollmentDtoAsync(replay, linked, ct), "Enrollment already exists.");
            }

            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.StudentReference &&
                x.StatusCode == "Active" && !x.IsDeleted, ct);
            if (student == null) return NotFound<StudentEnrollmentDto>();
            var section = await _sections.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicSectionId!.Value &&
                x.IsActive && !x.IsDeleted, ct);
            if (section == null || section.Capacity <= 0)
                return Error<StudentEnrollmentDto>("Active section with capacity is required.", 409);
            var level = await db.AcademicLevels.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == section.AcademicLevelId &&
                x.IsActive && !x.IsDeleted, ct);
            var year = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicYearId!.Value &&
                x.IsActive && !x.IsDeleted, ct);
            var date = request.EnrollmentDate == default
                ? DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime) : request.EnrollmentDate;
            if (level == null || year == null ||
                year.CampusId.HasValue && year.CampusId != section.CampusId ||
                date < year.StartDate || date > year.EndDate ||
                !await db.Campuses.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == section.CampusId &&
                    x.IsActive && !x.IsDeleted, ct))
                return Error<StudentEnrollmentDto>("Invalid campus, class or academic period.", 409);
            if (request.AcademicTermId.HasValue)
            {
                var term = await db.AcademicTerms.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.AcademicTermId.Value &&
                    x.AcademicYearId == year.Id && x.IsActive && !x.IsDeleted, ct);
                if (term == null || date < term.StartDate || date > term.EndDate)
                    return Error<StudentEnrollmentDto>("Term does not contain enrollment date.", 409);
            }
            var curriculum = await _curricula.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicCurriculumId &&
                x.AcademicProgramId == level.AcademicProgramId && x.IsCurrent &&
                x.IsActive && !x.IsDeleted && x.EffectiveFrom <= date &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= date), ct);
            if (curriculum == null)
                return Error<StudentEnrollmentDto>("Current curriculum does not match this class.", 409);
            var campuses = await db.ProgramCampuses.AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AcademicProgramId == level.AcademicProgramId &&
                x.IsActive && !x.IsDeleted).Select(x => x.CampusId).ToListAsync(ct);
            if (campuses.Count > 0 && !campuses.Contains(section.CampusId))
                return Error<StudentEnrollmentDto>("Program is not offered at this campus.", 403);
            var current = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.StudentId == student.Id && x.IsCurrent &&
                x.State == EnrollmentState.Active && !x.IsDeleted, ct);
            if (current != null)
            {
                if (current.AcademicBatchId == null && current.AcademicSectionId == section.Id &&
                    current.AcademicYearId == year.Id && current.AcademicTermId == request.AcademicTermId &&
                    current.AcademicCurriculumId == curriculum.Id && current.RollNo == roll &&
                    current.EnrollmentDate == date)
                    return ApiResponse<StudentEnrollmentDto>.SuccessResponse(
                        await BuildEnrollmentDtoAsync(current, student, ct), "Student already enrolled.");
                return Error<StudentEnrollmentDto>("Student already has an active enrollment.", 409);
            }

            var required = await _curriculumSubjects.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AcademicCurriculumId == curriculum.Id &&
                x.AcademicLevelId == section.AcademicLevelId && x.IsActive &&
                !x.IsDeleted && !x.IsOptional).ToListAsync(ct);
            if (required.Count == 0)
                return Error<StudentEnrollmentDto>("Required curriculum subjects are missing.", 409);
            var requiredIds = required.Select(x => x.Id).ToArray();
            var offerings = await _offerings.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AcademicBatchId == null && x.AcademicYearId == year.Id &&
                x.AcademicTermId == request.AcademicTermId && x.AcademicSectionId == section.Id &&
                x.IsActive && !x.IsDeleted && requiredIds.Contains(x.CurriculumSubjectId))
                .ToListAsync(ct);
            if (offerings.GroupBy(x => x.CurriculumSubjectId).Any(g => g.Count() != 1) ||
                required.Any(x => offerings.All(y => y.CurriculumSubjectId != x.Id)))
                return Error<StudentEnrollmentDto>(
                    "Every required subject needs exactly one active Section offering.", 409);

            // The Section RowVersion update is the local capacity mutex.
            // All checks and registrations stay in the same serializable transaction.
            var locked = await _sections.GetQueryable().FirstAsync(x =>
                x.TenantId == tenant && x.Id == section.Id && x.IsActive && !x.IsDeleted, ct);
            locked.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            locked.UpdatedBy = _user.UserId;
            _sections.Update(locked);
            await _uow.SaveChangesAsync(ct);
            if (!await AcademicSectionPlacementPolicy.CapacityAvailableAsync(
                _enrollments.GetQueryable(), tenant, section.Id, locked.Capacity, ct))
                throw new BatchAllocationException("Academic section is at capacity.");
            if (await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicYearId == year.Id &&
                x.AcademicSectionId == section.Id && x.RollNo == roll &&
                x.IsCurrent && !x.IsDeleted, ct))
                throw new BatchAllocationException("Roll is already in use in this academic section/year.");
            foreach (var offering in offerings.OrderBy(x => x.Id))
                if (offering.Capacity <= 0 ||
                    await CountReservedSeatsAsync(offering.Id, ct) >= offering.Capacity)
                    throw new BatchAllocationException("A required subject offering is full.");

            var now = _clock.GetUtcNow().UtcDateTime;
            var enrollment = new StudentEnrollment
            {
                TenantId = tenant, PublicId = Guid.NewGuid(), ClientRequestId = request.ClientRequestId,
                StudentId = student.Id, CampusId = section.CampusId, AcademicYearId = year.Id,
                AcademicTermId = request.AcademicTermId, AcademicProgramId = level.AcademicProgramId,
                AcademicLevelId = section.AcademicLevelId, AcademicBatchId = null,
                AcademicSectionId = section.Id, AcademicCurriculumId = curriculum.Id,
                AcademicTrackId = curriculum.AcademicTrackId, MediumId = curriculum.MediumId,
                RollNo = roll, EnrollmentDate = date, State = EnrollmentState.Active,
                IsCurrent = true, Remarks = Trim(request.Remarks), CreatedAt = now, CreatedBy = _user.UserId
            };
            await _enrollments.AddAsync(enrollment);
            await _uow.SaveChangesAsync(ct);
            foreach (var offering in offerings)
            {
                var map = required.Single(x => x.Id == offering.CurriculumSubjectId);
                await _registrations.AddAsync(new StudentSubjectRegistration
                {
                    TenantId = tenant, ClientRequestId = Guid.NewGuid(),
                    StudentEnrollmentId = enrollment.Id, SubjectOfferingId = offering.Id,
                    State = SubjectRegistrationState.Approved, RegisteredAt = now,
                    CreditHoursSnapshot = map.CreditHours, ApprovedByUserId = _user.UserId,
                    ApprovedAt = now, CreatedAt = now, CreatedBy = _user.UserId
                });
            }
            await _uow.SaveChangesAsync(ct);
            return Created(await BuildEnrollmentDtoAsync(enrollment, student, ct),
                "Student enrolled without an academic batch.");
        }, IsolationLevel.Serializable);
    }
}
