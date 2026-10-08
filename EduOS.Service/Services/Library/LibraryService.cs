using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Library;

public sealed class LibraryService : ILibraryService
{
    private readonly IGenericRepository<Book> _books;
    private readonly IGenericRepository<BookCopy> _copies;
    private readonly IGenericRepository<BookIssue> _issues;
    private readonly IGenericRepository<BookCategory> _categories;
    private readonly IGenericRepository<Student> _students;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<LibraryService> _logger;

    public LibraryService(IGenericRepository<Book> books, IGenericRepository<BookCopy> copies,
        IGenericRepository<BookIssue> issues, IGenericRepository<BookCategory> categories,
        IGenericRepository<Student> students, IUnitOfWork unitOfWork,
        ICurrentUserService currentUser, TimeProvider clock, ILogger<LibraryService> logger)
    {
        _books = books; _copies = copies; _issues = issues; _categories = categories;
        _students = students; _uow = unitOfWork; _user = currentUser; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<LibraryBookDto>>> GetCatalogAsync(string? search,
        CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<LibraryBookDto>>();
        var tenant = _user.TenantId;
        var query = _books.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Title.Contains(term) ||
                (x.Author != null && x.Author.Contains(term)) || (x.ISBN != null && x.ISBN.Contains(term)));
        }
        var books = await query.OrderBy(x => x.Title).ThenBy(x => x.Id).Take(200).ToListAsync(ct);
        var ids = books.Select(x => x.Id).ToArray();
        var counts = await _copies.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && ids.Contains(x.BookId))
            .GroupBy(x => x.BookId).Select(g => new { BookId = g.Key, Total = g.Count(),
                Available = g.Count(x => x.State == BookCopyState.Available) }).ToDictionaryAsync(x => x.BookId, ct);
        var categoryIds = books.Where(x => x.BookCategoryId.HasValue).Select(x => x.BookCategoryId!.Value).Distinct().ToArray();
        var categories = await _categories.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            categoryIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        IReadOnlyList<LibraryBookDto> result = books.Select(x =>
        {
            counts.TryGetValue(x.Id, out var count);
            return MapBook(x, categories.GetValueOrDefault(x.BookCategoryId ?? 0), count?.Total ?? 0, count?.Available ?? 0);
        }).ToList();
        return ApiResponse<IReadOnlyList<LibraryBookDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<LibraryBookDto>> SaveBookAsync(SaveLibraryBookDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<LibraryBookDto>();
        if (request == null || string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 300 ||
            request.ReplacementPrice < 0m) return ApiResponse<LibraryBookDto>.ErrorResponse("Book metadata is invalid.");
        var tenant = _user.TenantId;
        if (request.BookCategoryId.HasValue && !await _categories.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == request.BookCategoryId && x.IsActive, ct))
            return ApiResponse<LibraryBookDto>.ErrorResponse("Book category is invalid.");
        try
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            Book book;
            if (request.Reference.HasValue)
            {
                var found = await _books.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                    x.PublicId == request.Reference.Value, ct);
                if (found == null) return ApiResponse<LibraryBookDto>.ErrorResponse("Book not found.", 404);
                if (!MatchesVersion(found.RowVersion, request.RowVersion))
                    return ApiResponse<LibraryBookDto>.ErrorResponse("Book changed. Reload and retry.", 409);
                book = found;
                book.UpdatedAt = now; book.UpdatedBy = _user.UserId;
            }
            else
            {
                book = new Book { TenantId = tenant, CreatedAt = now, CreatedBy = _user.UserId };
                await _books.AddAsync(book);
            }
            book.BookCategoryId = request.BookCategoryId;
            book.Title = request.Title.Trim();
            book.Author = Trim(request.Author); book.Publisher = Trim(request.Publisher);
            book.ISBN = Trim(request.ISBN); book.Edition = Trim(request.Edition);
            book.ReplacementPrice = request.ReplacementPrice;
            book.CoverImageUrl = Trim(request.CoverImageUrl); book.IsActive = request.IsActive;
            await _uow.SaveChangesAsync(ct);
            var totals = await _copies.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && x.BookId == book.Id)
                .GroupBy(x => x.BookId).Select(g => new { Total = g.Count(),
                    Available = g.Count(x => x.State == BookCopyState.Available) }).FirstOrDefaultAsync(ct);
            var categoryName = book.BookCategoryId.HasValue ?
                await _categories.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                    x.Id == book.BookCategoryId.Value).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
            return ApiResponse<LibraryBookDto>.SuccessResponse(
                MapBook(book, categoryName, totals?.Total ?? 0, totals?.Available ?? 0), "Book metadata saved.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<LibraryBookDto>.ErrorResponse("Book changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Book metadata conflict for tenant {TenantId}", tenant);
            return ApiResponse<LibraryBookDto>.ErrorResponse("Book save conflicts with existing records.", 409);
        }
    }

    public async Task<ApiResponse<bool>> ArchiveBookAsync(Guid reference, string rowVersion, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<bool>();
        var tenant = _user.TenantId;
        try
        {
            var book = await _books.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == reference && x.IsActive, ct);
            if (book == null) return ApiResponse<bool>.ErrorResponse("Book not found.", 404);
            if (!MatchesVersion(book.RowVersion, rowVersion))
                return ApiResponse<bool>.ErrorResponse("Book changed. Reload and retry.", 409);
            var hasOutstandingIssue = await (from issue in _issues.GetQueryable().AsNoTracking()
                join copy in _copies.GetQueryable().AsNoTracking() on issue.BookCopyId equals copy.Id
                where issue.TenantId == tenant && copy.TenantId == tenant && copy.BookId == book.Id &&
                    issue.State == BookIssueState.Issued
                select issue.Id).AnyAsync(ct);
            if (hasOutstandingIssue) return ApiResponse<bool>.ErrorResponse("Return outstanding copies first.", 409);
            book.IsActive = false; book.UpdatedAt = _clock.GetUtcNow().UtcDateTime; book.UpdatedBy = _user.UserId;
            await _uow.SaveChangesAsync(ct);
            return ApiResponse<bool>.SuccessResponse(true, "Book archived.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<bool>.ErrorResponse("Book was modified. Reload and retry.", 409);
        }
    }

    public async Task<ApiResponse<LibraryIssueDto>> IssueAsync(IssueBookDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<LibraryIssueDto>();
        if (request == null || request.BookCopyId <= 0 || request.ClientRequestId == Guid.Empty ||
            request.StudentReference == Guid.Empty) return Error("Book copy, student and request ID are required.");
        var issueDate = request.IssueDate == default ? DateOnly.FromDateTime(_clock.GetLocalNow().DateTime) : request.IssueDate;
        if (request.DueDate < issueDate) return Error("Due date must be on or after the issue date.");
        var tenant = _user.TenantId;
        try
        {
            using var tx = new TransactionScope(TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.Serializable },
                TransactionScopeAsyncFlowOption.Enabled);
            var replay = await _issues.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, ct);
            if (replay != null)
            {
                var studentId = await _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                    x.PublicId == request.StudentReference).Select(x => x.Id).FirstOrDefaultAsync(ct);
                if (studentId == 0 || replay.BookCopyId != request.BookCopyId || replay.StudentId != studentId)
                    return Error("Client request ID was reused with different issue details.", 409);
                var prior = await MapIssueAsync(replay, ct);
                tx.Complete();
                return ApiResponse<LibraryIssueDto>.SuccessResponse(prior, "Book issue already processed.");
            }
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == request.StudentReference && x.IsActive, ct);
            if (student == null) return Error("Active student not found.", 404);
            var copy = await _copies.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == request.BookCopyId, ct);
            if (copy == null || copy.State != BookCopyState.Available)
                return Error("The selected copy is not available.", 409);
            if (!await _books.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.Id == copy.BookId && x.IsActive, ct))
                return Error("Book is archived or unavailable.", 409);
            if (await _issues.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.BookCopyId == copy.Id && x.State == BookIssueState.Issued, ct))
                return Error("The selected copy has an outstanding issue.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            var entity = new BookIssue
            {
                TenantId = tenant, ClientRequestId = request.ClientRequestId, BookCopyId = copy.Id,
                StudentId = student.Id, IssueDate = issueDate, DueDate = request.DueDate,
                State = BookIssueState.Issued, IssuedByUserId = _user.UserId,
                CreatedAt = now, CreatedBy = _user.UserId
            };
            copy.State = BookCopyState.Issued; copy.UpdatedAt = now; copy.UpdatedBy = _user.UserId;
            await _issues.AddAsync(entity);
            await _uow.SaveChangesAsync(ct);
            var response = MapIssue(entity, copy,
                await _books.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == copy.BookId, ct), student);
            tx.Complete();
            return new ApiResponse<LibraryIssueDto> { Success = true, StatusCode = 201,
                Message = "Book issued.", Data = response };
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error("Book issue changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Library book issue conflict in tenant {TenantId}", tenant);
            return Error("Book issue conflicts with another transaction.", 409);
        }
        catch (TransactionAbortedException)
        {
            return Error("Concurrent issue of this copy was rejected.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Book issue failed in tenant {TenantId}", tenant);
            return Error("Book could not be issued.", 500);
        }
    }

    public async Task<ApiResponse<LibraryIssueDto>> CloseAsync(Guid reference, ReturnBookDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<LibraryIssueDto>();
        if (reference == Guid.Empty || request == null) return Error("Return request is invalid.");
        if (!string.IsNullOrWhiteSpace(request.ConditionNote))
            return Error("Condition notes are not persisted by this version of the library model.");
        var tenant = _user.TenantId;
        try
        {
            using var tx = new TransactionScope(TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.Serializable },
                TransactionScopeAsyncFlowOption.Enabled);
            var issue = await _issues.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.ClientRequestId == reference, ct);
            if (issue == null) return Error("Issue not found.", 404);
            if (issue.State != BookIssueState.Issued)
            {
                var closed = await MapIssueAsync(issue, ct);
                tx.Complete();
                return ApiResponse<LibraryIssueDto>.SuccessResponse(closed, "Issue already closed.");
            }
            if (!MatchesVersion(issue.RowVersion, request.RowVersion))
                return Error("Issue changed. Reload and retry.", 409);
            var date = request.ReturnDate == default ? DateOnly.FromDateTime(_clock.GetLocalNow().DateTime) : request.ReturnDate;
            if (date < issue.IssueDate) return Error("Return date cannot precede issue date.");
            var copy = await _copies.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == issue.BookCopyId, ct);
            if (copy == null || copy.State != BookCopyState.Issued)
                return Error("Copy state is inconsistent with the outstanding issue.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            issue.State = BookIssueState.Returned; issue.ReturnDate = date;
            issue.ReturnedByUserId = _user.UserId; issue.UpdatedAt = now; issue.UpdatedBy = _user.UserId;
            copy.State = BookCopyState.Available; copy.UpdatedAt = now; copy.UpdatedBy = _user.UserId;
            await _uow.SaveChangesAsync(ct);
            var book = await _books.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == copy.BookId, ct);
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == issue.StudentId, ct);
            var response = MapIssue(issue, copy, book, student);
            tx.Complete();
            return ApiResponse<LibraryIssueDto>.SuccessResponse(response, "Book returned.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error("Book issue changed. Reload and retry.", 409);
        }
        catch (TransactionAbortedException)
        {
            return Error("Concurrent return transaction was rejected.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Book return failed for tenant {TenantId}", tenant);
            return Error("Book could not be returned.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<LibraryIssueDto>>> GetMyIssuesAsync(CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<LibraryIssueDto>>();
        var tenant = _user.TenantId;
        var ids = await _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && x.UserId == _user.UserId)
            .Select(x => x.Id).ToArrayAsync(ct);
        var issues = await _issues.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            ids.Contains(x.StudentId)).OrderByDescending(x => x.IssueDate).ThenByDescending(x => x.Id).Take(200).ToListAsync(ct);
        var copyIds = issues.Select(x => x.BookCopyId).Distinct().ToArray();
        var copies = await _copies.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            copyIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var bookIds = copies.Values.Select(x => x.BookId).Distinct().ToArray();
        var books = await _books.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            bookIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var studentIds = issues.Select(x => x.StudentId).Distinct().ToArray();
        var students = await _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            studentIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        IReadOnlyList<LibraryIssueDto> result = issues.Select(x =>
        {
            copies.TryGetValue(x.BookCopyId, out var copy);
            var book = copy == null ? null : books.GetValueOrDefault(copy.BookId);
            return MapIssue(x, copy, book, students.GetValueOrDefault(x.StudentId));
        }).ToList();
        return ApiResponse<IReadOnlyList<LibraryIssueDto>>.SuccessResponse(result);
    }

    private async Task<LibraryIssueDto> MapIssueAsync(BookIssue issue, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var copy = await _copies.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == issue.BookCopyId, ct);
        var book = copy == null ? null : await _books.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
            x.Id == copy.BookId, ct);
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
            x.Id == issue.StudentId, ct);
        return MapIssue(issue, copy, book, student);
    }

    private static LibraryIssueDto MapIssue(BookIssue issue, BookCopy? copy, Book? book, Student? student) => new()
    {
        Id = issue.Id, Reference = issue.ClientRequestId, BookCopyId = issue.BookCopyId,
        AccessionNumber = copy?.AccessionNumber ?? string.Empty, BookTitle = book?.Title ?? string.Empty,
        StudentReference = student?.PublicId ?? Guid.Empty, StudentName = student?.FullName ?? string.Empty,
        IssueDate = issue.IssueDate, DueDate = issue.DueDate, ReturnDate = issue.ReturnDate,
        State = issue.State, FineAmount = issue.FineAmount, RowVersion = Convert.ToBase64String(issue.RowVersion)
    };
    private static LibraryBookDto MapBook(Book book, string? categoryName, int total, int available) => new()
    {
        Id = book.Id, Reference = book.PublicId, BookCategoryId = book.BookCategoryId,
        BookCategoryName = categoryName, Title = book.Title, Author = book.Author,
        Publisher = book.Publisher, ISBN = book.ISBN, Edition = book.Edition,
        ReplacementPrice = book.ReplacementPrice, CoverImageUrl = book.CoverImageUrl,
        IsActive = book.IsActive, TotalCopies = total, AvailableCopies = available,
        RowVersion = Convert.ToBase64String(book.RowVersion)
    };
    private static bool MatchesVersion(byte[] current, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return false;
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(encoded)); }
        catch (FormatException) { return false; }
    }
    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0;
    private bool CanManage() => CanRead() && (_user.IsTenantAdmin ||
        _user.IsInRole("Principal") || _user.IsInRole("Librarian"));
    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Library permission required.", 403);
    private static ApiResponse<LibraryIssueDto> Error(string message, int status = 400) =>
        ApiResponse<LibraryIssueDto>.ErrorResponse(message, status);
}
