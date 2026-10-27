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
/// Tenant- and campus-bound historical enrollment report. This read-only workflow
/// does not mutate leave decisions or attendance marks.
/// </summary>
public sealed class StudentLeavePeriodReportService(
    EduOSDbContext db, ICurrentUserService user, ILogger<StudentLeavePeriodReportService> logger)
    : IStudentLeavePeriodReportService
{
    public async Task<ApiResponse<PagedResult<StudentLeavePeriodReportRowDto>>> GetAsync(
        StudentLeavePeriodReportQueryDto query, CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || user.TenantId <= 0 ||
            !(user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("VicePrincipal")))
            return ApiResponse<PagedResult<StudentLeavePeriodReportRowDto>>.ErrorResponse(
                "Student leave reporting permission is required.", 403);

        if (query == null || query.AcademicBatchId <= 0 || query.FromDate == default ||
            query.ToDate == default || query.ToDate < query.FromDate ||
            query.ToDate.DayNumber - query.FromDate.DayNumber > 365 ||
            query.Page < 1 || query.PageSize is < 1 or > 100 ||
            query.Page > int.MaxValue / query.PageSize ||
            (query.State.HasValue && !Enum.IsDefined(query.State.Value)))
            return ApiResponse<PagedResult<StudentLeavePeriodReportRowDto>>.ErrorResponse(
                "Select a valid batch, state and period of at most 366 days; page size must be 1–100.");

        try
        {
            var tenant = user.TenantId;
            var batch = await db.AcademicBatches.AsNoTracking()
                .Where(x => x.TenantId == tenant && x.Id == query.AcademicBatchId && !x.IsDeleted)
                .Select(x => new { x.Id, x.CampusId })
                .SingleOrDefaultAsync(cancellationToken);
            if (batch == null)
                return ApiResponse<PagedResult<StudentLeavePeriodReportRowDto>>.ErrorResponse(
                    "Academic batch not found.", 404);

            // Enrollment and roll are the original leave application's identity,
            // never the student's latest/current enrollment or roll number.
            var matches =
                from leave in db.StudentLeaveApplications.AsNoTracking()
                join enrollment in db.StudentEnrollments.AsNoTracking()
                    on leave.StudentEnrollmentId equals enrollment.Id
                join student in db.Students.AsNoTracking()
                    on enrollment.StudentId equals student.Id
                where leave.TenantId == tenant && enrollment.TenantId == tenant &&
                      student.TenantId == tenant && !leave.IsDeleted &&
                      !enrollment.IsDeleted && !student.IsDeleted &&
                      enrollment.AcademicBatchId == batch.Id &&
                      enrollment.CampusId == batch.CampusId &&
                      leave.FromDate <= query.ToDate && leave.ToDate >= query.FromDate
                select new
                {
                    leave.Id, leave.FromDate, leave.ToDate, leave.State, leave.ReviewedAt,
                    EnrollmentReference = enrollment.PublicId, enrollment.RollNo,
                    student.StudentCode, StudentName = student.FullName
                };

            if (query.State.HasValue)
                matches = matches.Where(x => x.State == query.State.Value);

            var total = await matches.CountAsync(cancellationToken);
            var rows = await matches.OrderBy(x => x.FromDate).ThenBy(x => x.Id)
                .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
                .ToListAsync(cancellationToken);
            var items = rows.Select(x =>
            {
                var start = x.FromDate > query.FromDate ? x.FromDate : query.FromDate;
                var end = x.ToDate < query.ToDate ? x.ToDate : query.ToDate;
                return new StudentLeavePeriodReportRowDto
                {
                    ApplicationId = x.Id,
                    StudentEnrollmentReference = x.EnrollmentReference,
                    RollNo = x.RollNo, StudentCode = x.StudentCode,
                    StudentName = x.StudentName, FromDate = x.FromDate,
                    ToDate = x.ToDate, PeriodDays = end.DayNumber - start.DayNumber + 1,
                    State = x.State, ReviewedAt = x.ReviewedAt
                };
            }).ToList();

            return ApiResponse<PagedResult<StudentLeavePeriodReportRowDto>>.SuccessResponse(
                new PagedResult<StudentLeavePeriodReportRowDto>
                {
                    Items = items, TotalCount = total, Page = query.Page, PageSize = query.PageSize
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Student leave report failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<PagedResult<StudentLeavePeriodReportRowDto>>.ErrorResponse(
                "Student leave report could not be loaded.", 500);
        }
    }
}
