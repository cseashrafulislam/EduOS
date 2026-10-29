using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    // Read-only current working-mark evidence. It must NEVER be represented as
    // the historical per-subject marks behind a published aggregate snapshot.
    public async Task<ApiResponse<ResultRescrutinyMarkEvidenceDto>> GetResultRescrutinyMarkEvidenceAsync(
        Guid caseReference, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<ResultRescrutinyMarkEvidenceDto>("Examination review permission required.", 403);
        if (caseReference == Guid.Empty) return Error<ResultRescrutinyMarkEvidenceDto>("Invalid case reference.");

        var review = await GetResultRescrutinyReviewAsync(caseReference, ct);
        if (!review.Success || review.Data == null)
            return Error<ResultRescrutinyMarkEvidenceDto>(review.Message, review.StatusCode);
        var tenant = _user.TenantId;
        var original = await _rescrutinyEvents.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.CaseReference == caseReference &&
            x.ResultPublicationId == review.Data.PublicationId &&
            x.PublicationVersionNo == review.Data.PublicationVersionNo &&
            !x.IsTerminal && x.Action == ResultRescrutinyAction.Requested && !x.IsDeleted, ct);
        if (original == null) return Error<ResultRescrutinyMarkEvidenceDto>("Case evidence requires reconciliation.", 409);
        var scope = await FindRescrutinyScopeAsync(original.ResultPublicationId, ct);
        if (scope == null || !await CanAccessCampusAsync(scope.CampusId, ct))
            return Error<ResultRescrutinyMarkEvidenceDto>("Campus access denied.", 403);

        var registrations = await (from registration in _registrations.GetQueryable().AsNoTracking()
            join offering in _offerings.GetQueryable().AsNoTracking()
                on new { registration.TenantId, Id = registration.SubjectOfferingId }
                equals new { offering.TenantId, offering.Id }
            join assessmentSubject in _subjects.GetQueryable().AsNoTracking()
                on new { offering.TenantId, Id = offering.Id }
                equals new { assessmentSubject.TenantId, Id = assessmentSubject.SubjectOfferingId }
            join curriculum in _curriculumSubjects.GetQueryable().AsNoTracking()
                on new { offering.TenantId, Id = offering.CurriculumSubjectId }
                equals new { curriculum.TenantId, curriculum.Id }
            join subject in _subjectNames.GetQueryable().AsNoTracking()
                on new { curriculum.TenantId, Id = curriculum.SubjectId }
                equals new { subject.TenantId, subject.Id }
            where registration.TenantId == tenant &&
                registration.StudentEnrollmentId == original.StudentEnrollmentId &&
                registration.State == SubjectRegistrationState.Approved &&
                offering.AcademicBatchId == scope.Publication.AcademicBatchId &&
                offering.AcademicYearId == scope.AcademicYearId &&
                assessmentSubject.AssessmentId == scope.Publication.AssessmentId &&
                !registration.IsDeleted && !offering.IsDeleted && !assessmentSubject.IsDeleted &&
                !curriculum.IsDeleted && !subject.IsDeleted
            orderby subject.Name, assessmentSubject.Id
            select new
            {
                RegistrationId = registration.Id, SubjectId = assessmentSubject.Id,
                SubjectName = subject.Name, assessmentSubject.FullMarks
            }).Take(101).ToListAsync(ct);
        if (registrations.Count > 100 || registrations.GroupBy(x => x.SubjectId).Any(x => x.Count() != 1))
            return Error<ResultRescrutinyMarkEvidenceDto>("Registered subject evidence requires reconciliation.", 409);

        var registrationIds = registrations.Select(x => x.RegistrationId).ToArray();
        var subjectIds = registrations.Select(x => x.SubjectId).ToArray();
        var marks = await _marks.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && registrationIds.Contains(x.StudentSubjectRegistrationId) &&
            subjectIds.Contains(x.AssessmentSubjectId) && !x.IsDeleted)
            .Select(x => new
            {
                x.Id, x.AssessmentSubjectId, x.StudentSubjectRegistrationId,
                x.ObtainedMarks, x.IsAbsent, x.IsWithheld, x.GradeLetter, x.GradePoint
            }).Take(201).ToListAsync(ct);
        if (marks.Count > 200 || marks.GroupBy(x => (x.AssessmentSubjectId, x.StudentSubjectRegistrationId))
            .Any(x => x.Count() != 1))
            return Error<ResultRescrutinyMarkEvidenceDto>("Current mark evidence requires reconciliation.", 409);

        var components = await _components.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && subjectIds.Contains(x.AssessmentSubjectId) && !x.IsDeleted)
            .Select(x => new { x.Id, x.AssessmentSubjectId, x.Name, x.FullMarks, x.DisplayOrder })
            .OrderBy(x => x.AssessmentSubjectId).ThenBy(x => x.DisplayOrder).ThenBy(x => x.Id)
            .Take(401).ToListAsync(ct);
        if (components.Count > 400)
            return Error<ResultRescrutinyMarkEvidenceDto>("Component evidence exceeds review limit.", 409);
        var componentIds = components.Select(x => x.Id).ToArray();
        var markIds = marks.Select(x => x.Id).ToArray();
        var details = await _componentMarks.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && componentIds.Contains(x.AssessmentComponentId) &&
            markIds.Contains(x.StudentAssessmentMarkId) && !x.IsDeleted)
            .Select(x => new { x.AssessmentComponentId, x.StudentAssessmentMarkId,
                x.ObtainedMarks, x.IsAbsent, x.IsWithheld }).Take(401).ToListAsync(ct);
        if (details.Count > 400 || details.GroupBy(x => (x.AssessmentComponentId, x.StudentAssessmentMarkId))
            .Any(x => x.Count() != 1))
            return Error<ResultRescrutinyMarkEvidenceDto>("Component marks require reconciliation.", 409);

        var markByPair = marks.ToDictionary(x => (x.AssessmentSubjectId, x.StudentSubjectRegistrationId));
        var detailByPair = details.ToDictionary(x => (x.AssessmentComponentId, x.StudentAssessmentMarkId));
        var result = registrations.Select(reg =>
        {
            markByPair.TryGetValue((reg.SubjectId, reg.RegistrationId), out var mark);
            return new ResultRescrutinySubjectEvidenceDto
            {
                AssessmentSubjectId = reg.SubjectId, SubjectName = reg.SubjectName,
                FullMarks = reg.FullMarks, CurrentObtainedMarks = mark?.ObtainedMarks,
                IsAbsent = mark?.IsAbsent ?? false, IsWithheld = mark?.IsWithheld ?? false,
                CurrentGradeLetter = mark?.GradeLetter, CurrentGradePoint = mark?.GradePoint,
                Components = components.Where(c => c.AssessmentSubjectId == reg.SubjectId).Select(c =>
                {
                    var hasDetail = mark != null && detailByPair.TryGetValue((c.Id, mark.Id), out _);
                    var detail = hasDetail ? detailByPair[(c.Id, mark!.Id)] : null;
                    return new ResultRescrutinyComponentEvidenceDto
                    {
                        Name = c.Name, FullMarks = c.FullMarks,
                        CurrentObtainedMarks = detail?.ObtainedMarks,
                        IsAbsent = detail?.IsAbsent ?? false, IsWithheld = detail?.IsWithheld ?? false
                    };
                }).ToArray()
            };
        }).ToArray();
        return ApiResponse<ResultRescrutinyMarkEvidenceDto>.SuccessResponse(new()
        {
            CaseReference = caseReference, PublicationId = review.Data.PublicationId,
            PublicationVersionNo = review.Data.PublicationVersionNo,
            StudentCode = review.Data.StudentCode,
            PublishedObtainedMarks = review.Data.ObtainedMarks,
            PublishedTotalMarks = review.Data.TotalMarks, Subjects = result
        });
    }
}
