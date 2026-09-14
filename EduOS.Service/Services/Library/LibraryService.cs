using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Employees;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Library;

public sealed class LibraryService : ILibraryService
{
    private readonly IGenericRepository<Book> _books;
    private readonly IGenericRepository<BookIssue> _issues;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Employee> _employees;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<LibraryService> _logger;

    public LibraryService(IGenericRepository<Book> books, IGenericRepository<BookIssue> issues, IGenericRepository<Student> students, IGenericRepository<Employee> employees, IUnitOfWork unitOfWork, ICurrentUserService currentUser, TimeProvider clock, ILogger<LibraryService> logger)
    {
        _books = books; _issues = issues; _students = students; _employees = employees; _unitOfWork = unitOfWork; _currentUser = currentUser; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<LibraryBookDto>>> GetCatalogAsync(string? search, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<LibraryBookDto>>();
        var tenantId = _currentUser.TenantId;
        var q = _books.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.Title.Contains(s) || (x.Author != null && x.Author.Contains(s)) || (x.ISBN != null && x.ISBN.Contains(s)));
        }
        IReadOnlyList<LibraryBookDto> rows = await q.OrderBy(x => x.Title).Take(200).Select(x => new LibraryBookDto { Reference = x.PublicId, Title = x.Title, Author = x.Author, ISBN = x.ISBN, Category = x.Category, ShelfNo = x.ShelfNo, TotalCopies = x.TotalCopies, AvailableCopies = x.AvailableCopies }).ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<LibraryBookDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<LibraryIssueDto>> IssueAsync(IssueBookDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<LibraryIssueDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.BookReference == Guid.Empty || (request.StudentReference.HasValue == request.EmployeeId.HasValue)) return Error("Exactly one borrower and a valid request reference are required.");
        var today = _clock.GetLocalNow().Date;
        if (request.DueDate.Date < today) return Error("Due date cannot be before today.");
        var tenantId = _currentUser.TenantId;
        try
        {
            var existing = await _issues.GetQueryable().AsNoTracking().Include(x => x.Book).Include(x => x.Student).Include(x => x.Employee).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (existing != null) return ApiResponse<LibraryIssueDto>.SuccessResponse(Map(existing), "Book issue was already processed.");
            var book = await _books.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == request.BookReference && x.IsActive, cancellationToken);
            if (book == null) return Error("Book not found.", 404);
            if (book.AvailableCopies <= 0) return Error("No copy is currently available.", 409);
            long? studentId = null; long? employeeId = null;
            if (request.StudentReference.HasValue)
            {
                var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == request.StudentReference.Value && x.IsActive, cancellationToken);
                if (student == null) return Error("Student borrower not found.", 404);
                studentId = student.Id;
            }
            else
            {
                var employee = await _employees.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.EmployeeId && x.IsActive, cancellationToken);
                if (employee == null) return Error("Employee borrower not found.", 404);
                employeeId = employee.Id;
            }
            var now = _clock.GetUtcNow().UtcDateTime;
            var issue = new BookIssue { TenantId = tenantId, PublicId = Guid.NewGuid(), ClientRequestId = request.ClientRequestId, BookId = book.Id, StudentId = studentId, EmployeeId = employeeId, IssueDate = today, ReturnDate = request.DueDate.Date, Status = "Issued", IssuedByUserId = _currentUser.UserId, CreatedAt = now, CreatedBy = _currentUser.UserId };
            book.AvailableCopies--;
            await _issues.AddAsync(issue);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            issue.Book = book;
            if (studentId.HasValue) issue.Student = await _students.GetQueryable().AsNoTracking().FirstAsync(x => x.Id == studentId.Value, cancellationToken);
            if (employeeId.HasValue) issue.Employee = await _employees.GetQueryable().AsNoTracking().FirstAsync(x => x.Id == employeeId.Value, cancellationToken);
            return new ApiResponse<LibraryIssueDto> { Success = true, StatusCode = 201, Message = "Book issued.", Data = Map(issue) };
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent library issue for tenant {TenantId}", tenantId);
            return Error("Book availability changed. Reload and try again.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting library issue for tenant {TenantId}", tenantId);
            return Error("The issue request conflicts with an existing transaction.", 409);
        }
    }

    public async Task<ApiResponse<LibraryIssueDto>> CloseAsync(Guid issueReference, ReturnBookDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<LibraryIssueDto>();
        if (issueReference == Guid.Empty || request == null || request.FineAmount < 0) return Error("Return request is invalid.");
        var action = request.Action?.Trim();
        if (!string.Equals(action, "Returned", StringComparison.OrdinalIgnoreCase) && !string.Equals(action, "Lost", StringComparison.OrdinalIgnoreCase)) return Error("Action must be Returned or Lost.");
        var tenantId = _currentUser.TenantId;
        try
        {
            var issue = await _issues.GetQueryable().Include(x => x.Book).Include(x => x.Student).Include(x => x.Employee).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == issueReference, cancellationToken);
            if (issue == null) return Error("Book issue not found.", 404);
            if (!string.Equals(issue.Status, "Issued", StringComparison.OrdinalIgnoreCase)) return ApiResponse<LibraryIssueDto>.SuccessResponse(Map(issue), "Book issue is already closed.");
            var now = _clock.GetUtcNow().UtcDateTime;
            issue.Status = string.Equals(action, "Lost", StringComparison.OrdinalIgnoreCase) ? "Lost" : "Returned";
            issue.ActualReturnDate = _clock.GetLocalNow().Date;
            issue.FineAmount = request.FineAmount;
            issue.ReturnedByUserId = _currentUser.UserId;
            issue.UpdatedAt = now; issue.UpdatedBy = _currentUser.UserId;
            if (issue.Status == "Returned" && issue.Book != null)
            {
                if (issue.Book.AvailableCopies >= issue.Book.TotalCopies) return Error("Book stock is inconsistent. Correct stock before return.", 409);
                issue.Book.AvailableCopies++;
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<LibraryIssueDto>.SuccessResponse(Map(issue), issue.Status == "Returned" ? "Book returned." : "Book marked as lost.");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent library return {Reference} for tenant {TenantId}", issueReference, tenantId);
            return Error("The issue changed by another user. Reload and try again.", 409);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<LibraryIssueDto>>> GetMyIssuesAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<LibraryIssueDto>>();
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var studentIds = await _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && x.UserId == userId).Select(x => x.Id).ToListAsync(cancellationToken);
        var employeeIds = await _employees.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && x.UserId == userId).Select(x => x.Id).ToListAsync(cancellationToken);
        IReadOnlyList<LibraryIssueDto> rows = await _issues.GetQueryable().AsNoTracking().Include(x => x.Book).Include(x => x.Student).Include(x => x.Employee).Where(x => x.TenantId == tenantId && ((x.StudentId.HasValue && studentIds.Contains(x.StudentId.Value)) || (x.EmployeeId.HasValue && employeeIds.Contains(x.EmployeeId.Value)))).OrderByDescending(x => x.IssueDate).Select(x => new LibraryIssueDto { Reference = x.PublicId, BookReference = x.Book != null ? x.Book.PublicId : Guid.Empty, BookTitle = x.Book != null ? x.Book.Title : string.Empty, BorrowerType = x.StudentId.HasValue ? "Student" : "Employee", BorrowerName = x.Student != null ? x.Student.FullName : x.Employee != null ? x.Employee.FullName : string.Empty, IssueDate = x.IssueDate, DueDate = x.ReturnDate, ActualReturnDate = x.ActualReturnDate, FineAmount = x.FineAmount, Status = x.Status, RowVersion = Convert.ToBase64String(x.RowVersion) }).ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<LibraryIssueDto>>.SuccessResponse(rows);
    }

    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0;
    private bool CanManage() => CanRead() && (_currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal") || _currentUser.IsInRole("Librarian"));
    private static LibraryIssueDto Map(BookIssue x) => new() { Reference = x.PublicId, BookReference = x.Book?.PublicId ?? Guid.Empty, BookTitle = x.Book?.Title ?? string.Empty, BorrowerType = x.StudentId.HasValue ? "Student" : "Employee", BorrowerName = x.Student?.FullName ?? x.Employee?.FullName ?? string.Empty, IssueDate = x.IssueDate, DueDate = x.ReturnDate, ActualReturnDate = x.ActualReturnDate, FineAmount = x.FineAmount, Status = x.Status, RowVersion = Convert.ToBase64String(x.RowVersion) };
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Library access is required.", 403);
    private static ApiResponse<LibraryIssueDto> Error(string message, int status = 400) => ApiResponse<LibraryIssueDto>.ErrorResponse(message, status);
}
