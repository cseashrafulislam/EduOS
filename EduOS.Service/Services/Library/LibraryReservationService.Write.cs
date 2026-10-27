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
    public async Task<ApiResponse<BookReservationDto>> ReserveBookAsync(ReserveBookRequestDto request, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<BookReservationDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.BookReference == Guid.Empty)
            return Error<BookReservationDto>("Book and request IDs are required.");
        var tenant = _user.TenantId;
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var students = _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted);
                var student = request.StudentReference == Guid.Empty
                    ? await students.FirstOrDefaultAsync(x => x.UserId == _user.UserId, token)
                    : await students.FirstOrDefaultAsync(x => x.PublicId == request.StudentReference, token);
                if (student == null || student.StatusCode != "Active") return Error<BookReservationDto>("Active student not found.", 404);
                if (!CanManage() && student.UserId != _user.UserId) return Denied<BookReservationDto>();
                var book = await _books.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.PublicId == request.BookReference && x.IsActive && !x.IsDeleted, token);
                if (book == null) return Error<BookReservationDto>("Active book not found.", 404);
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.StudentId == student.Id && x.IsCurrent &&
                    x.State == EnrollmentState.Active && !x.IsDeleted, token);
                if (enrollment == null) return Error<BookReservationDto>("Current active enrollment is required.", 409);
                if (CanManage() && !await CanManageCampusAsync(enrollment.CampusId, token))
                    return Denied<BookReservationDto>();
                var replay = await _reservations.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, token);
                if (replay != null)
                {
                    if (replay.StudentId != student.Id || replay.BookId != book.Id)
                        return Error<BookReservationDto>("Client request ID was reused with different details.", 409);
                    return ApiResponse<BookReservationDto>.SuccessResponse(Map(replay, book, student, Now), "Reservation already processed.");
                }
                var branchIds = await _branches.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.CampusId == enrollment.CampusId && x.IsActive && !x.IsDeleted)
                    .Select(x => x.Id).ToArrayAsync(token);
                if (!await _copies.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.BookId == book.Id && !x.IsDeleted &&
                    (x.State == BookCopyState.Available || x.State == BookCopyState.Issued) &&
                    (x.LibraryBranchId == null || branchIds.Contains(x.LibraryBranchId.Value)), token))
                    return Error<BookReservationDto>("No circulating copy is assigned to the student's campus.", 409);
                var now = Now;
                if (await _reservations.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.StudentId == student.Id && x.BookId == book.Id && !x.IsDeleted &&
                    x.State == BookReservationState.Pending && x.ExpiresAt > now, token))
                    return Error<BookReservationDto>("An active reservation already exists.", 409);
                var row = new BookReservation
                {
                    TenantId = tenant, BookId = book.Id, StudentId = student.Id,
                    ClientRequestId = request.ClientRequestId, ReservedAt = now, ExpiresAt = now.AddDays(7),
                    State = BookReservationState.Pending, CreatedAt = now, CreatedBy = _user.UserId
                };
                await _reservations.AddAsync(row); await _uow.SaveChangesAsync(token);
                return ApiResponse<BookReservationDto>.SuccessResponse(Map(row, book, student, now), "Reservation placed.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<BookReservationDto>("Reservation changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Reservation conflict for tenant {TenantId}", tenant);
            return Error<BookReservationDto>("Reservation conflict. Retry with the same request ID.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Reservation failed for tenant {TenantId}", tenant);
            return Error<BookReservationDto>("Reservation could not be saved.", 500);
        }
    }
    public async Task<ApiResponse<BookReservationDto>> CancelReservationAsync(long reservationId, string rowVersion, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<BookReservationDto>();
        if (reservationId <= 0) return Error<BookReservationDto>("Invalid reservation ID.");
        var tenant = _user.TenantId;
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var row = await _reservations.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == reservationId && !x.IsDeleted, token);
                if (row == null) return Error<BookReservationDto>("Reservation not found.", 404);
                if (CanManage())
                {
                    if (!await CanManageReservationAsync(row.StudentId, token)) return Denied<BookReservationDto>();
                }
                else if (!await _students.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == row.StudentId && x.UserId == _user.UserId, token))
                    return Denied<BookReservationDto>();
                if (row.State == BookReservationState.Cancelled)
                    return ApiResponse<BookReservationDto>.SuccessResponse(await MapAsync(row, token), "Reservation already cancelled.");
                if (row.State != BookReservationState.Pending || row.ExpiresAt <= Now)
                    return Error<BookReservationDto>("Only active pending reservations may be cancelled.", 409);
                if (!Matches(row.RowVersion, rowVersion))
                    return Error<BookReservationDto>("Reservation changed. Reload and retry.", 409);
                row.State = BookReservationState.Cancelled;
                row.UpdatedAt = Now; row.UpdatedBy = _user.UserId;
                await _uow.SaveChangesAsync(token);
                return ApiResponse<BookReservationDto>.SuccessResponse(await MapAsync(row, token), "Reservation cancelled.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<BookReservationDto>("Reservation changed concurrently.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Cancellation failed for tenant {TenantId}", tenant);
            return Error<BookReservationDto>("Reservation could not be cancelled.", 500);
        }
    }
}