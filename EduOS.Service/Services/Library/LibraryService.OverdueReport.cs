using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Library;

public sealed partial class LibraryService
{
    /// <summary>SQL-grouped, campus-authorized overdue ageing by physical branch; never materializes borrower records.</summary>
    public async Task<ApiResponse<PagedResult<LibraryOverdueBranchDto>>> GetOverdueBranchesAsync(
        string? search, long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<LibraryOverdueBranchDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || search?.Length > 100 || campusId is <= 0)
            return ApiResponse<PagedResult<LibraryOverdueBranchDto>>.ErrorResponse("Invalid overdue report filters or pagination.");
        if (_branches == null)
            return ApiResponse<PagedResult<LibraryOverdueBranchDto>>.ErrorResponse("Library branch lookup is unavailable.", 503);

        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue)
            return ApiResponse<PagedResult<LibraryOverdueBranchDto>>.ErrorResponse("Page exceeds the supported range.");
        var tenant = _user.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var last7 = today.AddDays(-7);
        var last30 = today.AddDays(-30);
        var branches = _branches.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted);
        if (campusId.HasValue) branches = branches.Where(x => x.CampusId == campusId.Value);
        if (IsCampusScopedStaff)
        {
            var allowed = AllowedStaffCampusIds(tenant, today);
            if (allowed == null) return Denied<PagedResult<LibraryOverdueBranchDto>>();
            branches = branches.Where(x => allowed.Contains(x.CampusId));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            branches = branches.Where(x => x.Name.Contains(term) || x.Code.Contains(term));
        }

        var overdue = from issue in _issues.GetQueryable().AsNoTracking()
                      join copy in _copies.GetQueryable().AsNoTracking() on issue.BookCopyId equals copy.Id
                      join book in _books.GetQueryable().AsNoTracking() on copy.BookId equals book.Id
                      join branch in branches on copy.LibraryBranchId equals (long?)branch.Id
                      where issue.TenantId == tenant && copy.TenantId == tenant && book.TenantId == tenant &&
                            !issue.IsDeleted && !copy.IsDeleted && !book.IsDeleted &&
                            issue.State == BookIssueState.Issued && issue.DueDate < today
                      select new
                      {
                          BranchId = branch.Id, branch.CampusId, BranchCode = branch.Code,
                          BranchName = branch.Name, issue.DueDate
                      };

        var grouped = overdue.GroupBy(x => new { x.BranchId, x.CampusId, x.BranchCode, x.BranchName })
            .Select(g => new LibraryOverdueBranchDto
            {
                BranchId = g.Key.BranchId,
                CampusId = g.Key.CampusId,
                BranchCode = g.Key.BranchCode,
                BranchName = g.Key.BranchName,
                TotalOverdue = g.Count(),
                Days1To7 = g.Count(x => x.DueDate >= last7),
                Days8To30 = g.Count(x => x.DueDate < last7 && x.DueDate >= last30),
                Days31Plus = g.Count(x => x.DueDate < last30),
                OldestDueDate = g.Min(x => x.DueDate)
            });
        var total = await grouped.CountAsync(ct);
        var rows = await grouped.OrderBy(x => x.CampusId).ThenBy(x => x.BranchCode)
            .ThenBy(x => x.BranchId).Skip((int)offset).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<LibraryOverdueBranchDto>>.SuccessResponse(
            new PagedResult<LibraryOverdueBranchDto>
            {
                Items = rows, TotalCount = total, Page = page, PageSize = pageSize
            });
    }
}
