using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Library;

public sealed partial class LibraryReservationService
{
    // Eligible physical copies are selected from the reservation student's campus.
    public async Task<ApiResponse<PagedResult<BookCopyDto>>> GetEligibleCopiesAsync(
        long reservationId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<BookCopyDto>>();
        if (reservationId <= 0 || page < 1 || pageSize is < 1 or > 100)
            return Error<PagedResult<BookCopyDto>>("Invalid reservation or pagination.");
        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue) return Error<PagedResult<BookCopyDto>>("Page exceeds supported range.");
        var tenant = _user.TenantId;
        var now = Now;
        var reservation = await _reservations.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == reservationId && !x.IsDeleted &&
            x.State == BookReservationState.Pending && x.ExpiresAt > now, ct);
        if (reservation == null) return Error<PagedResult<BookCopyDto>>("Active reservation not found.", 404);
        var campusId = await _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.StudentId == reservation.StudentId &&
                x.IsCurrent && x.State == EnrollmentState.Active && !x.IsDeleted)
            .Select(x => (long?)x.CampusId).FirstOrDefaultAsync(ct);
        if (!campusId.HasValue) return Error<PagedResult<BookCopyDto>>("Current active enrollment required.", 409);
        if (!await CanManageCampusAsync(campusId.Value, ct)) return Denied<PagedResult<BookCopyDto>>();
        var book = await _books.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == reservation.BookId && x.IsActive && !x.IsDeleted, ct);
        if (book == null) return Error<PagedResult<BookCopyDto>>("Active book not found.", 404);
        var branches = _branches.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.CampusId == campusId.Value && x.IsActive && !x.IsDeleted)
            .Select(x => x.Id);
        var query = _copies.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.BookId == book.Id && !x.IsDeleted &&
            x.State == BookCopyState.Available &&
            (x.LibraryBranchId.HasValue && branches.Contains(x.LibraryBranchId.Value) ||
                _user.IsTenantAdmin && !x.LibraryBranchId.HasValue) &&
            !_issues.GetQueryable().Any(i => i.TenantId == tenant && !i.IsDeleted &&
                i.BookCopyId == x.Id && i.State == BookIssueState.Issued));
        var total = await query.CountAsync(ct);
        var copies = await query.OrderBy(x => x.AccessionNumber).ThenBy(x => x.Id)
            .Skip((int)offset).Take(pageSize).Select(x => new BookCopyDto
            {
                Id = x.Id, BookReference = book.PublicId, BookTitle = book.Title,
                LibraryBranchId = x.LibraryBranchId, AccessionNumber = x.AccessionNumber,
                Barcode = x.Barcode, ShelfNumber = x.ShelfNumber, State = x.State,
                RowVersion = Convert.ToBase64String(x.RowVersion)
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<BookCopyDto>>.SuccessResponse(
            new PagedResult<BookCopyDto> { Items = copies, TotalCount = total, Page = page, PageSize = pageSize });
    }
}
