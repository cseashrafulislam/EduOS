using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Academic;

/// <summary>
/// Read-only enrollment history. Uses the original enrollment's roll, campus and dates;
/// catalog labels are explicitly display-only, never used to reconstruct historical identity.
/// </summary>
public sealed class StudentEnrollmentHistoryQueryService(
    EduOSDbContext db, ICurrentUserService user) : IStudentEnrollmentHistoryQueryService
{
    public async Task<ApiResponse<PagedResult<StudentEnrollmentHistoryRowDto>>> GetAsync(
        Guid studentReference, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || !user.IsTenantAdmin || user.TenantId <= 0)
            return ApiResponse<PagedResult<StudentEnrollmentHistoryRowDto>>.ErrorResponse(
                "Institution administrator access required.", 403);
        if (studentReference == Guid.Empty || page < 1 || pageSize is < 1 or > 100 ||
            page > int.MaxValue / pageSize)
            return ApiResponse<PagedResult<StudentEnrollmentHistoryRowDto>>.ErrorResponse(
                "A valid student, page and page size (1-100) are required.");

        var tenant = user.TenantId;
        // Do not require Active status: exited/transferred learners still have institutional history.
        var studentId = await db.Students.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.PublicId == studentReference && !x.IsDeleted)
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(ct);
        if (studentId is null)
            return ApiResponse<PagedResult<StudentEnrollmentHistoryRowDto>>.ErrorResponse(
                "Student not found.", 404);

        var enrollments = db.StudentEnrollments.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.StudentId == studentId.Value && !x.IsDeleted);
        var total = await enrollments.CountAsync(ct);

        // Correlated, tenant-bound catalog lookups keep deleted/renamed catalog records from
        // suppressing an enrollment. They are NOT a claim of historical name snapshots.
        var rows = await enrollments.OrderByDescending(x => x.EnrollmentDate)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.PublicId, x.CampusId, x.AcademicBatchId, x.RollNo, x.RegistrationNo,
                x.EnrollmentDate, x.EndDate, x.State, x.IsCurrent, x.RowVersion,
                BatchCode = db.AcademicBatches
                    .Where(b => b.TenantId == tenant && b.Id == x.AcademicBatchId &&
                                b.CampusId == x.CampusId)
                    .Select(b => b.Code).FirstOrDefault(),
                BatchName = db.AcademicBatches
                    .Where(b => b.TenantId == tenant && b.Id == x.AcademicBatchId &&
                                b.CampusId == x.CampusId)
                    .Select(b => b.Name).FirstOrDefault(),
                CurriculumName = db.AcademicCurriculums
                    .Where(c => c.TenantId == tenant && c.Id == x.AcademicCurriculumId)
                    .Select(c => c.Name).FirstOrDefault()
            }).ToListAsync(ct);

        var items = rows.Select(x => new StudentEnrollmentHistoryRowDto
        {
            EnrollmentReference = x.PublicId, CampusId = x.CampusId,
            AcademicBatchId = x.AcademicBatchId, BatchCode = x.BatchCode ?? string.Empty,
            BatchName = x.BatchName ?? string.Empty,
            CurriculumName = x.CurriculumName ?? string.Empty,
            RollNo = x.RollNo, RegistrationNo = x.RegistrationNo,
            EnrollmentDate = x.EnrollmentDate, EndDate = x.EndDate,
            State = x.State.ToString(), IsCurrent = x.IsCurrent,
            RowVersion = Convert.ToBase64String(x.RowVersion)
        }).ToList();

        return ApiResponse<PagedResult<StudentEnrollmentHistoryRowDto>>.SuccessResponse(
            new PagedResult<StudentEnrollmentHistoryRowDto>
            {
                Items = items, TotalCount = total, Page = page, PageSize = pageSize
            });
    }
}
