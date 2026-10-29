using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<AssessmentReadinessPageDto>> GetResultReadinessAsync(
        AssessmentReadinessQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<AssessmentReadinessPageDto>("Result readiness permission required.", 403);
        if (request == null || request.Page is < 1 or > 100000 ||
            request.PageSize is < 1 or > 50)
            return Error<AssessmentReadinessPageDto>("Invalid readiness page.");
        var scope = await ScopeAsync(request, ct);
        if (scope == null) return Error<AssessmentReadinessPageDto>("Assessment or batch not found.", 404);
        var tenant = _user.TenantId;
        var batch = scope.Value.Batch;
        var roster = from enrollment in _enrollments.GetQueryable().AsNoTracking()
            join student in _students.GetQueryable().AsNoTracking()
                on enrollment.StudentId equals student.Id
            where enrollment.TenantId == tenant && student.TenantId == tenant &&
                enrollment.AcademicBatchId == batch.Id && enrollment.CampusId == batch.CampusId &&
                enrollment.AcademicYearId == batch.AcademicYearId &&
                enrollment.State == EnrollmentState.Active && enrollment.IsCurrent &&
                student.StatusCode == "Active" && !enrollment.IsDeleted && !student.IsDeleted
            select new { enrollment, student };
        if (scope.Value.Exam.AttemptMode != AssessmentAttemptMode.Regular)
        {
            if (_attemptRegistrations == null)
                return Error<AssessmentReadinessPageDto>("Attempt register is unavailable.", 503);
            var approved = from attempt in _attemptRegistrations.GetQueryable().AsNoTracking()
                join subject in _subjects.GetQueryable().AsNoTracking() on attempt.AssessmentSubjectId equals subject.Id
                join registration in _registrations.GetQueryable().AsNoTracking()
                    on attempt.StudentSubjectRegistrationId equals registration.Id
                where attempt.TenantId == tenant && subject.TenantId == tenant && registration.TenantId == tenant &&
                    subject.AssessmentId == scope.Value.Exam.Id && !attempt.IsDeleted && !subject.IsDeleted &&
                    !registration.IsDeleted && attempt.Decision == AssessmentAttemptDecision.Approved
                select registration.StudentEnrollmentId;
            roster = roster.Where(x => approved.Contains(x.enrollment.Id));
        }
        var total = await roster.CountAsync(ct);
        var students = await roster.OrderBy(x => x.enrollment.RollNo).ThenBy(x => x.enrollment.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        var subjects = await (from sub in _subjects.GetQueryable().AsNoTracking()
            join offering in _offerings.GetQueryable().AsNoTracking()
                on sub.SubjectOfferingId equals offering.Id
            join item in _curriculumSubjects.GetQueryable().AsNoTracking()
                on offering.CurriculumSubjectId equals item.Id
            join name in _subjectNames.GetQueryable().AsNoTracking() on item.SubjectId equals name.Id
            where sub.TenantId == tenant && offering.TenantId == tenant &&
                item.TenantId == tenant && name.TenantId == tenant &&
                sub.AssessmentId == scope.Value.Exam.Id && offering.AcademicBatchId == batch.Id &&
                offering.AcademicYearId == batch.AcademicYearId &&
                !sub.IsDeleted && !offering.IsDeleted && !item.IsDeleted && !name.IsDeleted
            orderby sub.Id
            select new { AssessmentSubjectId = sub.Id, sub.FullMarks,
                OfferingId = offering.Id, item.AcademicCurriculumId, item.AcademicLevelId,
                item.IsOptional, SubjectName = name.Name }).Take(201).ToListAsync(ct);
        if (subjects.Count > 200)
            return Error<AssessmentReadinessPageDto>("More than 200 assessment subjects require an asynchronous review.", 409);
        var enrollmentIds = students.Select(x => x.enrollment.Id).ToArray();
        var offeringIds = subjects.Select(x => x.OfferingId).ToArray();
        var registrations = await _registrations.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && enrollmentIds.Contains(x.StudentEnrollmentId) &&
            offeringIds.Contains(x.SubjectOfferingId) &&
            x.State == SubjectRegistrationState.Approved && !x.IsDeleted).ToListAsync(ct);
        var registrationIds = registrations.Select(x => x.Id).ToArray();
        var subjectIds = subjects.Select(x => x.AssessmentSubjectId).ToArray();
        HashSet<(long SubjectId, long RegistrationId)>? authorized = null;
        if (scope.Value.Exam.AttemptMode != AssessmentAttemptMode.Regular)
        {
            var attempts = await _attemptRegistrations!.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && subjectIds.Contains(x.AssessmentSubjectId) &&
                registrationIds.Contains(x.StudentSubjectRegistrationId) &&
                x.Decision == AssessmentAttemptDecision.Approved && !x.IsDeleted)
                .Select(x => new { x.AssessmentSubjectId, x.StudentSubjectRegistrationId }).ToListAsync(ct);
            authorized = attempts.Select(x => (x.AssessmentSubjectId, x.StudentSubjectRegistrationId)).ToHashSet();
        }
        var marks = await _marks.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && subjectIds.Contains(x.AssessmentSubjectId) &&
            registrationIds.Contains(x.StudentSubjectRegistrationId) && !x.IsDeleted).ToListAsync(ct);
        var registrationLookup = registrations.GroupBy(x => (x.StudentEnrollmentId, x.SubjectOfferingId))
            .ToDictionary(x => x.Key, x => x.ToArray());
        var markLookup = marks.GroupBy(x => (x.AssessmentSubjectId, x.StudentSubjectRegistrationId))
            .ToDictionary(x => x.Key, x => x.ToArray());
        var definitions = await _components.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && subjectIds.Contains(x.AssessmentSubjectId) &&
            !x.IsDeleted).ToListAsync(ct);
        var componentsBySubject = definitions.GroupBy(x => x.AssessmentSubjectId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<EduOS.Core.Entities.Assessment.AssessmentComponent>)x.ToList());
        var masterIds = marks.Select(x => x.Id).ToArray();
        var details = await _componentMarks.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && masterIds.Contains(x.StudentAssessmentMarkId) &&
            !x.IsDeleted).ToListAsync(ct);
        var detailsByMaster = details.GroupBy(x => x.StudentAssessmentMarkId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<EduOS.Core.Entities.Assessment.StudentAssessmentComponentMark>)x.ToList());
        var output = new List<AssessmentReadinessStudentDto>(students.Count);
        foreach (var person in students)
        {
            var issues = new List<AssessmentReadinessIssueDto>();
            var applicable = 0; var entered = 0; var absent = 0;
            foreach (var subject in subjects)
            {
                if (!AssessmentSubjectEligibility.AppliesToEnrollment(subject.AcademicCurriculumId,
                    subject.AcademicLevelId, person.enrollment.AcademicCurriculumId,
                    person.enrollment.AcademicLevelId))
                    continue;
                registrationLookup.TryGetValue((person.enrollment.Id, subject.OfferingId), out var regs);
                if (authorized != null && (regs == null ||
                    !regs.Any(x => authorized.Contains((subject.AssessmentSubjectId, x.Id)))))
                    continue;
                if (!AssessmentSubjectEligibility.RequiresMark(subject.IsOptional, regs is { Length: > 0 }))
                    continue;
                applicable++;
                void Issue(string code) => issues.Add(new AssessmentReadinessIssueDto
                { SubjectName = subject.SubjectName, Code = code });
                if (regs == null || regs.Length == 0) { Issue("MissingRegistration"); continue; }
                if (regs.Length != 1) { Issue("DuplicateRegistration"); continue; }
                markLookup.TryGetValue((subject.AssessmentSubjectId, regs[0].Id), out var entries);
                if (entries == null || entries.Length == 0) { Issue("MissingMark"); continue; }
                if (entries.Length != 1) { Issue("DuplicateMark"); continue; }
                var mark = entries[0]; entered++;
                if (mark.IsWithheld) Issue("Withheld");
                if (mark.IsAbsent) absent++;
                if (mark.ObtainedMarks < 0 || mark.ObtainedMarks > subject.FullMarks ||
                    mark.IsAbsent && mark.ObtainedMarks != 0)
                    Issue("InvalidMark");
                componentsBySubject.TryGetValue(subject.AssessmentSubjectId, out var definitionsForSubject);
                detailsByMaster.TryGetValue(mark.Id, out var detailsForMark);
                var componentCheck = AssessmentComponentMarkPolicy.Evaluate(
                    subject.FullMarks, mark.ObtainedMarks, mark.IsAbsent, mark.IsWithheld,
                    definitionsForSubject ?? Array.Empty<EduOS.Core.Entities.Assessment.AssessmentComponent>(),
                    detailsForMark ?? Array.Empty<EduOS.Core.Entities.Assessment.StudentAssessmentComponentMark>());
                if (!componentCheck.IsReconciled) Issue(componentCheck.Status);
            }
            if (applicable == 0)
                issues.Add(new AssessmentReadinessIssueDto { Code = "NoApplicableSubjects" });
            output.Add(new AssessmentReadinessStudentDto
            {
                StudentReference = person.student.PublicId, StudentCode = person.student.StudentCode,
                StudentName = person.student.FullName, RollNo = person.enrollment.RollNo,
                ApplicableSubjects = applicable, RecordedMarks = entered, AbsentMarks = absent,
                IsReady = issues.Count == 0, Issues = issues
            });
        }
        return ApiResponse<AssessmentReadinessPageDto>.SuccessResponse(new AssessmentReadinessPageDto
        {
            AssessmentId = scope.Value.Exam.Id, AcademicBatchId = batch.Id,
            Page = request.Page, PageSize = request.PageSize, TotalCount = total, Students = output
        });
    }
}
