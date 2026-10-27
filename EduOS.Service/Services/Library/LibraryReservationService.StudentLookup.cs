using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Library;

public sealed partial class LibraryReservationService
{
    public async Task<ApiResponse<PagedResult<BookReservationStudentOptionDto>>> SearchEligibleStudentsAsync(
        string search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<BookReservationStudentOptionDto>>();
        search = search?.Trim() ?? string.Empty;
        if (search.Length is < 2 or > 100 || page < 1 || pageSize is < 1 or > 50 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<BookReservationStudentOptionDto>>("Enter 2–100 characters and valid paging.");

        var tenant = _user.TenantId;
        var query = _students.GetQueryable().AsNoTracking().Where(student =>
            student.TenantId == tenant && !student.IsDeleted && student.StatusCode == "Active" &&
            (student.StudentCode.StartsWith(search) || student.FullName.StartsWith(search)) &&
            _enrollments.GetQueryable().Any(enrollment =>
                enrollment.TenantId == tenant && !enrollment.IsDeleted &&
                enrollment.StudentId == student.Id && enrollment.IsCurrent &&
                enrollment.State == EnrollmentState.Active));

        if (!_user.IsTenantAdmin)
        {
            var allowed = AllowedStaffCampuses(tenant);
            if (allowed == null) return Denied<PagedResult<BookReservationStudentOptionDto>>();
            query = query.Where(student => _enrollments.GetQueryable().Any(e =>
                e.TenantId == tenant && e.StudentId == student.Id && !e.IsDeleted && e.IsCurrent &&
                e.State == EnrollmentState.Active && allowed.Contains(e.CampusId)));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.StudentCode).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new BookReservationStudentOptionDto
            {
                StudentReference = x.PublicId, StudentCode = x.StudentCode, StudentName = x.FullName
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<BookReservationStudentOptionDto>>.SuccessResponse(new PagedResult<BookReservationStudentOptionDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total, Items = items
        });
    }
}
