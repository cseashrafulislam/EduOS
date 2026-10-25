using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Entities.HR;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Library;

public sealed partial class LibraryService
{
    /// <summary>Staff register for outstanding, overdue and historical loans; never exposes another tenant.</summary>
    public async Task<ApiResponse<PagedResult<LibraryCirculationRowDto>>> GetCirculationRegisterAsync(
        string? search, string? status, long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<LibraryCirculationRowDto>>();
        status = (status ?? "issued").Trim().ToLowerInvariant();
        if (status is not ("issued" or "overdue" or "returned" or "all") ||
            page < 1 || pageSize is < 1 or > 100 || search?.Length > 100 || campusId is <= 0)
            return ApiResponse<PagedResult<LibraryCirculationRowDto>>.ErrorResponse("Invalid circulation filters or pagination.");
        if (campusId.HasValue && _branches == null)
            return ApiResponse<PagedResult<LibraryCirculationRowDto>>.ErrorResponse("Campus branch lookup is unavailable.", 503);

        var tenant = _user.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var query = from issue in _issues.GetQueryable().AsNoTracking()
                    join copy in _copies.GetQueryable().AsNoTracking() on issue.BookCopyId equals copy.Id
                    join book in _books.GetQueryable().AsNoTracking() on copy.BookId equals book.Id
                    join student in _students.GetQueryable().AsNoTracking() on issue.StudentId equals student.Id
                    where issue.TenantId == tenant && copy.TenantId == tenant &&
                          book.TenantId == tenant && student.TenantId == tenant &&
                          !issue.IsDeleted && !copy.IsDeleted && !book.IsDeleted && !student.IsDeleted
                    select new { Issue = issue, Copy = copy, Book = book, Student = student };
        if (status == "issued") query = query.Where(x => x.Issue.State == BookIssueState.Issued);
        if (status == "returned") query = query.Where(x => x.Issue.State == BookIssueState.Returned);
        if (status == "overdue") query = query.Where(x =>
            x.Issue.State == BookIssueState.Issued && x.Issue.DueDate < today);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Student.FullName.Contains(term) ||
                x.Student.StudentCode.Contains(term) || x.Book.Title.Contains(term) ||
                x.Copy.AccessionNumber.Contains(term));
        }
        if (IsCampusScopedLibrarian)
        {
            var staffCampuses = StaffCampusIds(tenant, today);
            if (staffCampuses == null || _branches == null)
                return Denied<PagedResult<LibraryCirculationRowDto>>();
            var permittedBranches = _branches.GetQueryable().AsNoTracking()
                .Where(b => b.TenantId == tenant && !b.IsDeleted && staffCampuses.Contains(b.CampusId))
                .Select(b => b.Id);
            query = query.Where(x => x.Copy.LibraryBranchId.HasValue &&
                permittedBranches.Contains(x.Copy.LibraryBranchId.Value));
        }
        if (campusId.HasValue)
        {
            var campus = campusId.Value;
            var matchingBranches = _branches!.GetQueryable().AsNoTracking()
                .Where(b => b.TenantId == tenant && !b.IsDeleted && b.CampusId == campus)
                .Select(b => b.Id);
            query = query.Where(x => x.Copy.LibraryBranchId.HasValue &&
                matchingBranches.Contains(x.Copy.LibraryBranchId.Value));
        }
        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue)
            return ApiResponse<PagedResult<LibraryCirculationRowDto>>.ErrorResponse("Page exceeds the supported range.");
        var total = await query.CountAsync(ct);
        var selected = await query.OrderBy(x => x.Issue.DueDate).ThenBy(x => x.Issue.Id)
            .Skip((int)offset).Take(pageSize).ToListAsync(ct);
        var branchIds = selected.Where(x => x.Copy.LibraryBranchId.HasValue)
            .Select(x => x.Copy.LibraryBranchId!.Value).Distinct().ToArray();
        var branches = _branches == null || branchIds.Length == 0
            ? new Dictionary<long, long>()
            : await _branches.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted && branchIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.CampusId, ct);
        var rows = selected.Select(x =>
        {
            var overdue = x.Issue.State == BookIssueState.Issued && x.Issue.DueDate < today;
            return new LibraryCirculationRowDto
            {
                Issue = MapIssue(x.Issue, x.Copy, x.Book, x.Student),
                CampusId = x.Copy.LibraryBranchId.HasValue &&
                    branches.TryGetValue(x.Copy.LibraryBranchId.Value, out var campus) ? campus : null,
                IsOverdue = overdue,
                DaysOverdue = overdue ? today.DayNumber - x.Issue.DueDate.DayNumber : 0
            };
        }).ToList();
        return ApiResponse<PagedResult<LibraryCirculationRowDto>>.SuccessResponse(
            new PagedResult<LibraryCirculationRowDto>
            { Items = rows, TotalCount = total, Page = page, PageSize = pageSize });
    }

    private bool IsCampusScopedLibrarian =>
        !_user.IsTenantAdmin && !_user.IsInRole("Principal") && _user.IsInRole("Librarian");

    private IQueryable<long>? StaffCampusIds(long tenant, DateOnly today)
    {
        if (_staffEmployees == null || _staffCampusAssignments == null) return null;
        var employeeIds = _staffEmployees.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.UserId == _user.UserId &&
                x.State == EmployeeState.Active && !x.IsDeleted)
            .Select(x => x.Id);
        return _staffCampusAssignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                employeeIds.Contains(x.EmployeeId) && x.EffectiveFrom <= today &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today))
            .Select(x => x.CampusId);
    }

    private async Task<bool> StaffCanManageBranchAsync(long? branchId, CancellationToken ct)
    {
        if (!IsCampusScopedLibrarian) return true;
        if (!branchId.HasValue || _branches == null) return false;
        var tenant = _user.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var campuses = StaffCampusIds(tenant, today);
        return campuses != null && await _branches.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == branchId.Value && !x.IsDeleted &&
            campuses.Contains(x.CampusId), ct);
    }
}
