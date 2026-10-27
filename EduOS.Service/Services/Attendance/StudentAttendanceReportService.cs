using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Attendance;

/// <summary>
/// Manager-only, bounded readback of posted attendance. Each mark belongs to its
/// original enrollment and campus; current-enrollment state is deliberately ignored.
/// </summary>
public sealed class StudentAttendanceReportService(
    EduOSDbContext db, ICurrentUserService user, ILogger<StudentAttendanceReportService> logger)
    : IStudentAttendanceReportService
{

    /// <summary>
    /// Historical (including inactive) offerings, SQL-paged and campus-authorized.
    /// No student names or marks are returned by the picker.
    /// </summary>
    public async Task<ApiResponse<PagedResult<StudentAttendanceReportSubjectDto>>> GetSubjectChoicesAsync(
        long academicBatchId, string? search, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || user.TenantId <= 0 ||
            !(user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("VicePrincipal")))
            return ApiResponse<PagedResult<StudentAttendanceReportSubjectDto>>.ErrorResponse(
                "Attendance report permission is required.", 403);
        if (academicBatchId <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue || search?.Length > 100)
            return ApiResponse<PagedResult<StudentAttendanceReportSubjectDto>>.ErrorResponse(
                "Invalid batch, search or page.");
        var tenant = user.TenantId;
        var batch = await db.AcademicBatches.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Id == academicBatchId && !x.IsDeleted)
            .Select(x => new { x.CampusId }).SingleOrDefaultAsync(cancellationToken);
        if (batch == null)
            return ApiResponse<PagedResult<StudentAttendanceReportSubjectDto>>.ErrorResponse(
                "Academic batch not found.", 404);
        if (!user.IsTenantAdmin && !await (from access in db.UserCampusAccesses.AsNoTracking()
            join campus in db.Campuses.AsNoTracking() on access.CampusId equals campus.Id
            where access.TenantId == tenant && campus.TenantId == tenant &&
                access.UserId == user.UserId && access.CampusId == batch.CampusId &&
                access.IsActive && !access.IsDeleted && campus.IsActive && !campus.IsDeleted
            select access.Id).AnyAsync(cancellationToken))
            return ApiResponse<PagedResult<StudentAttendanceReportSubjectDto>>.ErrorResponse(
                "Academic batch not found.", 404);

        var query = from offering in db.SubjectOfferings.AsNoTracking()
            join mapping in db.CurriculumSubjects.AsNoTracking()
                on offering.CurriculumSubjectId equals mapping.Id
            join subject in db.Subjects.AsNoTracking() on mapping.SubjectId equals subject.Id
            where offering.TenantId == tenant && mapping.TenantId == tenant &&
                subject.TenantId == tenant && offering.AcademicBatchId == academicBatchId &&
                !offering.IsDeleted && !mapping.IsDeleted && !subject.IsDeleted
            select new StudentAttendanceReportSubjectDto {
                Id = offering.Id, Code = offering.Code,
                SubjectCode = subject.Code, SubjectName = subject.Name
            };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Code.StartsWith(term) ||
                x.SubjectCode.StartsWith(term) || x.SubjectName.StartsWith(term));
        }
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(x => x.SubjectName).ThenBy(x => x.Code)
            .ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<StudentAttendanceReportSubjectDto>>.SuccessResponse(
            new PagedResult<StudentAttendanceReportSubjectDto> {
                Items = rows, TotalCount = count, Page = page, PageSize = pageSize
            });
    }

    public async Task<ApiResponse<PagedResult<StudentAttendanceReportRowDto>>> GetAsync(
        StudentAttendanceReportQueryDto query, CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || user.TenantId <= 0 ||
            !(user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("VicePrincipal")))
            return ApiResponse<PagedResult<StudentAttendanceReportRowDto>>.ErrorResponse(
                "Attendance report permission is required.", 403);

        if (query == null || query.AcademicBatchId <= 0 || query.FromDate == default ||
            query.ToDate == default || query.ToDate < query.FromDate ||
            query.ToDate.DayNumber - query.FromDate.DayNumber > 30 ||
            query.Page < 1 || query.PageSize is < 1 or > 100 ||
            query.Page > int.MaxValue / query.PageSize)
            return ApiResponse<PagedResult<StudentAttendanceReportRowDto>>.ErrorResponse(
                "Select a valid batch and a date range of at most 31 days, with page size 1–100.");

        try
        {
            var tenant = user.TenantId;
            var batch = await db.AcademicBatches.AsNoTracking()
                .Where(x => x.TenantId == tenant && x.Id == query.AcademicBatchId && !x.IsDeleted)
                .Select(x => new { x.Id, x.CampusId })
                .SingleOrDefaultAsync(cancellationToken);
            if (batch == null)
                return ApiResponse<PagedResult<StudentAttendanceReportRowDto>>.ErrorResponse(
                    "Academic batch not found.", 404);

            // Live campus grants apply to every manager report read, including after revocation.
            if (!user.IsTenantAdmin)
            {
                var allowed = await (from access in db.UserCampusAccesses.AsNoTracking()
                    join campus in db.Campuses.AsNoTracking() on access.CampusId equals campus.Id
                    where access.TenantId == tenant && campus.TenantId == tenant &&
                          access.UserId == user.UserId && access.CampusId == batch.CampusId &&
                          access.IsActive && !access.IsDeleted && campus.IsActive && !campus.IsDeleted
                    select access.Id).AnyAsync(cancellationToken);
                if (!allowed)
                    return ApiResponse<PagedResult<StudentAttendanceReportRowDto>>.ErrorResponse(
                        "Academic batch not found.", 404);
            }

            if (query.SubjectOfferingId.HasValue &&
                !await db.SubjectOfferings.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == query.SubjectOfferingId.Value &&
                    x.AcademicBatchId == batch.Id, cancellationToken))
                return ApiResponse<PagedResult<StudentAttendanceReportRowDto>>.ErrorResponse(
                    "Subject offering not found for this batch.", 404);

            // Do not use Student.IsCurrent or current roll: old posted marks retain
            // the enrollment and roll that existed when attendance was recorded.
            var grouped =
                from mark in db.StudentAttendances.AsNoTracking()
                join session in db.AttendanceSessions.AsNoTracking()
                    on mark.AttendanceSessionId equals session.Id
                join enrollment in db.StudentEnrollments.AsNoTracking()
                    on mark.StudentEnrollmentId equals enrollment.Id
                join student in db.Students.AsNoTracking()
                    on enrollment.StudentId equals student.Id
                where mark.TenantId == tenant && session.TenantId == tenant &&
                      enrollment.TenantId == tenant && student.TenantId == tenant &&
                      !mark.IsDeleted && !session.IsDeleted && !enrollment.IsDeleted &&
                      !student.IsDeleted && session.IsFinalized &&
                      session.AcademicBatchId == batch.Id &&
                      (!query.SubjectOfferingId.HasValue ||
                       session.SubjectOfferingId == query.SubjectOfferingId.Value) &&
                      enrollment.AcademicBatchId == batch.Id &&
                      enrollment.CampusId == batch.CampusId &&
                      session.AttendanceDate >= query.FromDate &&
                      session.AttendanceDate <= query.ToDate
                group mark by new {
                    enrollment.PublicId, enrollment.RollNo,
                    student.StudentCode, student.FullName
                } into g
                select new StudentAttendanceReportRowDto
                {
                    StudentEnrollmentReference = g.Key.PublicId,
                    RollNo = g.Key.RollNo,
                    StudentCode = g.Key.StudentCode,
                    StudentName = g.Key.FullName,
                    MarkedSessions = g.Count(),
                    Present = g.Count(x => x.State == AttendanceState.Present),
                    Absent = g.Count(x => x.State == AttendanceState.Absent),
                    Late = g.Count(x => x.State == AttendanceState.Late),
                    Leave = g.Count(x => x.State == AttendanceState.Leave),
                    Excused = g.Count(x => x.State == AttendanceState.Excused)
                };

            var total = await grouped.CountAsync(cancellationToken);
            var offset = (int)Math.Min((long)(query.Page - 1) * query.PageSize, int.MaxValue);
            var items = await grouped.OrderBy(x => x.RollNo).ThenBy(x => x.StudentCode)
                .ThenBy(x => x.StudentEnrollmentReference)
                .Skip(offset).Take(query.PageSize).ToListAsync(cancellationToken);
            return ApiResponse<PagedResult<StudentAttendanceReportRowDto>>.SuccessResponse(
                new PagedResult<StudentAttendanceReportRowDto> {
                    Items = items, TotalCount = total, Page = query.Page, PageSize = query.PageSize
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Attendance report failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<PagedResult<StudentAttendanceReportRowDto>>.ErrorResponse(
                "Attendance report could not be loaded.", 500);
        }
    }
}
