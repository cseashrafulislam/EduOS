using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Academic;

/// <summary>
/// Historical subject registrations, bound to the original enrollment rather than
/// the student's current enrollment. Read-only; never mutates catalog or history.
/// </summary>
public sealed class StudentSubjectHistoryQueryService(
    EduOSDbContext db, ICurrentUserService user) : IStudentSubjectHistoryQueryService
{
    public async Task<ApiResponse<PagedResult<StudentSubjectHistoryRowDto>>> GetAsync(
        Guid studentReference, int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || !user.IsTenantAdmin || user.TenantId <= 0)
            return ApiResponse<PagedResult<StudentSubjectHistoryRowDto>>.ErrorResponse(
                "Institution administrator access required.", 403);
        if (studentReference == Guid.Empty || page < 1 || pageSize is < 1 or > 100 ||
            page > int.MaxValue / pageSize)
            return ApiResponse<PagedResult<StudentSubjectHistoryRowDto>>.ErrorResponse(
                "A valid student, page and page size (1-100) are required.");

        var tenant = user.TenantId;
        // Former learners remain eligible for institutional historical readback.
        var studentId = await db.Students.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.PublicId == studentReference && !x.IsDeleted)
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(ct);
        if (studentId is null)
            return ApiResponse<PagedResult<StudentSubjectHistoryRowDto>>.ErrorResponse(
                "Student not found.", 404);

        var registrations =
            from enrollment in db.StudentEnrollments.AsNoTracking()
            join registration in db.StudentSubjectRegistrations.AsNoTracking()
                on enrollment.Id equals registration.StudentEnrollmentId
            where enrollment.TenantId == tenant && registration.TenantId == tenant &&
                  enrollment.StudentId == studentId.Value &&
                  !enrollment.IsDeleted && !registration.IsDeleted
            select new { enrollment, registration };

        var total = await registrations.CountAsync(ct);
        // Resolve current catalog display labels with tenant-bound correlated lookups.
        // A removed offering must not erase the registration's original roll/state.
        var rows = await registrations
            .OrderByDescending(x => x.registration.RegisteredAt)
            .ThenByDescending(x => x.registration.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.enrollment.PublicId, x.enrollment.CampusId,
                x.enrollment.AcademicBatchId, x.enrollment.RollNo,
                x.enrollment.EnrollmentDate, EnrollmentState = x.enrollment.State,
                x.enrollment.IsCurrent,
                x.registration.CreditHoursSnapshot, x.registration.RegisteredAt,
                x.registration.ApprovedAt, x.registration.Remarks,
                RegistrationState = x.registration.State,
                SubjectCode = (
                    from offering in db.SubjectOfferings
                    join curriculumSubject in db.CurriculumSubjects
                        on offering.CurriculumSubjectId equals curriculumSubject.Id
                    join subject in db.Subjects
                        on curriculumSubject.SubjectId equals subject.Id
                    where offering.Id == x.registration.SubjectOfferingId &&
                          offering.TenantId == tenant &&
                          curriculumSubject.TenantId == tenant &&
                          subject.TenantId == tenant &&
                          !offering.IsDeleted && !curriculumSubject.IsDeleted &&
                          !subject.IsDeleted
                    select subject.Code).FirstOrDefault(),
                SubjectName = (
                    from offering in db.SubjectOfferings
                    join curriculumSubject in db.CurriculumSubjects
                        on offering.CurriculumSubjectId equals curriculumSubject.Id
                    join subject in db.Subjects
                        on curriculumSubject.SubjectId equals subject.Id
                    where offering.Id == x.registration.SubjectOfferingId &&
                          offering.TenantId == tenant &&
                          curriculumSubject.TenantId == tenant &&
                          subject.TenantId == tenant &&
                          !offering.IsDeleted && !curriculumSubject.IsDeleted &&
                          !subject.IsDeleted
                    select subject.Name).FirstOrDefault()
            }).ToListAsync(ct);

        var items = rows.Select(x => new StudentSubjectHistoryRowDto
        {
            EnrollmentReference = x.PublicId,
            CampusId = x.CampusId, AcademicBatchId = x.AcademicBatchId,
            RollNo = x.RollNo, EnrollmentDate = x.EnrollmentDate,
            EnrollmentState = x.EnrollmentState.ToString(),
            IsCurrentEnrollment = x.IsCurrent,
            SubjectCode = x.SubjectCode ?? string.Empty,
            SubjectName = x.SubjectName ?? string.Empty,
            CreditHoursSnapshot = x.CreditHoursSnapshot,
            RegistrationState = x.RegistrationState.ToString(),
            RegisteredAt = x.RegisteredAt, ApprovedAt = x.ApprovedAt,
            Remarks = x.Remarks
        }).ToList();

        return ApiResponse<PagedResult<StudentSubjectHistoryRowDto>>.SuccessResponse(
            new PagedResult<StudentSubjectHistoryRowDto>
            {
                Items = items, TotalCount = total, Page = page, PageSize = pageSize
            });
    }
}
