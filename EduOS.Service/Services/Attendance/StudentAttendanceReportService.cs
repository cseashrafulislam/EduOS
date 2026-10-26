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
            query.Page < 1 || query.PageSize is < 1 or > 100)
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
