using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Exams;

public sealed partial class ExamWorkflowService
{
    public async Task<ApiResponse<AssessmentDto>> SaveAssessmentAsync(Guid? reference,
        SaveAssessmentRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<AssessmentDto>("Assessment management permission required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.CampusId <= 0 ||
            request.AcademicYearId <= 0 || request.AcademicTermId is <= 0 || request.GradeSchemeId is <= 0 ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 150 ||
            string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 50 ||
            request.StartDate == default || request.EndDate < request.StartDate ||
            request.Remarks?.Length > 1000 || !Enum.IsDefined(request.Type))
            return Error<AssessmentDto>("Invalid assessment name, scope, period or type.");
        return await WriteAsync("save assessment", async token =>
        {
            var tenant = _user.TenantId;
            var campus = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.CampusId && x.IsActive && !x.IsDeleted, token);
            var year = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicYearId && x.IsActive && !x.IsDeleted, token);
            if (campus == null || year == null) return Error<AssessmentDto>("Campus or academic year not found.", 404);
            if (request.StartDate < year.StartDate || request.EndDate > year.EndDate)
                return Error<AssessmentDto>("Assessment dates exceed academic year.", 409);
            if (request.AcademicTermId.HasValue)
            {
                var term = await _terms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.AcademicTermId &&
                    x.AcademicYearId == year.Id && x.IsActive && !x.IsDeleted, token);
                if (term == null || request.StartDate < term.StartDate || request.EndDate > term.EndDate)
                    return Error<AssessmentDto>("Assessment is outside the academic term.", 409);
            }
            if (request.GradeSchemeId.HasValue && !await _gradeSchemes.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenant && x.Id == request.GradeSchemeId.Value &&
                    x.IsActive && !x.IsDeleted, token))
                return Error<AssessmentDto>("Grade scheme not found.", 404);
            var row = reference.HasValue ? await _assessments.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == reference && !x.IsDeleted, token) : null;
            if (reference.HasValue && row == null) return Error<AssessmentDto>("Assessment not found.", 404);
            if (row != null && !Matches(row.RowVersion, request.RowVersion))
                return Error<AssessmentDto>("Assessment was changed; reload and retry.", 409);
            if (row != null && row.State != AssessmentState.Draft)
                return Error<AssessmentDto>("Only draft assessment can be edited.", 409);
            var code = request.Code.Trim().ToUpperInvariant();
            var existing = await _assessments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Code == code && x.VersionNo == 1 && !x.IsDeleted, token);
            if (existing != null && existing.Id != row?.Id)
            {
                if (reference.HasValue || existing.CampusId != request.CampusId ||
                    existing.AcademicYearId != request.AcademicYearId ||
                    existing.AcademicTermId != request.AcademicTermId || existing.GradeSchemeId != request.GradeSchemeId ||
                    existing.Name != request.Name.Trim() || existing.StartDate != request.StartDate ||
                    existing.EndDate != request.EndDate || existing.Type != request.Type ||
                    existing.Remarks != Trim(request.Remarks))
                    return Error<AssessmentDto>("Assessment code already belongs to another definition.", 409);
                return ApiResponse<AssessmentDto>.SuccessResponse(await MapAssessmentAsync(existing, token),
                    "Assessment already exists.");
            }
            var now = _clock.GetUtcNow().UtcDateTime;
            if (row == null)
            {
                row = new Assessment
                {
                    TenantId = tenant, PublicId = Guid.NewGuid(), VersionNo = 1,
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _assessments.AddAsync(row);
            }
            else
            {
                row.UpdatedAt = now; row.UpdatedBy = _user.UserId; _assessments.Update(row);
            }
            row.Name = request.Name.Trim(); row.Code = code;
            row.CampusId = campus.Id; row.AcademicYearId = year.Id;
            row.AcademicTermId = request.AcademicTermId; row.GradeSchemeId = request.GradeSchemeId;
            row.Type = request.Type; row.StartDate = request.StartDate;
            row.EndDate = request.EndDate; row.Remarks = Trim(request.Remarks);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<AssessmentDto>.SuccessResponse(await MapAssessmentAsync(row, token),
                "Assessment saved.");
        }, ct);
    }

    public async Task<ApiResponse<AssessmentDto>> GetAssessmentAsync(Guid reference, CancellationToken ct = default)
    {
        if (!CanRead()) return Error<AssessmentDto>("Assessment access denied.", 403);
        if (reference == Guid.Empty) return Error<AssessmentDto>("Assessment not found.", 404);
        var row = await _assessments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
        if (row == null) return Error<AssessmentDto>("Assessment not found.", 404);
        if (!CanPublish())
        {
            var teacherId = await OwnTeacherIdAsync(ct);
            if (teacherId == 0) return Error<AssessmentDto>("Assessment not found.", 404);
            var allowed = await (from subject in _subjects.GetQueryable().AsNoTracking()
                join assigned in _instructors.GetQueryable().AsNoTracking()
                    on subject.SubjectOfferingId equals assigned.SubjectOfferingId
                where subject.TenantId == _user.TenantId && assigned.TenantId == _user.TenantId &&
                    subject.AssessmentId == row.Id && assigned.EmployeeId == teacherId && assigned.IsActive
                select subject.Id).AnyAsync(ct);
            if (!allowed) return Error<AssessmentDto>("Assessment not found.", 404);
        }
        return ApiResponse<AssessmentDto>.SuccessResponse(await MapAssessmentAsync(row, ct));
    }

    public Task<ApiResponse<AssessmentDto>> ChangeAssessmentStateAsync(Guid reference,
        ChangeAssessmentStateRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Task.FromResult(Error<AssessmentDto>("Assessment management permission required.", 403));
        if (reference == Guid.Empty || request == null || !TryVersion(request.RowVersion, out var expected) ||
            !Enum.IsDefined(request.State))
            return Task.FromResult(Error<AssessmentDto>("Assessment reference, state and row version required."));
        return WriteAsync("assessment state transition", async token =>
        {
            var row = await _assessments.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _user.TenantId && x.PublicId == reference && !x.IsDeleted, token);
            if (row == null) return Error<AssessmentDto>("Assessment not found.", 404);
            if (!Matches(row.RowVersion, expected)) return Error<AssessmentDto>("Assessment changed; reload.", 409);
            if (row.State == request.State)
                return ApiResponse<AssessmentDto>.SuccessResponse(await MapAssessmentAsync(row, token),
                    "Assessment state is unchanged.");
            var allowed = (row.State, request.State) switch
            {
                (AssessmentState.Draft, AssessmentState.Scheduled) => true,
                (AssessmentState.Scheduled, AssessmentState.InProgress) => true,
                (AssessmentState.InProgress, AssessmentState.MarksEntry) => true,
                (AssessmentState.MarksEntry, AssessmentState.Locked) => true,
                (AssessmentState.Draft, AssessmentState.Cancelled) => true,
                (AssessmentState.Scheduled, AssessmentState.Cancelled) => true,
                _ => false
            };
            if (!allowed) return Error<AssessmentDto>("Assessment status transition is forbidden.", 409);
            if (request.State == AssessmentState.Locked)
            {
                var offerings = await _subjects.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == _user.TenantId && x.AssessmentId == row.Id && !x.IsDeleted)
                    .Select(x => x.Id).ToArrayAsync(token);
                if (offerings.Length == 0) return Error<AssessmentDto>("Assessment has no subjects.", 409);
            }
            row.State = request.State;
            row.UpdatedAt = _clock.GetUtcNow().UtcDateTime; row.UpdatedBy = _user.UserId;
            _assessments.Update(row); await _uow.SaveChangesAsync(token);
            return ApiResponse<AssessmentDto>.SuccessResponse(await MapAssessmentAsync(row, token),
                "Assessment status updated.");
        }, ct);
    }

    public Task<ApiResponse<AssessmentSubjectDto>> SaveAssessmentSubjectAsync(long? subjectId,
        SaveAssessmentSubjectRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Task.FromResult(Error<AssessmentSubjectDto>("Assessment management permission required.", 403));
        if (request == null || request.AssessmentReference == Guid.Empty ||
            request.SubjectOfferingReference == Guid.Empty || subjectId is <= 0 ||
            request.FullMarks <= 0 || request.PassMarks < 0 || request.PassMarks > request.FullMarks ||
            request.Weightage is <= 0 or > 100)
            return Task.FromResult(Error<AssessmentSubjectDto>("Invalid assessment subject or marking scheme."));
        return WriteAsync("save assessment subject", async token =>
        {
            var tenant = _user.TenantId;
            var exam = await _assessments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.AssessmentReference && !x.IsDeleted, token);
            if (exam == null || exam.State != AssessmentState.Draft)
                return Error<AssessmentSubjectDto>("Assessment must exist in Draft state.", 409);
            var offering = await _offerings.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.SubjectOfferingReference &&
                x.AcademicYearId == exam.AcademicYearId && x.IsActive && !x.IsDeleted, token);
            if (offering == null) return Error<AssessmentSubjectDto>("Subject offering not found.", 404);
            var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == offering.AcademicBatchId &&
                x.CampusId == exam.CampusId && x.AcademicYearId == exam.AcademicYearId && x.IsActive, token);
            if (batch == null) return Error<AssessmentSubjectDto>("Offering is outside assessment scope.", 409);
            var row = subjectId.HasValue ? await _subjects.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == subjectId.Value && x.AssessmentId == exam.Id && !x.IsDeleted, token) : null;
            if (subjectId.HasValue && row == null) return Error<AssessmentSubjectDto>("Assessment subject not found.", 404);
            if (row != null && !Matches(row.RowVersion, request.RowVersion))
                return Error<AssessmentSubjectDto>("Assessment subject changed.", 409);
            if (await _subjects.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AssessmentId == exam.Id && x.SubjectOfferingId == offering.Id &&
                !x.IsDeleted && (!subjectId.HasValue || x.Id != subjectId), token))
                return Error<AssessmentSubjectDto>("Subject offering already configured.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            if (row == null)
            {
                row = new AssessmentSubject { TenantId = tenant, AssessmentId = exam.Id,
                    SubjectOfferingId = offering.Id, CreatedAt = now, CreatedBy = _user.UserId };
                await _subjects.AddAsync(row);
            }
            else
            {
                row.SubjectOfferingId = offering.Id; row.UpdatedAt = now;
                row.UpdatedBy = _user.UserId; _subjects.Update(row);
            }
            row.FullMarks = Round(request.FullMarks); row.PassMarks = Round(request.PassMarks);
            row.Weightage = Round(request.Weightage);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<AssessmentSubjectDto>.SuccessResponse(
                await MapSubjectAsync(row, token), "Assessment subject saved.");
        }, ct);
    }

    public Task<ApiResponse<AssessmentScheduleDto>> SaveAssessmentScheduleAsync(long? scheduleId,
        SaveAssessmentScheduleRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Task.FromResult(Error<AssessmentScheduleDto>("Assessment management permission required.", 403));
        if (request == null || request.AssessmentSubjectId <= 0 || scheduleId is <= 0 ||
            request.AssessmentDate == default || request.StartTime.HasValue != request.EndTime.HasValue ||
            request.StartTime.HasValue && request.StartTime >= request.EndTime ||
            request.RoomId is <= 0 || request.Instructions?.Length > 1000)
            return Task.FromResult(Error<AssessmentScheduleDto>("Invalid assessment schedule."));
        return WriteAsync("save assessment schedule", async token =>
        {
            var tenant = _user.TenantId;
            var sub = await _subjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AssessmentSubjectId && !x.IsDeleted, token);
            var exam = sub == null ? null : await _assessments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == sub.AssessmentId && !x.IsDeleted, token);
            if (exam == null || exam.State is not (AssessmentState.Draft or AssessmentState.Scheduled))
                return Error<AssessmentScheduleDto>("Assessment cannot be scheduled now.", 409);
            if (request.AssessmentDate < exam.StartDate || request.AssessmentDate > exam.EndDate)
                return Error<AssessmentScheduleDto>("Schedule date is outside assessment period.", 409);
            var offering = await _offerings.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == sub!.SubjectOfferingId, token);
            var batch = offering == null ? null : await _batches.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == offering.AcademicBatchId, token);
            if (batch == null) return Error<AssessmentScheduleDto>("Subject batch not found.", 409);
            if (request.RoomId.HasValue && !await _rooms.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == request.RoomId.Value &&
                x.CampusId == batch.CampusId && x.IsActive && !x.IsDeleted, token))
                return Error<AssessmentScheduleDto>("Room is not available in assessment campus.", 409);
            var row = scheduleId.HasValue ? await _schedules.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == scheduleId.Value && x.AssessmentSubjectId == sub.Id &&
                !x.IsDeleted, token) : null;
            if (scheduleId.HasValue && row == null) return Error<AssessmentScheduleDto>("Schedule not found.", 404);
            if (row != null && !Matches(row.RowVersion, request.RowVersion))
                return Error<AssessmentScheduleDto>("Schedule changed; reload and retry.", 409);
            var clashes = await _schedules.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AssessmentSubjectId == sub.Id &&
                !x.IsDeleted && (!scheduleId.HasValue || x.Id != scheduleId.Value))
                .AnyAsync(x => x.AssessmentDate == request.AssessmentDate, token);
            if (clashes) return Error<AssessmentScheduleDto>("A schedule already exists on this date.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            if (row == null)
            {
                row = new AssessmentSchedule
                {
                    TenantId = tenant, AssessmentSubjectId = sub.Id, CreatedAt = now,
                    CreatedBy = _user.UserId
                };
                await _schedules.AddAsync(row);
            }
            else
            {
                row.UpdatedAt = now; row.UpdatedBy = _user.UserId; _schedules.Update(row);
            }
            row.AssessmentDate = request.AssessmentDate; row.StartTime = request.StartTime;
            row.EndTime = request.EndTime; row.RoomId = request.RoomId;
            row.Instructions = Trim(request.Instructions);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<AssessmentScheduleDto>.SuccessResponse(
                await MapScheduleAsync(row, token), "Assessment schedule saved.");
        }, ct);
    }

    public Task<ApiResponse<GradeSchemeDto>> SaveGradeSchemeAsync(long? gradeSchemeId,
        SaveGradeSchemeRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Task.FromResult(Error<GradeSchemeDto>("Assessment management permission required.", 403));
        if (request == null || gradeSchemeId is <= 0 || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 150 || string.IsNullOrWhiteSpace(request.Code) ||
            request.Code.Trim().Length > 50 || request.Rules == null ||
            request.Rules.Count is < 1 or > 100 || request.Rules.Any(x =>
                x.MinMarks < 0 || x.MaxMarks > 100 || x.MinMarks > x.MaxMarks ||
                x.GradeLetter.Length is < 1 or > 20 || x.GradePoint is < 0 or > 10 ||
                x.Id.HasValue || x.RowVersion != null))
            return Task.FromResult(Error<GradeSchemeDto>("Invalid grade scheme or grade rules."));
        return WriteAsync("save grade scheme", async token =>
        {
            var tenant = _user.TenantId;
            if (request.AcademicProgramId.HasValue && !await _batches.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenant && x.AcademicProgramId == request.AcademicProgramId &&
                    x.IsActive, token))
                return Error<GradeSchemeDto>("Academic program has no active batches.", 409);
            var code = request.Code.Trim().ToUpperInvariant();
            if (await _gradeSchemes.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Code == code && !x.IsDeleted &&
                (!gradeSchemeId.HasValue || x.Id != gradeSchemeId.Value), token))
                return Error<GradeSchemeDto>("Grade scheme code already exists.", 409);
            var row = gradeSchemeId.HasValue ? await _gradeSchemes.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == gradeSchemeId && !x.IsDeleted, token) : null;
            if (gradeSchemeId.HasValue && row == null) return Error<GradeSchemeDto>("Grade scheme not found.", 404);
            if (row != null) return Error<GradeSchemeDto>("Existing grading schemes require a controlled versioned amendment.", 409);
            var ordered = request.Rules.OrderBy(x => x.MinMarks).ToArray();
            if (ordered[0].MinMarks != 0 || ordered[^1].MaxMarks != 100 ||
                ordered.Select(x => x.GradeLetter.Trim().ToUpperInvariant()).Distinct().Count() != ordered.Length ||
                ordered.Skip(1).Select((x, i) => (x, prev: ordered[i])).Any(x =>
                    x.x.MinMarks <= x.prev.MaxMarks || x.x.MinMarks - x.prev.MaxMarks > 0.01m))
                return Error<GradeSchemeDto>("Grade intervals must cover 0–100 without overlaps or gaps.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            row = new GradeScheme
            {
                TenantId = tenant, Name = request.Name.Trim(), Code = code,
                AcademicProgramId = request.AcademicProgramId, IsDefault = request.IsDefault,
                IsActive = request.IsActive, CreatedAt = now, CreatedBy = _user.UserId
            };
            if (request.IsDefault && await _gradeSchemes.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicProgramId == row.AcademicProgramId &&
                x.IsDefault && x.IsActive && !x.IsDeleted, token))
                return Error<GradeSchemeDto>("A default grade scheme already exists.", 409);
            await _gradeSchemes.AddAsync(row); await _uow.SaveChangesAsync(token);
            var grades = ordered.Select((x, i) => new GradeRule
            {
                TenantId = tenant, GradeSchemeId = row.Id, MinMarks = x.MinMarks,
                MaxMarks = x.MaxMarks, GradeLetter = x.GradeLetter.Trim(),
                GradePoint = x.GradePoint, IsFailGrade = x.IsFailGrade,
                DisplayOrder = x.DisplayOrder == 0 ? i + 1 : x.DisplayOrder,
                CreatedAt = now, CreatedBy = _user.UserId
            }).ToList();
            foreach (var rule in grades) await _grades.AddAsync(rule);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<GradeSchemeDto>.SuccessResponse(MapGradeScheme(row, grades),
                "Grade scheme created.");
        }, ct);
    }

    private async Task<AssessmentDto> MapAssessmentAsync(Assessment row, CancellationToken ct)
    {
        var campusName = await _campuses.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == row.TenantId && x.Id == row.CampusId)
            .Select(x => x.Name).FirstOrDefaultAsync(ct);
        var yearName = await _years.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == row.TenantId && x.Id == row.AcademicYearId)
            .Select(x => x.Name).FirstOrDefaultAsync(ct);
        var termName = row.AcademicTermId.HasValue ? await _terms.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == row.TenantId && x.Id == row.AcademicTermId)
            .Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var subjects = await _subjects.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == row.TenantId && x.AssessmentId == row.Id && !x.IsDeleted)
            .OrderBy(x => x.Id).Take(500).ToListAsync(ct);
        var mapped = new List<AssessmentSubjectDto>(subjects.Count);
        foreach (var x in subjects) mapped.Add(await MapSubjectAsync(x, ct));
        return new AssessmentDto
        {
            Id = row.Id, Reference = row.PublicId, CampusId = row.CampusId,
            CampusName = campusName ?? string.Empty, AcademicYearId = row.AcademicYearId,
            AcademicYearName = yearName ?? string.Empty, AcademicTermId = row.AcademicTermId,
            AcademicTermName = termName, GradeSchemeId = row.GradeSchemeId, Name = row.Name, Code = row.Code,
            Type = row.Type, State = row.State, StartDate = row.StartDate, EndDate = row.EndDate,
            Remarks = row.Remarks, RowVersion = Version(row.RowVersion), Subjects = mapped
        };
    }

    private async Task<AssessmentSubjectDto> MapSubjectAsync(AssessmentSubject row, CancellationToken ct)
    {
        var info = await (from offering in _offerings.GetQueryable().AsNoTracking()
            join item in _curriculumSubjects.GetQueryable().AsNoTracking() on offering.CurriculumSubjectId equals item.Id
            join subject in _subjectNames.GetQueryable().AsNoTracking() on item.SubjectId equals subject.Id
            where offering.TenantId == row.TenantId && item.TenantId == row.TenantId &&
                subject.TenantId == row.TenantId && offering.Id == row.SubjectOfferingId
            select new { offering.PublicId, subject.Code, subject.Name }).FirstOrDefaultAsync(ct);
        return new AssessmentSubjectDto
        {
            Id = row.Id, SubjectOfferingReference = info?.PublicId ?? Guid.Empty,
            SubjectCode = info?.Code ?? string.Empty, SubjectName = info?.Name ?? string.Empty,
            FullMarks = row.FullMarks, PassMarks = row.PassMarks,
            Weightage = row.Weightage, RowVersion = Version(row.RowVersion)
        };
    }

    private async Task<AssessmentScheduleDto> MapScheduleAsync(AssessmentSchedule row, CancellationToken ct)
    {
        var info = await (from sub in _subjects.GetQueryable().AsNoTracking()
            join offering in _offerings.GetQueryable().AsNoTracking() on sub.SubjectOfferingId equals offering.Id
            join item in _curriculumSubjects.GetQueryable().AsNoTracking() on offering.CurriculumSubjectId equals item.Id
            join subject in _subjectNames.GetQueryable().AsNoTracking() on item.SubjectId equals subject.Id
            where sub.TenantId == row.TenantId && offering.TenantId == row.TenantId &&
                item.TenantId == row.TenantId && subject.TenantId == row.TenantId &&
                sub.Id == row.AssessmentSubjectId
            select subject.Name).FirstOrDefaultAsync(ct);
        var roomName = row.RoomId.HasValue ? await _rooms.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == row.TenantId && x.Id == row.RoomId)
            .Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        return new AssessmentScheduleDto
        {
            Id = row.Id, AssessmentSubjectId = row.AssessmentSubjectId,
            SubjectName = info ?? string.Empty, AssessmentDate = row.AssessmentDate,
            StartTime = row.StartTime, EndTime = row.EndTime, RoomId = row.RoomId,
            RoomName = roomName, Instructions = row.Instructions,
            RowVersion = Version(row.RowVersion)
        };
    }

    private static GradeSchemeDto MapGradeScheme(GradeScheme row, IReadOnlyList<GradeRule> rules) => new()
    {
        Id = row.Id, Name = row.Name, Code = row.Code,
        AcademicProgramId = row.AcademicProgramId,
        IsDefault = row.IsDefault, IsActive = row.IsActive,
        RowVersion = Version(row.RowVersion),
        Rules = rules.Select(x => new GradeRuleDto
        {
            Id = x.Id, MinMarks = x.MinMarks, MaxMarks = x.MaxMarks,
            GradeLetter = x.GradeLetter, GradePoint = x.GradePoint,
            IsFailGrade = x.IsFailGrade, DisplayOrder = x.DisplayOrder,
            RowVersion = Version(x.RowVersion)
        }).ToList()
    };
}
