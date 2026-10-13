using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
namespace EduOS.Service.Services.Library;
public sealed partial class LibraryReservationService : ILibraryReservationService
{
    private readonly IGenericRepository<BookReservation> _reservations;
    private readonly IGenericRepository<Book> _books;
    private readonly IGenericRepository<BookCopy> _copies;
    private readonly IGenericRepository<BookIssue> _issues;
    private readonly IGenericRepository<LibraryBranch> _branches;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<LibraryReservationService> _logger;
    public LibraryReservationService(IGenericRepository<BookReservation> reservations, IGenericRepository<Book> books,
        IGenericRepository<BookCopy> copies, IGenericRepository<BookIssue> issues,
        IGenericRepository<LibraryBranch> branches, IGenericRepository<Student> students,
        IGenericRepository<StudentEnrollment> enrollments, IUnitOfWork uow, ICurrentUserService user,
        TimeProvider clock, ILogger<LibraryReservationService> logger)
    {
        _reservations = reservations; _books = books; _copies = copies; _issues = issues;
        _branches = branches; _students = students; _enrollments = enrollments;
        _uow = uow; _user = user; _clock = clock; _logger = logger;
    }
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0;
    private bool CanManage() => CanRead() && (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("Librarian"));
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Library reservation permission required.", 403);
    private static ApiResponse<T> Error<T>(string message, int code = 400) => ApiResponse<T>.ErrorResponse(message, code);
    private static bool Matches(byte[] version, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try { return version.AsSpan().SequenceEqual(Convert.FromBase64String(text)); }
        catch (FormatException) { return false; }
    }
    private static BookReservationDto Map(BookReservation row, Book? book, Student? student, DateTime now) => new()
    {
        Id = row.Id, BookReference = book?.PublicId ?? Guid.Empty, BookTitle = book?.Title ?? "",
        StudentReference = student?.PublicId ?? Guid.Empty, StudentName = student?.FullName ?? "",
        ReservedAt = row.ReservedAt, ExpiresAt = row.ExpiresAt,
        State = row.State == BookReservationState.Pending && row.ExpiresAt <= now ? BookReservationState.Expired : row.State,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };
    private async Task<BookReservationDto> MapAsync(BookReservation row, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var book = await _books.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == row.BookId, ct);
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == row.StudentId, ct);
        return Map(row, book, student, Now);
    }
    private async Task<IReadOnlyList<BookReservationDto>> MapRowsAsync(List<BookReservation> rows, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var bookIds = rows.Select(x => x.BookId).Distinct().ToArray();
        var studentIds = rows.Select(x => x.StudentId).Distinct().ToArray();
        var books = await _books.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && bookIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var students = await _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && studentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        return rows.Select(x => Map(x, books.GetValueOrDefault(x.BookId), students.GetValueOrDefault(x.StudentId), Now)).ToList();
    }
    public async Task<ApiResponse<PagedResult<BookReservationDto>>> GetMyReservationsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<PagedResult<BookReservationDto>>();
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var tenant = _user.TenantId;
        var studentIds = _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && x.UserId == _user.UserId && !x.IsDeleted).Select(x => x.Id);
        var query = _reservations.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted && studentIds.Contains(x.StudentId));
        var total = await query.CountAsync(ct); var offset = ((long)page - 1) * pageSize;
        var rows = offset > int.MaxValue ? new List<BookReservation>() : await query.OrderByDescending(x => x.ReservedAt)
            .ThenByDescending(x => x.Id).Skip((int)offset).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<BookReservationDto>>.SuccessResponse(new PagedResult<BookReservationDto>
        { Items = (await MapRowsAsync(rows, ct)).ToList(), TotalCount = total, Page = page, PageSize = pageSize });
    }
    public async Task<ApiResponse<PagedResult<BookReservationDto>>> GetActiveReservationsForBookAsync(Guid bookReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<BookReservationDto>>();
        if (bookReference == Guid.Empty) return Error<PagedResult<BookReservationDto>>("Book reference is required.");
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var tenant = _user.TenantId;
        var book = await _books.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant && x.PublicId == bookReference && !x.IsDeleted, ct);
        if (book == null) return Error<PagedResult<BookReservationDto>>("Book not found.", 404);
        var now = Now;
        var query = _reservations.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && x.BookId == book.Id &&
            !x.IsDeleted && x.State == BookReservationState.Pending && x.ExpiresAt > now);
        var total = await query.CountAsync(ct); var offset = ((long)page - 1) * pageSize;
        var rows = offset > int.MaxValue ? new List<BookReservation>() : await query.OrderBy(x => x.ReservedAt)
            .ThenBy(x => x.Id).Skip((int)offset).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<BookReservationDto>>.SuccessResponse(new PagedResult<BookReservationDto>
        { Items = (await MapRowsAsync(rows, ct)).ToList(), TotalCount = total, Page = page, PageSize = pageSize });
    }
}