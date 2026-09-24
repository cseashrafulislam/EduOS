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
    private readonly IGenericRepository<Book> _books; private readonly IGenericRepository<BookIssue> _issues;
    private readonly IGenericRepository<Student> _students; private readonly IGenericRepository<Employee> _employees;
    private readonly IUnitOfWork _unitOfWork; private readonly ICurrentUserService _currentUser; private readonly TimeProvider _clock; private readonly ILogger<LibraryService> _logger;
    public LibraryService(IGenericRepository<Book> books, IGenericRepository<BookIssue> issues, IGenericRepository<Student> students, IGenericRepository<Employee> employees, IUnitOfWork unitOfWork, ICurrentUserService currentUser, TimeProvider clock, ILogger<LibraryService> logger)
    { _books=books; _issues=issues; _students=students; _employees=employees; _unitOfWork=unitOfWork; _currentUser=currentUser; _clock=clock; _logger=logger; }

    public async Task<ApiResponse<IReadOnlyList<LibraryBookDto>>> GetCatalogAsync(string? search,CancellationToken ct=default)
    {
        if(!CanRead()) return Denied<IReadOnlyList<LibraryBookDto>>(); var t=_currentUser.TenantId;
        var q=_books.GetQueryable().AsNoTracking().Where(x=>x.TenantId==t&&x.IsActive);
        if(!string.IsNullOrWhiteSpace(search)){var s=search.Trim();q=q.Where(x=>x.Title.Contains(s)||(x.Author!=null&&x.Author.Contains(s))||(x.ISBN!=null&&x.ISBN.Contains(s)));}
        var rows=await q.OrderBy(x=>x.Title).Take(200).Select(x=>new LibraryBookDto{Reference=x.PublicId,Title=x.Title,Author=x.Author,Publisher=x.Publisher,ISBN=x.ISBN,Category=x.Category,Edition=x.Edition,ShelfNo=x.ShelfNo,TotalCopies=x.TotalCopies,AvailableCopies=x.AvailableCopies,Price=x.Price,CoverImageUrl=x.CoverImageUrl,RowVersion=Convert.ToBase64String(x.RowVersion)}).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<LibraryBookDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<LibraryBookDto>> SaveBookAsync(SaveLibraryBookDto r,CancellationToken ct=default)
    {
        if(!CanManage()) return Denied<LibraryBookDto>(); if(r==null||string.IsNullOrWhiteSpace(r.Title)||r.TotalCopies<0)return ApiResponse<LibraryBookDto>.ErrorResponse("Book data is invalid.");
        var t=_currentUser.TenantId; var now=_clock.GetUtcNow().UtcDateTime; Book? b=null;
        if(r.Reference.HasValue){b=await _books.GetQueryable().FirstOrDefaultAsync(x=>x.TenantId==t&&x.PublicId==r.Reference.Value&&x.IsActive,ct);if(b==null)return ApiResponse<LibraryBookDto>.ErrorResponse("Book not found.",404);if(!TryDecodeRowVersion(r.RowVersion,out var rv)||!b.RowVersion.AsSpan().SequenceEqual(rv))return ApiResponse<LibraryBookDto>.ErrorResponse("The book changed by another user. Reload and try again.",409);var issued=b.TotalCopies-b.AvailableCopies;if(r.TotalCopies<issued)return ApiResponse<LibraryBookDto>.ErrorResponse("Total copies cannot be less than copies currently issued.",409);b.AvailableCopies=r.TotalCopies-issued;b.UpdatedAt=now;b.UpdatedBy=_currentUser.UserId;}
        else{b=new Book{TenantId=t,PublicId=Guid.NewGuid(),AvailableCopies=r.TotalCopies,IsActive=true,CreatedAt=now,CreatedBy=_currentUser.UserId};await _books.AddAsync(b);}
        b.Title=r.Title.Trim();b.Author=Trim(r.Author);b.Publisher=Trim(r.Publisher);b.ISBN=Trim(r.ISBN);b.Category=Trim(r.Category);b.Edition=Trim(r.Edition);b.ShelfNo=Trim(r.ShelfNo);b.TotalCopies=r.TotalCopies;b.Price=r.Price;b.CoverImageUrl=Trim(r.CoverImageUrl);
        try{await _unitOfWork.SaveChangesAsync(ct);return ApiResponse<LibraryBookDto>.SuccessResponse(MapBook(b),r.Reference.HasValue?"Book updated.":"Book created.");}catch(DbUpdateConcurrencyException ex){_logger.LogWarning(ex,"Concurrent library book update for tenant {TenantId}",t);return ApiResponse<LibraryBookDto>.ErrorResponse("The book changed by another user. Reload and try again.",409);}
    }

    public async Task<ApiResponse<bool>> ArchiveBookAsync(Guid reference,string rowVersion,CancellationToken ct=default)
    {
        if(!CanManage())return Denied<bool>();var t=_currentUser.TenantId;var b=await _books.GetQueryable().FirstOrDefaultAsync(x=>x.TenantId==t&&x.PublicId==reference&&x.IsActive,ct);if(b==null)return ApiResponse<bool>.ErrorResponse("Book not found.",404);
        if(!TryDecodeRowVersion(rowVersion,out var rv)||!b.RowVersion.AsSpan().SequenceEqual(rv))return ApiResponse<bool>.ErrorResponse("The book changed by another user. Reload and try again.",409);
        if(await _issues.AnyAsync(x=>x.TenantId==t&&x.BookId==b.Id&&x.Status=="Issued"))return ApiResponse<bool>.ErrorResponse("A book with active issues cannot be archived.",409);
        b.IsActive=false;b.UpdatedAt=_clock.GetUtcNow().UtcDateTime;b.UpdatedBy=_currentUser.UserId;try{await _unitOfWork.SaveChangesAsync(ct);return ApiResponse<bool>.SuccessResponse(true,"Book archived.");}catch(DbUpdateConcurrencyException){return ApiResponse<bool>.ErrorResponse("The book changed by another user. Reload and try again.",409);}
    }

    public async Task<ApiResponse<LibraryIssueDto>> IssueAsync(IssueBookDto r,CancellationToken ct=default)
    {
        if(!CanManage())return Denied<LibraryIssueDto>();if(r==null||r.ClientRequestId==Guid.Empty||r.BookReference==Guid.Empty||(r.StudentReference.HasValue==r.EmployeeReference.HasValue))return Error("Exactly one borrower and a valid request reference are required.");var today=_clock.GetLocalNow().Date;if(r.DueDate.Date<today)return Error("Due date cannot be before today.");var t=_currentUser.TenantId;
        try{var existing=await _issues.GetQueryable().AsNoTracking().Include(x=>x.Book).Include(x=>x.Student).Include(x=>x.Employee).FirstOrDefaultAsync(x=>x.TenantId==t&&x.ClientRequestId==r.ClientRequestId,ct);if(existing!=null)return ApiResponse<LibraryIssueDto>.SuccessResponse(Map(existing),"Book issue was already processed.");var book=await _books.GetQueryable().FirstOrDefaultAsync(x=>x.TenantId==t&&x.PublicId==r.BookReference&&x.IsActive,ct);if(book==null)return Error("Book not found.",404);if(book.AvailableCopies<=0)return Error("No copy is currently available.",409);long? sid=null,eid=null;if(r.StudentReference.HasValue){var s=await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x=>x.TenantId==t&&x.PublicId==r.StudentReference.Value&&x.IsActive,ct);if(s==null)return Error("Student borrower not found.",404);sid=s.Id;}else{var e=await _employees.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x=>x.TenantId==t&&x.PublicId==r.EmployeeReference!.Value&&x.IsActive,ct);if(e==null)return Error("Employee borrower not found.",404);eid=e.Id;}var now=_clock.GetUtcNow().UtcDateTime;var issue=new BookIssue{TenantId=t,PublicId=Guid.NewGuid(),ClientRequestId=r.ClientRequestId,BookId=book.Id,StudentId=sid,EmployeeId=eid,IssueDate=today,ReturnDate=r.DueDate.Date,Status="Issued",IssuedByUserId=_currentUser.UserId,CreatedAt=now,CreatedBy=_currentUser.UserId};book.AvailableCopies--;await _issues.AddAsync(issue);await _unitOfWork.SaveChangesAsync(ct);issue.Book=book;if(sid.HasValue)issue.Student=await _students.GetQueryable().AsNoTracking().FirstAsync(x=>x.TenantId==t&&x.Id==sid.Value,ct);if(eid.HasValue)issue.Employee=await _employees.GetQueryable().AsNoTracking().FirstAsync(x=>x.TenantId==t&&x.Id==eid.Value,ct);return new ApiResponse<LibraryIssueDto>{Success=true,StatusCode=201,Message="Book issued.",Data=Map(issue)};}catch(DbUpdateConcurrencyException ex){_logger.LogWarning(ex,"Concurrent library issue for tenant {TenantId}",t);return Error("Book availability changed. Reload and try again.",409);}catch(DbUpdateException ex){_logger.LogWarning(ex,"Conflicting library issue for tenant {TenantId}",t);return Error("The issue request conflicts with an existing transaction.",409);}
    }

    public async Task<ApiResponse<LibraryIssueDto>> CloseAsync(Guid reference,ReturnBookDto r,CancellationToken ct=default)
    {
        if(!CanManage())return Denied<LibraryIssueDto>();if(reference==Guid.Empty||r==null||r.FineAmount<0)return Error("Return request is invalid.");var action=r.Action?.Trim();if(!string.Equals(action,"Returned",StringComparison.OrdinalIgnoreCase)&&!string.Equals(action,"Lost",StringComparison.OrdinalIgnoreCase))return Error("Action must be Returned or Lost.");var t=_currentUser.TenantId;
        try{var issue=await _issues.GetQueryable().Include(x=>x.Book).Include(x=>x.Student).Include(x=>x.Employee).FirstOrDefaultAsync(x=>x.TenantId==t&&x.PublicId==reference,ct);if(issue==null)return Error("Book issue not found.",404);if(!string.Equals(issue.Status,"Issued",StringComparison.OrdinalIgnoreCase))return ApiResponse<LibraryIssueDto>.SuccessResponse(Map(issue),"Book issue is already closed.");if(!TryDecodeRowVersion(r.RowVersion,out var rv)||!issue.RowVersion.AsSpan().SequenceEqual(rv))return Error("The issue changed by another user. Reload and try again.",409);var now=_clock.GetUtcNow().UtcDateTime;issue.Status=string.Equals(action,"Lost",StringComparison.OrdinalIgnoreCase)?"Lost":"Returned";issue.ActualReturnDate=_clock.GetLocalNow().Date;issue.FineAmount=r.FineAmount;issue.ReturnedByUserId=_currentUser.UserId;issue.UpdatedAt=now;issue.UpdatedBy=_currentUser.UserId;if(issue.Status=="Returned"&&issue.Book!=null){if(issue.Book.AvailableCopies>=issue.Book.TotalCopies)return Error("Book stock is inconsistent. Correct stock before return.",409);issue.Book.AvailableCopies++;}await _unitOfWork.SaveChangesAsync(ct);return ApiResponse<LibraryIssueDto>.SuccessResponse(Map(issue),issue.Status=="Returned"?"Book returned.":"Book marked as lost.");}catch(DbUpdateConcurrencyException ex){_logger.LogWarning(ex,"Concurrent library return {Reference} for tenant {TenantId}",reference,t);return Error("The issue changed by another user. Reload and try again.",409);}
    }

    public async Task<ApiResponse<IReadOnlyList<LibraryIssueDto>>> GetMyIssuesAsync(CancellationToken ct=default)
    {
        if(!CanRead())return Denied<IReadOnlyList<LibraryIssueDto>>();var t=_currentUser.TenantId;var uid=_currentUser.UserId;var sids=await _students.GetQueryable().AsNoTracking().Where(x=>x.TenantId==t&&x.UserId==uid).Select(x=>x.Id).ToListAsync(ct);var eids=await _employees.GetQueryable().AsNoTracking().Where(x=>x.TenantId==t&&x.UserId==uid).Select(x=>x.Id).ToListAsync(ct);var rows=await _issues.GetQueryable().AsNoTracking().Include(x=>x.Book).Include(x=>x.Student).Include(x=>x.Employee).Where(x=>x.TenantId==t&&((x.StudentId.HasValue&&sids.Contains(x.StudentId.Value))||(x.EmployeeId.HasValue&&eids.Contains(x.EmployeeId.Value)))).OrderByDescending(x=>x.IssueDate).Select(x=>new LibraryIssueDto{Reference=x.PublicId,BookReference=x.Book!=null?x.Book.PublicId:Guid.Empty,BookTitle=x.Book!=null?x.Book.Title:string.Empty,BorrowerType=x.StudentId.HasValue?"Student":"Employee",BorrowerName=x.Student!=null?x.Student.FullName:x.Employee!=null?x.Employee.FullName:string.Empty,IssueDate=x.IssueDate,DueDate=x.ReturnDate,ActualReturnDate=x.ActualReturnDate,FineAmount=x.FineAmount,Status=x.Status,RowVersion=Convert.ToBase64String(x.RowVersion)}).ToListAsync(ct);return ApiResponse<IReadOnlyList<LibraryIssueDto>>.SuccessResponse(rows);
    }

    private bool CanRead()=>_currentUser.IsAuthenticated&&_currentUser.TenantId>0;private bool CanManage()=>CanRead()&&(_currentUser.IsTenantAdmin||_currentUser.IsInRole("Principal")||_currentUser.IsInRole("Librarian"));
    private static string? Trim(string? x)=>string.IsNullOrWhiteSpace(x)?null:x.Trim();
    private static bool TryDecodeRowVersion(string? s,out byte[] value){value=Array.Empty<byte>();if(string.IsNullOrWhiteSpace(s))return false;try{value=Convert.FromBase64String(s);return value.Length>0;}catch(FormatException){return false;}}
    private static LibraryBookDto MapBook(Book x)=>new(){Reference=x.PublicId,Title=x.Title,Author=x.Author,Publisher=x.Publisher,ISBN=x.ISBN,Category=x.Category,Edition=x.Edition,ShelfNo=x.ShelfNo,TotalCopies=x.TotalCopies,AvailableCopies=x.AvailableCopies,Price=x.Price,CoverImageUrl=x.CoverImageUrl,RowVersion=Convert.ToBase64String(x.RowVersion)};
    private static LibraryIssueDto Map(Book x)=>throw new NotSupportedException();
    private static LibraryIssueDto Map(BookIssue x)=>new(){Reference=x.PublicId,BookReference=x.Book?.PublicId??Guid.Empty,BookTitle=x.Book?.Title??string.Empty,BorrowerType=x.StudentId.HasValue?"Student":"Employee",BorrowerName=x.Student?.FullName??x.Employee?.FullName??string.Empty,IssueDate=x.IssueDate,DueDate=x.ReturnDate,ActualReturnDate=x.ActualReturnDate,FineAmount=x.FineAmount,Status=x.Status,RowVersion=Convert.ToBase64String(x.RowVersion)};
    private static ApiResponse<T> Denied<T>()=>ApiResponse<T>.ErrorResponse("Library access is required.",403);private static ApiResponse<LibraryIssueDto> Error(string message,int status=400)=>ApiResponse<LibraryIssueDto>.ErrorResponse(message,status);
}