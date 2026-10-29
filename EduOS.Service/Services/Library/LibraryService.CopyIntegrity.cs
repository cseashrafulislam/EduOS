using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Library;

public sealed partial class LibraryService
{
    /// <summary>
    /// Read-only discrepancy report. Does not repair historical copy states or touch borrower data.
    /// SQL groups outstanding loans once and filters before server paging.
    /// </summary>
    public async Task<ApiResponse<PagedResult<LibraryCopyIntegrityDto>>> GetCopyIntegrityAsync(
        string? accession, long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<LibraryCopyIntegrityDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || accession?.Length > 100 || campusId is <= 0)
            return ApiResponse<PagedResult<LibraryCopyIntegrityDto>>.ErrorResponse("Invalid integrity report filters.");
        if (_branches == null)
            return ApiResponse<PagedResult<LibraryCopyIntegrityDto>>.ErrorResponse("Library branch lookup unavailable.", 503);
        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue)
            return ApiResponse<PagedResult<LibraryCopyIntegrityDto>>.ErrorResponse("Page exceeds supported range.");

        var tenant = _user.TenantId;
        var branches = _branches.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted);
        var copies = _copies.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted);
        // Scope the COPY before any left join. A missing/ungranted branch must never
        // appear to campus-scoped staff as an apparent "missing branch" anomaly.
        if (IsCampusScopedStaff)
        {
            var allowed = AllowedStaffCampusIds(tenant,
                DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime));
            if (allowed == null) return Denied<PagedResult<LibraryCopyIntegrityDto>>();
            var permitted = branches.Where(x => allowed.Contains(x.CampusId)).Select(x => x.Id);
            copies = copies.Where(x => x.LibraryBranchId.HasValue &&
                permitted.Contains(x.LibraryBranchId.Value));
        }
        if (campusId.HasValue)
        {
            var matching = branches.Where(x => x.CampusId == campusId.Value).Select(x => x.Id);
            copies = copies.Where(x => x.LibraryBranchId.HasValue &&
                matching.Contains(x.LibraryBranchId.Value));
        }
        if (!string.IsNullOrWhiteSpace(accession))
        {
            var term = accession.Trim();
            copies = copies.Where(x => x.AccessionNumber.Contains(term));
        }
        var books = _books.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted);
        var openLoanCounts = _issues.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                x.State == BookIssueState.Issued)
            .GroupBy(x => x.BookCopyId)
            .Select(g => new { CopyId = g.Key, OpenLoans = g.Count() });

        var projected =
            from copy in copies
            join book in books on copy.BookId equals book.Id into bookGroup
            from book in bookGroup.DefaultIfEmpty()
            join branch in branches on copy.LibraryBranchId equals (long?)branch.Id into branchGroup
            from branch in branchGroup.DefaultIfEmpty()
            join loan in openLoanCounts on copy.Id equals loan.CopyId into loanGroup
            select new
            {
                CopyId = copy.Id, copy.AccessionNumber, CopyState = copy.State,
                BranchId = copy.LibraryBranchId,
                BookTitle = book == null ? null : book.Title,
                BookExists = book != null,
                BranchCode = branch == null ? null : branch.Code,
                CampusId = branch == null ? (long?)null : branch.CampusId,
                BranchExists = branch != null,
                // Sum on an empty group is zero. Avoid dereferencing a missing
                // left-joined aggregate (also fails on EF InMemory test provider).
                OpenLoanCount = loanGroup.Sum(x => x.OpenLoans)
            };

        var anomalies = projected.Where(x =>
            !x.BookExists ||
            (x.BranchId.HasValue && !x.BranchExists) ||
            x.OpenLoanCount > 1 ||
            (x.CopyState == BookCopyState.Issued && x.OpenLoanCount == 0) ||
            (x.CopyState != BookCopyState.Issued && x.OpenLoanCount > 0));
        var total = await anomalies.CountAsync(ct);
        var pageRows = await anomalies.OrderBy(x => x.CopyId)
            .Skip((int)offset).Take(pageSize).ToListAsync(ct);
        var rows = pageRows.Select(x => new LibraryCopyIntegrityDto
        {
            CopyId = x.CopyId,
            AccessionNumber = x.AccessionNumber,
            BookTitle = x.BookTitle ?? "(missing book)",
            BranchCode = x.BranchCode,
            CampusId = x.CampusId,
            CopyState = x.CopyState,
            OpenLoanCount = x.OpenLoanCount,
            MissingBook = !x.BookExists,
            MissingBranch = x.BranchId.HasValue && !x.BranchExists,
            StateMismatch = (x.CopyState == BookCopyState.Issued) != (x.OpenLoanCount > 0),
            MultipleOpenLoans = x.OpenLoanCount > 1
        }).ToList();
        return ApiResponse<PagedResult<LibraryCopyIntegrityDto>>.SuccessResponse(
            new PagedResult<LibraryCopyIntegrityDto>
            { Items = rows, TotalCount = total, Page = page, PageSize = pageSize });
    }
}
