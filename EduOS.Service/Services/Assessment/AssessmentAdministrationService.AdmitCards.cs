using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    // Administrative, bounded print roster. This is not a public credential or a result.
    public async Task<ApiResponse<AssessmentAdmitCardPageDto>> SearchAdmitCardsAsync(
        AssessmentAdmitCardQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<AssessmentAdmitCardPageDto>("Exam administration permission required.", 403);
        if (request == null || request.AssessmentId <= 0 || request.AcademicBatchId <= 0 ||
            request.Page is < 1 or > 100000 || request.PageSize is < 1 or > 50 ||
            request.Search?.Length > 100)
            return Error<AssessmentAdmitCardPageDto>("Invalid admit card filters.");

        var scope = await ScopeAsync(request, ct);
        if (scope == null)
            return Error<AssessmentAdmitCardPageDto>("Assessment or authorized batch not found.", 404);
        var (exam, batch) = scope.Value;
        if (exam.State is AssessmentState.Draft or AssessmentState.Cancelled)
            return Error<AssessmentAdmitCardPageDto>("Assessment must be scheduled before printing admit cards.", 409);
        var tenant = _user.TenantId;
        var subjects = await (
            from item in _subjects.GetQueryable().AsNoTracking()
            join offering in _offerings.GetQueryable().AsNoTracking()
                on item.SubjectOfferingId equals offering.Id
            join curriculum in _curriculumSubjects.GetQueryable().AsNoTracking()
                on offering.CurriculumSubjectId equals curriculum.Id
            join name in _subjectNames.GetQueryable().AsNoTracking()
                on curriculum.SubjectId equals name.Id
            where item.TenantId == tenant && offering.TenantId == tenant &&
                curriculum.TenantId == tenant && name.TenantId == tenant &&
                item.AssessmentId == exam.Id && offering.AcademicBatchId == batch.Id &&
                offering.AcademicYearId == exam.AcademicYearId &&
                !item.IsDeleted && !offering.IsDeleted && offering.IsActive &&
                !curriculum.IsDeleted && !name.IsDeleted
            select new { item.Id, item.SubjectOfferingId, name.Name, name.Code }
        ).ToListAsync(ct);
        if (subjects.Count == 0 || subjects.Count > 100 ||
            subjects.GroupBy(x => x.SubjectOfferingId).Any(x => x.Count() != 1))
            return Error<AssessmentAdmitCardPageDto>("Assessment subjects require reconciliation.", 409);

        var subjectIds = subjects.Select(x => x.Id).ToArray();
        var sessions = await _schedules.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && subjectIds.Contains(x.AssessmentSubjectId) && !x.IsDeleted)
            .Select(x => new { x.AssessmentSubjectId, x.AssessmentDate, x.StartTime,
                x.EndTime, x.RoomId }).ToListAsync(ct);
        if (subjects.Any(x => !sessions.Any(s => s.AssessmentSubjectId == x.Id)) ||
            sessions.Any(x => x.AssessmentDate < exam.StartDate || x.AssessmentDate > exam.EndDate ||
                x.StartTime.HasValue != x.EndTime.HasValue ||
                x.StartTime.HasValue && x.EndTime <= x.StartTime))
            return Error<AssessmentAdmitCardPageDto>("Exam schedule is incomplete or inconsistent.", 409);
        var roomIds = sessions.Where(x => x.RoomId.HasValue).Select(x => x.RoomId!.Value).Distinct().ToArray();
        var rooms = await _rooms.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.CampusId == exam.CampusId &&
                roomIds.Contains(x.Id) && !x.IsDeleted)
            .Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        if (sessions.Any(x => x.RoomId.HasValue && !rooms.ContainsKey(x.RoomId.Value)))
            return Error<AssessmentAdmitCardPageDto>("Exam room evidence requires reconciliation.", 409);

        var offeringIds = subjects.Select(x => x.SubjectOfferingId).ToArray();
        var candidates = from enrollment in _enrollments.GetQueryable().AsNoTracking()
            join student in _students.GetQueryable().AsNoTracking()
                on enrollment.StudentId equals student.Id
            where enrollment.TenantId == tenant && student.TenantId == tenant &&
                enrollment.AcademicBatchId == batch.Id && enrollment.CampusId == exam.CampusId &&
                enrollment.AcademicYearId == exam.AcademicYearId &&
                enrollment.IsCurrent && enrollment.State == EnrollmentState.Active &&
                student.StatusCode == "Active" &&
                !enrollment.IsDeleted && !student.IsDeleted &&
                _registrations.GetQueryable().AsNoTracking().Any(reg =>
                    reg.TenantId == tenant && !reg.IsDeleted &&
                    reg.StudentEnrollmentId == enrollment.Id &&
                    offeringIds.Contains(reg.SubjectOfferingId) &&
                    (reg.State == SubjectRegistrationState.Approved ||
                     reg.State == SubjectRegistrationState.Completed))
            select new { EnrollmentId = enrollment.Id, student.PublicId,
                student.StudentCode, StudentName = student.FullName, enrollment.RollNo };
        var term = request.Search?.Trim();
        if (!string.IsNullOrEmpty(term))
            candidates = candidates.Where(x => x.StudentName.Contains(term) ||
                x.StudentCode.Contains(term) || x.RollNo.Contains(term));
        var total = await candidates.CountAsync(ct);
        var page = await candidates.OrderBy(x => x.RollNo).ThenBy(x => x.EnrollmentId)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        var enrollmentIds = page.Select(x => x.EnrollmentId).ToArray();
        var registrations = await _registrations.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                enrollmentIds.Contains(x.StudentEnrollmentId) &&
                offeringIds.Contains(x.SubjectOfferingId) &&
                (x.State == SubjectRegistrationState.Approved ||
                 x.State == SubjectRegistrationState.Completed))
            .Select(x => new { x.StudentEnrollmentId, x.SubjectOfferingId }).ToListAsync(ct);
        if (registrations.GroupBy(x => (x.StudentEnrollmentId, x.SubjectOfferingId))
            .Any(x => x.Count() != 1))
            return Error<AssessmentAdmitCardPageDto>("Duplicate candidate registrations require reconciliation.", 409);
        var byOffering = subjects.ToDictionary(x => x.SubjectOfferingId);
        var cards = page.Select(candidate => new AssessmentAdmitCardDto
        {
            StudentReference = candidate.PublicId, StudentCode = candidate.StudentCode,
            StudentName = candidate.StudentName, RollNo = candidate.RollNo,
            Sessions = registrations.Where(x => x.StudentEnrollmentId == candidate.EnrollmentId)
                .SelectMany(x => sessions.Where(s =>
                    s.AssessmentSubjectId == byOffering[x.SubjectOfferingId].Id)
                    .Select(s => new AssessmentAdmitCardScheduleDto
                    {
                        SubjectName = byOffering[x.SubjectOfferingId].Name,
                        SubjectCode = byOffering[x.SubjectOfferingId].Code,
                        Date = s.AssessmentDate, StartTime = s.StartTime, EndTime = s.EndTime,
                        RoomName = s.RoomId.HasValue ? rooms[s.RoomId.Value] : null
                    }))
                .OrderBy(x => x.Date).ThenBy(x => x.StartTime)
                .ThenBy(x => x.SubjectCode).ToList()
        }).ToList();
        return ApiResponse<AssessmentAdmitCardPageDto>.SuccessResponse(new()
        {
            AssessmentName = exam.Name, BatchName = batch.Name, Page = request.Page,
            PageSize = request.PageSize, TotalCount = total, Items = cards
        });
    }
}
