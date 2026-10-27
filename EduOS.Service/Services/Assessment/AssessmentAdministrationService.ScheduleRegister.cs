using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<AssessmentScheduleRegisterPageDto>> SearchAssessmentSchedulesAsync(
        AssessmentScheduleRegisterQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<AssessmentScheduleRegisterPageDto>("Assessment management permission required.", 403);
        if (request == null || request.Page is < 1 or > 100000 ||
            request.PageSize is < 1 or > 100 || request.AssessmentId is <= 0 ||
            request.AssessmentSubjectId is <= 0 ||
            request.FromDate.HasValue && request.ToDate.HasValue &&
                request.FromDate > request.ToDate)
            return Error<AssessmentScheduleRegisterPageDto>("Invalid schedule filters.");
        var tenant = _user.TenantId;
        var query =
            from schedule in _schedules.GetQueryable().AsNoTracking()
            join subject in _subjects.GetQueryable().AsNoTracking()
                on schedule.AssessmentSubjectId equals subject.Id
            join exam in _assessments.GetQueryable().AsNoTracking()
                on subject.AssessmentId equals exam.Id
            join offering in _offerings.GetQueryable().AsNoTracking()
                on subject.SubjectOfferingId equals offering.Id
            join batch in _batches.GetQueryable().AsNoTracking()
                on offering.AcademicBatchId equals batch.Id
            where schedule.TenantId == tenant && subject.TenantId == tenant &&
                exam.TenantId == tenant && offering.TenantId == tenant &&
                batch.TenantId == tenant && !schedule.IsDeleted && !subject.IsDeleted &&
                !exam.IsDeleted && !offering.IsDeleted && !batch.IsDeleted &&
                exam.CampusId == batch.CampusId && exam.AcademicYearId == batch.AcademicYearId
            select new { schedule, subject, exam, batch };
        if (IsRestrictedCampusManager())
            query = query.Where(x => AllowedCampusIds(tenant).Contains(x.exam.CampusId));
        if (request.AssessmentId.HasValue)
            query = query.Where(x => x.exam.Id == request.AssessmentId.Value);
        if (request.AssessmentSubjectId.HasValue)
            query = query.Where(x => x.subject.Id == request.AssessmentSubjectId.Value);
        if (request.FromDate.HasValue)
            query = query.Where(x => x.schedule.AssessmentDate >= request.FromDate.Value);
        if (request.ToDate.HasValue)
            query = query.Where(x => x.schedule.AssessmentDate <= request.ToDate.Value);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.schedule.AssessmentDate)
            .ThenBy(x => x.schedule.StartTime).ThenBy(x => x.schedule.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        var mapped = new List<AssessmentScheduleRegisterRowDto>(rows.Count);
        foreach (var item in rows)
        {
            var detail = await MapScheduleAsync(item.schedule, ct);
            mapped.Add(new AssessmentScheduleRegisterRowDto
            {
                Id = item.schedule.Id, AssessmentId = item.exam.Id,
                AssessmentName = item.exam.Name, AssessmentSubjectId = item.subject.Id,
                SubjectName = detail.SubjectName, BatchName = item.batch.Name,
                State = item.exam.State, AssessmentDate = item.schedule.AssessmentDate,
                StartTime = item.schedule.StartTime, EndTime = item.schedule.EndTime,
                RoomId = item.schedule.RoomId, RoomName = detail.RoomName,
                Instructions = item.schedule.Instructions,
                RowVersion = Version(item.schedule.RowVersion)
            });
        }
        return ApiResponse<AssessmentScheduleRegisterPageDto>.SuccessResponse(new()
        {
            Page = request.Page, PageSize = request.PageSize, TotalCount = total, Items = mapped
        });
    }

    public async Task<ApiResponse<IReadOnlyList<AssessmentScheduleRoomDto>>> GetAssessmentScheduleRoomsAsync(
        long assessmentSubjectId, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<IReadOnlyList<AssessmentScheduleRoomDto>>("Assessment management permission required.", 403);
        if (assessmentSubjectId <= 0)
            return Error<IReadOnlyList<AssessmentScheduleRoomDto>>("Assessment subject is required.");
        var tenant = _user.TenantId;
        var scope = await (
            from subject in _subjects.GetQueryable().AsNoTracking()
            join exam in _assessments.GetQueryable().AsNoTracking()
                on subject.AssessmentId equals exam.Id
            join offering in _offerings.GetQueryable().AsNoTracking()
                on subject.SubjectOfferingId equals offering.Id
            join batch in _batches.GetQueryable().AsNoTracking()
                on offering.AcademicBatchId equals batch.Id
            where subject.Id == assessmentSubjectId && subject.TenantId == tenant &&
                exam.TenantId == tenant && offering.TenantId == tenant &&
                batch.TenantId == tenant && !subject.IsDeleted && !exam.IsDeleted &&
                !offering.IsDeleted && !batch.IsDeleted &&
                batch.CampusId == exam.CampusId && batch.AcademicYearId == exam.AcademicYearId
            select new { exam.CampusId }).FirstOrDefaultAsync(ct);
        if (scope == null)
            return Error<IReadOnlyList<AssessmentScheduleRoomDto>>("Assessment subject not found.", 404);
        if (!await CanAccessCampusAsync(scope.CampusId, ct))
            return Error<IReadOnlyList<AssessmentScheduleRoomDto>>("Campus access denied.", 403);
        IReadOnlyList<AssessmentScheduleRoomDto> rooms = await _rooms.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.CampusId == scope.CampusId &&
                x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Take(200)
            .Select(x => new AssessmentScheduleRoomDto
            {
                Id = x.Id, Name = x.Name, Code = x.Code
            }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<AssessmentScheduleRoomDto>>.SuccessResponse(rooms);
    }
}
