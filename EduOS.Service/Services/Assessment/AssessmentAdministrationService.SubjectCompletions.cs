using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.AssessmentAdministration;
public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<IReadOnlyList<SubjectCompletionBatchDto>>> GetSubjectCompletionBatchesAsync(
        CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<IReadOnlyList<SubjectCompletionBatchDto>>("Completion permission required.", 403);
        var tenant = _user.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var batches = await (from batch in _batches.GetQueryable().AsNoTracking()
            join year in _years.GetQueryable().AsNoTracking() on batch.AcademicYearId equals year.Id
            where batch.TenantId == tenant && year.TenantId == tenant &&
                !batch.IsDeleted && !year.IsDeleted && year.EndDate < today &&
                (!year.CampusId.HasValue || year.CampusId == batch.CampusId)
            orderby year.EndDate descending, batch.Name
            select new SubjectCompletionBatchDto
            {
                Id = batch.Id, Name = batch.Name, AcademicYearName = year.Name,
                CampusId = batch.CampusId, AcademicYearEnd = year.EndDate
            }).Take(500).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<SubjectCompletionBatchDto>>.SuccessResponse(batches);
    }

    public async Task<ApiResponse<SubjectCompletionPageDto>> SearchSubjectCompletionsAsync(
        SubjectCompletionQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<SubjectCompletionPageDto>("Completion permission required.", 403);
        if (request == null || request.AcademicBatchId <= 0 || request.Page is < 1 or > 1000000 ||
            request.PageSize is < 1 or > 50 || request.Search?.Length > 100)
            return Error<SubjectCompletionPageDto>("Invalid batch, search or page.");
        var tenant = _user.TenantId;
        var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == request.AcademicBatchId && !x.IsDeleted, ct);
        if (batch == null) return Error<SubjectCompletionPageDto>("Batch not found.", 404);
        var year = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == batch.AcademicYearId && !x.IsDeleted, ct);
        if (year == null || year.CampusId.HasValue && year.CampusId != batch.CampusId)
            return Error<SubjectCompletionPageDto>("Batch campus or year invalid.", 409);
        var query = from registration in _registrations.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on registration.StudentEnrollmentId equals enrollment.Id
            join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
            join offering in _offerings.GetQueryable().AsNoTracking()
                on registration.SubjectOfferingId equals offering.Id
            join item in _curriculumSubjects.GetQueryable().AsNoTracking()
                on offering.CurriculumSubjectId equals item.Id
            join subject in _subjectNames.GetQueryable().AsNoTracking() on item.SubjectId equals subject.Id
            where registration.TenantId == tenant && enrollment.TenantId == tenant &&
                student.TenantId == tenant && offering.TenantId == tenant &&
                item.TenantId == tenant && subject.TenantId == tenant &&
                !registration.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted &&
                !offering.IsDeleted && !item.IsDeleted && !subject.IsDeleted &&
                enrollment.AcademicBatchId == batch.Id && enrollment.CampusId == batch.CampusId &&
                enrollment.AcademicYearId == batch.AcademicYearId &&
                offering.AcademicBatchId == batch.Id && offering.AcademicYearId == batch.AcademicYearId &&
                item.AcademicCurriculumId == enrollment.AcademicCurriculumId &&
                item.AcademicLevelId == enrollment.AcademicLevelId &&
                (registration.State == SubjectRegistrationState.Approved ||
                 registration.State == SubjectRegistrationState.Completed)
            select new { registration, enrollment, student, offering, subject };
        var search = request.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.student.FullName.Contains(search) ||
                x.student.StudentCode.Contains(search) || x.enrollment.RollNo.Contains(search) ||
                x.subject.Name.Contains(search) || x.subject.Code.Contains(search));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.enrollment.RollNo).ThenBy(x => x.subject.Name)
            .ThenBy(x => x.registration.Id).Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize).ToListAsync(ct);
        var items = new List<SubjectCompletionRowDto>();
        foreach (var x in rows)
        {
            var block = x.registration.State == SubjectRegistrationState.Completed ? "Completed" :
                await CompletionBlockAsync(x.registration, x.enrollment, x.offering, batch, year, ct);
            items.Add(new SubjectCompletionRowDto
            {
                RegistrationId = x.registration.Id, AcademicBatchId = batch.Id,
                RollNo = x.enrollment.RollNo, StudentCode = x.student.StudentCode,
                StudentName = x.student.FullName, SubjectCode = x.subject.Code,
                SubjectName = x.subject.Name, State = x.registration.State,
                CanComplete = block == null, StatusMessage = block ?? "Ready",
                RowVersion = Version(x.registration.RowVersion)
            });
        }
        return ApiResponse<SubjectCompletionPageDto>.SuccessResponse(new SubjectCompletionPageDto
        { Page = request.Page, PageSize = request.PageSize, TotalCount = count, Items = items });
    }

    public Task<ApiResponse<SubjectCompletionRowDto>> CompleteSubjectAsync(long id,
        CompleteSubjectRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Task.FromResult(Error<SubjectCompletionRowDto>("Completion permission required.", 403));
        if (id <= 0 || request == null || !TryVersion(request.RowVersion, out _) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
            return Task.FromResult(Error<SubjectCompletionRowDto>("Row version and audit reason required."));
        return WriteAsync("complete subject", async token =>
        {
            var tenant = _user.TenantId;
            var row = await _registrations.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == id && !x.IsDeleted, token);
            if (row == null) return Error<SubjectCompletionRowDto>("Registration not found.", 404);
            var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == row.StudentEnrollmentId && !x.IsDeleted, token);
            var offering = await _offerings.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == row.SubjectOfferingId && !x.IsDeleted, token);
            var batch = enrollment == null ? null : await _batches.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == enrollment.AcademicBatchId &&
                    !x.IsDeleted, token);
            var year = batch == null ? null : await _years.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == batch.AcademicYearId &&
                    !x.IsDeleted, token);
            var item = offering == null ? null : await _curriculumSubjects.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == offering.CurriculumSubjectId &&
                    !x.IsDeleted, token);
            var subject = item == null ? null : await _subjectNames.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == item.SubjectId &&
                    !x.IsDeleted, token);
            var student = enrollment == null ? null : await _students.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == enrollment.StudentId &&
                    !x.IsDeleted, token);
            if (enrollment == null || offering == null || batch == null || year == null ||
                item == null || subject == null || student == null ||
                offering.AcademicBatchId != batch.Id || offering.AcademicYearId != batch.AcademicYearId ||
                enrollment.CampusId != batch.CampusId || enrollment.AcademicYearId != batch.AcademicYearId ||
                item.AcademicCurriculumId != enrollment.AcademicCurriculumId ||
                item.AcademicLevelId != enrollment.AcademicLevelId ||
                year.CampusId.HasValue && year.CampusId != batch.CampusId)
                return Error<SubjectCompletionRowDto>("Registration curriculum, campus or year mismatch.", 409);
            SubjectCompletionRowDto Map() => new()
            {
                RegistrationId = row.Id, AcademicBatchId = batch.Id,
                RollNo = enrollment.RollNo, StudentCode = student.StudentCode,
                StudentName = student.FullName, SubjectCode = subject.Code, SubjectName = subject.Name,
                State = row.State, CanComplete = false, StatusMessage = "Completed",
                RowVersion = Version(row.RowVersion)
            };
            if (row.State == SubjectRegistrationState.Completed)
                return ApiResponse<SubjectCompletionRowDto>.SuccessResponse(Map(), "Subject already completed.");
            if (row.State != SubjectRegistrationState.Approved)
                return Error<SubjectCompletionRowDto>("Only approved subjects can be completed.", 409);
            if (!Matches(row.RowVersion, request.RowVersion))
                return Error<SubjectCompletionRowDto>("Registration changed; refresh.", 409);
            var block = await CompletionBlockAsync(row, enrollment, offering, batch, year, token);
            if (block != null) return Error<SubjectCompletionRowDto>(block, 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            row.State = SubjectRegistrationState.Completed;
            row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
            _registrations.Update(row);
            await RecordAuditAsync("Complete", "StudentSubjectRegistration", row.Id, request.Reason.Trim(), now);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<SubjectCompletionRowDto>.SuccessResponse(Map(), "Subject completed.");
        }, ct);
    }

    private async Task<string?> CompletionBlockAsync(StudentSubjectRegistration registration,
        StudentEnrollment enrollment, SubjectOffering offering, AcademicBatch batch, AcademicYear year,
        CancellationToken ct)
    {
        if (enrollment.State is not (EnrollmentState.Active or EnrollmentState.Completed or EnrollmentState.Promoted) ||
            offering.AcademicBatchId != batch.Id || offering.AcademicYearId != batch.AcademicYearId ||
            enrollment.EnrollmentDate < year.StartDate || enrollment.EnrollmentDate > year.EndDate ||
            batch.StartDate.HasValue && enrollment.EnrollmentDate < batch.StartDate.Value ||
            batch.EndDate.HasValue && enrollment.EnrollmentDate > batch.EndDate.Value)
            return "Enrollment or academic dates invalid.";
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        if (year.EndDate >= today) return "Academic year must end before completion.";
        var tenant = _user.TenantId;
        var exams = await (from sub in _subjects.GetQueryable().AsNoTracking()
            join exam in _assessments.GetQueryable().AsNoTracking() on sub.AssessmentId equals exam.Id
            where sub.TenantId == tenant && exam.TenantId == tenant &&
                sub.SubjectOfferingId == offering.Id && !sub.IsDeleted && !exam.IsDeleted &&
                exam.CampusId == batch.CampusId && exam.AcademicYearId == batch.AcademicYearId &&
                exam.State != AssessmentState.Cancelled
            select new { Sub = sub, Exam = exam }).Take(101).ToListAsync(ct);
        if (exams.Count > 100) return "Assessment evidence exceeds review limit.";
        var examIds = exams.Select(x => x.Exam.Id).Distinct().ToArray();
        var subIds = exams.Select(x => x.Sub.Id).ToArray();
        var pubs = await _publications.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AcademicBatchId == batch.Id &&
            examIds.Contains(x.AssessmentId) && !x.IsDeleted).ToListAsync(ct);
        var pubIds = pubs.Select(x => x.Id).ToArray();
        var summaries = await _summaries.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.StudentEnrollmentId == enrollment.Id &&
            pubIds.Contains(x.ResultPublicationId) && !x.IsDeleted).ToListAsync(ct);
        var marks = await _marks.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.StudentSubjectRegistrationId == registration.Id &&
            subIds.Contains(x.AssessmentSubjectId) && !x.IsDeleted).ToListAsync(ct);
        var components = await _components.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && subIds.Contains(x.AssessmentSubjectId) && !x.IsDeleted).ToListAsync(ct);
        var compIds = components.Select(x => x.Id).ToArray();
        var markIds = marks.Select(x => x.Id).ToArray();
        var componentMarks = await _componentMarks.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && compIds.Contains(x.AssessmentComponentId) &&
            markIds.Contains(x.StudentAssessmentMarkId) && !x.IsDeleted).ToListAsync(ct);
        var evidence = exams.Select(x =>
        {
            var released = pubs.Where(p => p.AssessmentId == x.Exam.Id &&
                p.State == ResultPublicationState.Published).ToList();
            var pub = released.Count == 1 ? released[0] : null;
            var snapshots = summaries.Where(s => pub != null &&
                s.ResultPublicationId == pub.Id && s.AssessmentId == x.Exam.Id &&
                s.PublicationVersionNo == pub.VersionNo).ToList();
            var summary = snapshots.Count == 1 ? snapshots[0] : null;
            var studentMarks = marks.Where(m => m.AssessmentSubjectId == x.Sub.Id).ToList();
            var mark = studentMarks.Count == 1 ? studentMarks[0] : null;
            var parts = components.Where(c => c.AssessmentSubjectId == x.Sub.Id).ToList();
            var validComponents = parts.Count == 0 || parts.Sum(c => c.Weightage) == 100m &&
                mark != null && parts.All(c =>
                {
                    var entries = componentMarks.Where(m => m.AssessmentComponentId == c.Id &&
                        m.StudentAssessmentMarkId == mark.Id).ToList();
                    return entries.Count == 1 && !entries[0].IsWithheld &&
                        (!c.IsMandatoryPass || !entries[0].IsAbsent &&
                         entries[0].ObtainedMarks >= c.PassMarks);
                });
            return new SubjectCompletionEvidence(
                released.Count == 1 && x.Exam.State is (AssessmentState.Locked or AssessmentState.Published),
                summary != null, summary?.IsWithheld == true || mark?.IsWithheld == true,
                mark != null, mark != null && !mark.IsAbsent &&
                    x.Sub.FullMarks > 0 && x.Sub.PassMarks >= 0 &&
                    mark.ObtainedMarks >= x.Sub.PassMarks && mark.ObtainedMarks <= x.Sub.FullMarks,
                validComponents);
        }).ToList();
        return SubjectCompletionPolicy.BlockReason(year.EndDate, today, evidence);
    }
}
