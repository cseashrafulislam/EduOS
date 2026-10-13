using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Library;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
namespace EduOS.Service.Services.Library;
public sealed partial class LibraryReservationService
{
    public async Task<ApiResponse<BookReservationDto>> FulfillReservationAsync(long reservationId, long bookCopyId,
        string rowVersion, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<BookReservationDto>();
        if (reservationId <= 0 || bookCopyId <= 0) return Error<BookReservationDto>("Reservation and copy IDs are required.");
        var tenant = _user.TenantId;
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var row = await _reservations.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == reservationId && !x.IsDeleted, token);
                if (row == null) return Error<BookReservationDto>("Reservation not found.", 404);
                var prior = await _issues.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.ClientRequestId == row.ClientRequestId, token);
                if (row.State == BookReservationState.Fulfilled)
                {
                    if (prior == null || prior.BookCopyId != bookCopyId || prior.StudentId != row.StudentId)
                        return Error<BookReservationDto>("Reservation was fulfilled with a different copy.", 409);
                    return ApiResponse<BookReservationDto>.SuccessResponse(await MapAsync(row, token), "Reservation already fulfilled.");
                }
                var now = Now;
                if (row.State != BookReservationState.Pending || row.ExpiresAt <= now)
                    return Error<BookReservationDto>("Reservation is no longer pending.", 409);
                if (!Matches(row.RowVersion, rowVersion))
                    return Error<BookReservationDto>("Reservation changed. Reload and retry.", 409);
                var first = await _reservations.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.BookId == row.BookId && !x.IsDeleted &&
                        x.State == BookReservationState.Pending && x.ExpiresAt > now)
                    .OrderBy(x => x.ReservedAt).ThenBy(x => x.Id).Select(x => x.Id).FirstOrDefaultAsync(token);
                if (first != row.Id) return Error<BookReservationDto>("Fulfill the oldest active reservation first.", 409);
                if (prior != null) return Error<BookReservationDto>("Circulation request ID already exists.", 409);
                var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == row.StudentId && x.StatusCode == "Active" && !x.IsDeleted, token);
                if (student == null) return Error<BookReservationDto>("Active student not found.", 409);
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.StudentId == student.Id && x.IsCurrent &&
                    x.State == EnrollmentState.Active && !x.IsDeleted, token);
                if (enrollment == null) return Error<BookReservationDto>("Current active enrollment required.", 409);
                var book = await _books.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == row.BookId && x.IsActive && !x.IsDeleted, token);
                if (book == null) return Error<BookReservationDto>("Book is inactive.", 409);
                var copy = await _copies.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == bookCopyId && x.BookId == book.Id && !x.IsDeleted, token);
                if (copy == null || copy.State != BookCopyState.Available)
                    return Error<BookReservationDto>("Selected copy is not available.", 409);
                if (copy.LibraryBranchId.HasValue && !await _branches.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == copy.LibraryBranchId.Value && x.CampusId == enrollment.CampusId &&
                    x.IsActive && !x.IsDeleted, token))
                    return Error<BookReservationDto>("Copy is assigned to another campus or inactive branch.", 409);
                if (await _issues.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.BookCopyId == copy.Id && x.State == BookIssueState.Issued, token))
                    return Error<BookReservationDto>("Copy already has an outstanding issue.", 409);
                var today = DateOnly.FromDateTime(now);
                await _issues.AddAsync(new BookIssue
                {
                    TenantId = tenant, ClientRequestId = row.ClientRequestId, BookCopyId = copy.Id,
                    StudentId = student.Id, IssueDate = today, DueDate = today.AddDays(14),
                    State = BookIssueState.Issued, IssuedByUserId = _user.UserId,
                    CreatedAt = now, CreatedBy = _user.UserId
                });
                copy.State = BookCopyState.Issued; copy.UpdatedAt = now; copy.UpdatedBy = _user.UserId;
                row.State = BookReservationState.Fulfilled; row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
                await _uow.SaveChangesAsync(token);
                return ApiResponse<BookReservationDto>.SuccessResponse(Map(row, book, student, now), "Reservation fulfilled and copy issued.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<BookReservationDto>("Copy or reservation changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Reservation fulfillment conflict in tenant {TenantId}", tenant);
            return Error<BookReservationDto>("Copy or reservation was claimed by another request.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Reservation fulfillment failed in tenant {TenantId}", tenant);
            return Error<BookReservationDto>("Reservation could not be fulfilled.", 500);
        }
    }
}