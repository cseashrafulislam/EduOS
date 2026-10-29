using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    // SQL-paged canonical registration roster; legacy bounded roster is retained for component marks.
    public async Task<ApiResponse<AssessmentMarkRosterDto>> SearchMarkRosterAsync(
        AssessmentMarkRosterPageQueryDto request, CancellationToken ct = default)
    {
        if (!CanRead()) return Error<AssessmentMarkRosterDto>("Assessment permission required.", 403);
        if (request == null || request.AssessmentSubjectId <= 0 || request.Page < 1 ||
            request.Page > 100000 || request.PageSize is < 1 or > 100 ||
            request.Search?.Length > 100 || ((long)request.Page - 1) * request.PageSize > int.MaxValue)
            return Error<AssessmentMarkRosterDto>("Invalid marks roster page or search.");
        var scope = await ScopeAsync(request, ct);
        if (scope == null) return Error<AssessmentMarkRosterDto>("Assessment or batch not found.", 404);
        var tenant = _user.TenantId;
        var sub = await _subjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == request.AssessmentSubjectId &&
            x.AssessmentId == scope.Value.Exam.Id && !x.IsDeleted, ct);
        if (sub == null) return Error<AssessmentMarkRosterDto>("Assessment subject not found.", 404);
        var offering = await _offerings.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == sub.SubjectOfferingId &&
            x.AcademicBatchId == request.AcademicBatchId && x.IsActive && !x.IsDeleted, ct);
        if (offering == null)
            return Error<AssessmentMarkRosterDto>("Subject offering does not belong to this batch.", 409);
        if (!await CanEditOfferingAsync(offering.Id, ct))
            return Error<AssessmentMarkRosterDto>("Instructor is not assigned to this offering.", 403);

        var eligible = from registration in _registrations.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on registration.StudentEnrollmentId equals enrollment.Id
            join student in _students.GetQueryable().AsNoTracking()
                on enrollment.StudentId equals student.Id
            where registration.TenantId == tenant && enrollment.TenantId == tenant &&
                student.TenantId == tenant && registration.SubjectOfferingId == offering.Id &&
                registration.State == SubjectRegistrationState.Approved &&
                enrollment.AcademicBatchId == request.AcademicBatchId &&
                enrollment.IsCurrent && enrollment.State == EnrollmentState.Active &&
                student.StatusCode == "Active" && !registration.IsDeleted &&
                !enrollment.IsDeleted && !student.IsDeleted
            select new
            {
                RegistrationId = registration.Id, StudentReference = student.PublicId,
                StudentCode = student.StudentCode, StudentName = student.FullName,
                RollNo = enrollment.RollNo
            };
        if (scope.Value.Exam.AttemptMode != AssessmentAttemptMode.Regular)
        {
            if (_attemptRegistrations == null)
                return Error<AssessmentMarkRosterDto>("Attempt register is unavailable.", 503);
            var allowed = ApprovedAttemptQuery(sub.Id).Select(x => x.StudentSubjectRegistrationId);
            eligible = eligible.Where(x => allowed.Contains(x.RegistrationId));
        }
        var term = request.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            eligible = eligible.Where(x => x.RollNo.StartsWith(term) ||
                x.StudentCode.StartsWith(term) || x.StudentName.StartsWith(term));
        var total = await eligible.CountAsync(ct);
        var rows = await eligible.OrderBy(x => x.RollNo).ThenBy(x => x.RegistrationId)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.RegistrationId).ToArray();
        var evidence = await _marks.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AssessmentSubjectId == sub.Id &&
            ids.Contains(x.StudentSubjectRegistrationId) && !x.IsDeleted).ToListAsync(ct);
        if (evidence.GroupBy(x => x.StudentSubjectRegistrationId).Any(x => x.Count() != 1))
            return Error<AssessmentMarkRosterDto>("Duplicate marks require reconciliation.", 409);
        var marks = evidence.ToDictionary(x => x.StudentSubjectRegistrationId);
        var subjectName = await (from item in _curriculumSubjects.GetQueryable().AsNoTracking()
            join subject in _subjectNames.GetQueryable().AsNoTracking()
                on item.SubjectId equals subject.Id
            where item.TenantId == tenant && subject.TenantId == tenant &&
                item.Id == offering.CurriculumSubjectId && !item.IsDeleted && !subject.IsDeleted
            select subject.Name).FirstOrDefaultAsync(ct);
        var published = await _publications.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.AssessmentId == scope.Value.Exam.Id &&
            x.AcademicBatchId == request.AcademicBatchId &&
            x.State == ResultPublicationState.Published && !x.IsDeleted, ct);
        return ApiResponse<AssessmentMarkRosterDto>.SuccessResponse(new AssessmentMarkRosterDto
        {
            AssessmentId = scope.Value.Exam.Id, AssessmentName = scope.Value.Exam.Name,
            AcademicBatchId = request.AcademicBatchId, AssessmentSubjectId = sub.Id,
            SubjectName = subjectName ?? string.Empty, FullMarks = sub.FullMarks,
            PassMarks = sub.PassMarks, IsResultPublished = published,
            Page = request.Page, PageSize = request.PageSize, TotalCount = total,
            Students = rows.Select(x =>
            {
                marks.TryGetValue(x.RegistrationId, out var mark);
                return new AssessmentMarkRosterItemDto
                {
                    StudentReference = x.StudentReference, StudentCode = x.StudentCode,
                    StudentName = x.StudentName, RollNo = x.RollNo,
                    StudentSubjectRegistrationId = x.RegistrationId,
                    ObtainedMarks = mark?.ObtainedMarks, IsAbsent = mark?.IsAbsent ?? false,
                    IsWithheld = mark?.IsWithheld ?? false, GradeLetter = mark?.GradeLetter,
                    GradePoint = mark?.GradePoint,
                    RowVersion = mark == null ? null : Version(mark.RowVersion)
                };
            }).ToList()
        });
    }
}
