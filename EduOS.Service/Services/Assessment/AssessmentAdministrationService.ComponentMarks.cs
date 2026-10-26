using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<AssessmentComponentMarkRosterDto>> GetComponentMarkRosterAsync(
        AssessmentComponentMarkRosterQueryDto request, CancellationToken ct = default)
    {
        if (!CanRead()) return Error<AssessmentComponentMarkRosterDto>("Marks access denied.", 403);
        if (request == null || request.AssessmentId <= 0 ||
            request.AcademicBatchId <= 0 || request.AssessmentSubjectId <= 0)
            return Error<AssessmentComponentMarkRosterDto>("Assessment, batch and subject are required.");
        // Reuse canonical enrollment, campus/year, instructor and tenant eligibility checks.
        var rosterResult = await GetMarkRosterAsync(new AssessmentMarkRosterQueryDto
        {
            AssessmentId = request.AssessmentId,
            AcademicBatchId = request.AcademicBatchId,
            AssessmentSubjectId = request.AssessmentSubjectId
        }, ct);
        if (!rosterResult.Success || rosterResult.Data == null)
            return Error<AssessmentComponentMarkRosterDto>(rosterResult.Message, rosterResult.StatusCode);
        var roster = rosterResult.Data;
        var tenant = _user.TenantId;
        var exam = await _assessments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == request.AssessmentId && !x.IsDeleted, ct);
        if (exam == null) return Error<AssessmentComponentMarkRosterDto>("Assessment not found.", 404);
        var components = await _components.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AssessmentSubjectId == request.AssessmentSubjectId && !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Take(51).ToListAsync(ct);
        if (components.Count > 50)
            return Error<AssessmentComponentMarkRosterDto>("Too many components for the synchronous register.", 409);
        var registrations = roster.Students.Select(x => x.StudentSubjectRegistrationId).ToArray();
        var masters = await _marks.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AssessmentSubjectId == request.AssessmentSubjectId &&
            registrations.Contains(x.StudentSubjectRegistrationId) && !x.IsDeleted)
            .ToListAsync(ct);
        var masterByRegistration = masters.ToDictionary(x => x.StudentSubjectRegistrationId);
        var masterIds = masters.Select(x => x.Id).ToArray();
        var componentIds = components.Select(x => x.Id).ToArray();
        var saved = await _componentMarks.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && masterIds.Contains(x.StudentAssessmentMarkId) &&
            componentIds.Contains(x.AssessmentComponentId) && !x.IsDeleted).ToListAsync(ct);
        var byMaster = saved.GroupBy(x => x.StudentAssessmentMarkId).ToDictionary(
            x => x.Key, x => (IReadOnlyList<AssessmentComponentMarkEntryDto>)x.Select(m =>
                new AssessmentComponentMarkEntryDto
                {
                    Id = m.Id, AssessmentComponentId = m.AssessmentComponentId,
                    ObtainedMarks = m.ObtainedMarks, IsAbsent = m.IsAbsent,
                    IsWithheld = m.IsWithheld, RowVersion = Version(m.RowVersion)
                }).ToList());
        return ApiResponse<AssessmentComponentMarkRosterDto>.SuccessResponse(new()
        {
            AssessmentId = request.AssessmentId, AcademicBatchId = request.AcademicBatchId,
            AssessmentSubjectId = request.AssessmentSubjectId,
            AssessmentName = roster.AssessmentName, SubjectName = roster.SubjectName,
            IsResultPublished = roster.IsResultPublished,
            IsEditable = !roster.IsResultPublished && exam.State == AssessmentState.MarksEntry,
            Components = components.Select(x => new AssessmentComponentMarkOptionDto
            {
                Id = x.Id, Name = x.Name, Code = x.Code, FullMarks = x.FullMarks,
                PassMarks = x.PassMarks, IsMandatoryPass = x.IsMandatoryPass
            }).ToList(),
            Students = roster.Students.Select(x =>
            {
                masterByRegistration.TryGetValue(x.StudentSubjectRegistrationId, out var master);
                return new AssessmentComponentMarkStudentDto
                {
                    StudentReference = x.StudentReference, StudentCode = x.StudentCode,
                    StudentName = x.StudentName, RollNo = x.RollNo,
                    StudentAssessmentMarkId = master?.Id,
                    Marks = master != null && byMaster.TryGetValue(master.Id, out var marks)
                        ? marks : Array.Empty<AssessmentComponentMarkEntryDto>()
                };
            }).ToList()
        });
    }
}
