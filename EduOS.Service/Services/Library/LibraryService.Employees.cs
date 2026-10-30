using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Library;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Library;

public sealed partial class LibraryService
{
    private IQueryable<Employee>? EligibleEmployees(long tenant, DateOnly today)
    {
        if (_staffEmployees == null || _staffCampusAssignments == null || _branches == null)
            return null;
        var activeCampuses = _staffCampusAssignments.GetQueryable().AsNoTracking().Where(a =>
            a.TenantId == tenant && !a.IsDeleted && a.IsCurrent &&
            a.EffectiveFrom <= today && (!a.EffectiveTo.HasValue || a.EffectiveTo.Value >= today));
        if (IsCampusScopedStaff)
        {
            var allowed = AllowedStaffCampusIds(tenant, today);
            if (allowed == null) return null;
            activeCampuses = activeCampuses.Where(a => allowed.Contains(a.CampusId));
        }
        var staffIds = activeCampuses.Select(a => a.EmployeeId);
        return _staffEmployees.GetQueryable().AsNoTracking().Where(e => e.TenantId == tenant &&
            !e.IsDeleted && e.State == EmployeeState.Active && staffIds.Contains(e.Id));
    }

    public async Task<ApiResponse<PagedResult<LibraryEmployeeOptionDto>>> FindEligibleEmployeesAsync(
        string search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<LibraryEmployeeOptionDto>>();
        if (string.IsNullOrWhiteSpace(search) || search.Trim().Length < 2 || search.Length > 100 ||
            page < 1 || pageSize is < 1 or > 50 || ((long)page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<LibraryEmployeeOptionDto>>.ErrorResponse("Valid employee search and page are required.");
        var tenant = _user.TenantId;
        var query = EligibleEmployees(tenant, DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime));
        if (query == null)
            return ApiResponse<PagedResult<LibraryEmployeeOptionDto>>.ErrorResponse("Employee-campus assignments are unavailable.", 503);
        var term = search.Trim();
        query = query.Where(e => e.FullName.Contains(term) || e.EmployeeCode.Contains(term));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(e => e.EmployeeCode).ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new LibraryEmployeeOptionDto
            { EmployeeReference = e.PublicId, EmployeeCode = e.EmployeeCode, EmployeeName = e.FullName })
            .ToListAsync(ct);
        return ApiResponse<PagedResult<LibraryEmployeeOptionDto>>.SuccessResponse(
            new PagedResult<LibraryEmployeeOptionDto>
            { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<PagedResult<BookCopyDto>>> GetAvailableCopiesForEmployeeAsync(
        Guid bookReference, Guid employeeReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<BookCopyDto>>();
        if (bookReference == Guid.Empty || employeeReference == Guid.Empty || page < 1 ||
            pageSize is < 1 or > 100 || ((long)page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<BookCopyDto>>.ErrorResponse("Invalid employee, book or page.");
        var tenant = _user.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var eligible = EligibleEmployees(tenant, today);
        if (eligible == null || _branches == null || _staffCampusAssignments == null)
            return ApiResponse<PagedResult<BookCopyDto>>.ErrorResponse("Employee-campus assignments are unavailable.", 503);
        var employeeId = await eligible.Where(e => e.PublicId == employeeReference)
            .Select(e => e.Id).FirstOrDefaultAsync(ct);
        if (employeeId == 0)
            return ApiResponse<PagedResult<BookCopyDto>>.ErrorResponse("Eligible employee not found.", 404);
        var campusIds = _staffCampusAssignments.GetQueryable().AsNoTracking().Where(a =>
            a.TenantId == tenant && a.EmployeeId == employeeId && a.IsCurrent && !a.IsDeleted &&
            a.EffectiveFrom <= today && (!a.EffectiveTo.HasValue || a.EffectiveTo.Value >= today))
            .Select(a => a.CampusId);
        var book = await _books.GetQueryable().AsNoTracking().FirstOrDefaultAsync(b =>
            b.TenantId == tenant && b.PublicId == bookReference && b.IsActive && !b.IsDeleted, ct);
        if (book == null)
            return ApiResponse<PagedResult<BookCopyDto>>.ErrorResponse("Active book not found.", 404);
        var branches = _branches.GetQueryable().AsNoTracking().Where(b => b.TenantId == tenant &&
            !b.IsDeleted && b.IsActive && campusIds.Contains(b.CampusId)).Select(b => b.Id);
        var query = _copies.GetQueryable().AsNoTracking().Where(c => c.TenantId == tenant &&
            c.BookId == book.Id && !c.IsDeleted && c.State == BookCopyState.Available &&
            ((c.LibraryBranchId.HasValue && branches.Contains(c.LibraryBranchId.Value)) ||
                (_user.IsTenantAdmin && !c.LibraryBranchId.HasValue)) &&
            !_issues.GetQueryable().Any(i => i.TenantId == tenant && i.BookCopyId == c.Id &&
                !i.IsDeleted && i.State == BookIssueState.Issued));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(c => c.AccessionNumber).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<BookCopyDto>>.SuccessResponse(new PagedResult<BookCopyDto>
        { Items = rows.Select(c => MapCopy(c, book)).ToList(), TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<BookIssueDto>> IssueEmployeeAsync(IssueBookRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<BookIssueDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.BookCopyId <= 0 ||
            !request.EmployeeReference.HasValue || request.EmployeeReference.Value == Guid.Empty ||
            request.StudentReference != Guid.Empty)
            return Error("A single employee, copy and unique request ID are required.");
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var issueDate = request.IssueDate == default ? today : request.IssueDate;
        if (issueDate > today || issueDate > request.DueDate)
            return Error("Issue date cannot be in the future or later than the due date.", 409);
        var tenant = _user.TenantId;
        if (_staffEmployees == null || _staffCampusAssignments == null || _reservations == null || _branches == null)
            return Error("Employee borrowing setup is unavailable.", 503);
        try
        {
            using var tx = new TransactionScope(TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.Serializable },
                TransactionScopeAsyncFlowOption.Enabled);
            var employee = await _staffEmployees.GetQueryable().AsNoTracking().FirstOrDefaultAsync(e =>
                e.TenantId == tenant && e.PublicId == request.EmployeeReference.Value && !e.IsDeleted, ct);
            if (employee == null) return Error("Employee not found.", 404);
            var replay = await _issues.GetQueryable().AsNoTracking().FirstOrDefaultAsync(i =>
                i.TenantId == tenant && i.ClientRequestId == request.ClientRequestId, ct);
            if (replay != null)
            {
                if (replay.EmployeeId != employee.Id || replay.StudentId.HasValue ||
                    replay.BookCopyId != request.BookCopyId || replay.DueDate != request.DueDate ||
                    (request.IssueDate != default && replay.IssueDate != request.IssueDate))
                    return Error("Request ID was used for different loan terms.", 409);
                var branchId = await _copies.GetQueryable().AsNoTracking().Where(c =>
                    c.TenantId == tenant && c.Id == replay.BookCopyId && !c.IsDeleted)
                    .Select(c => c.LibraryBranchId).FirstOrDefaultAsync(ct);
                if (!await StaffCanManageBranchAsync(branchId, ct)) return Error("Loan campus is restricted.", 403);
                var response = await MapIssueAsync(replay, ct);
                tx.Complete();
                return ApiResponse<BookIssueDto>.SuccessResponse(response, "Employee loan already recorded.");
            }
            if (employee.State != EmployeeState.Active)
                return Error("Only active employees may borrow.", 409);
            var copy = await _copies.GetQueryable().FirstOrDefaultAsync(c =>
                c.TenantId == tenant && c.Id == request.BookCopyId && !c.IsDeleted, ct);
            if (copy == null || copy.State != BookCopyState.Available)
                return Error("Copy is unavailable.", 409);
            if (!await StaffCanManageBranchAsync(copy.LibraryBranchId, ct))
                return Error("Copy is outside your campus.", 403);
            var book = await _books.GetQueryable().AsNoTracking().FirstOrDefaultAsync(b =>
                b.TenantId == tenant && b.Id == copy.BookId && b.IsActive && !b.IsDeleted, ct);
            if (book == null) return Error("Book unavailable.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            long? campusId = null;
            if (copy.LibraryBranchId.HasValue)
            {
                campusId = await _branches.GetQueryable().AsNoTracking().Where(b => b.TenantId == tenant &&
                    b.Id == copy.LibraryBranchId.Value && b.IsActive && !b.IsDeleted)
                    .Select(b => (long?)b.CampusId).FirstOrDefaultAsync(ct);
                if (!campusId.HasValue) return Error("Active copy branch required.", 409);
            }
            var activeAssignments = _staffCampusAssignments.GetQueryable().AsNoTracking().Where(a =>
                a.TenantId == tenant && a.EmployeeId == employee.Id && !a.IsDeleted && a.IsCurrent &&
                a.EffectiveFrom <= today && (!a.EffectiveTo.HasValue || a.EffectiveTo.Value >= today));
            if (!await activeAssignments.AnyAsync(a => !campusId.HasValue || a.CampusId == campusId.Value, ct))
                return Error("Employee is not assigned to this campus.", 409);
            if (await _issues.GetQueryable().AsNoTracking().AnyAsync(i =>
                i.TenantId == tenant && !i.IsDeleted && i.BookCopyId == copy.Id &&
                i.State == BookIssueState.Issued, ct))
                return Error("Copy is already issued.", 409);
            // Student FIFO reservation still takes precedence over direct employee issuance.
            if (await _reservations.GetQueryable().AsNoTracking().AnyAsync(r =>
                r.TenantId == tenant && r.BookId == copy.BookId && !r.IsDeleted &&
                r.State == BookReservationState.Pending && r.ExpiresAt > now &&
                _students.GetQueryable().Any(s => s.TenantId == tenant && s.Id == r.StudentId &&
                    s.StatusCode == "Active" && !s.IsDeleted) &&
                _enrollments.GetQueryable().Any(e => e.TenantId == tenant && e.StudentId == r.StudentId &&
                    e.IsCurrent && !e.IsDeleted && e.State == EnrollmentState.Active &&
                    (!campusId.HasValue || e.CampusId == campusId.Value)), ct))
                return Error("An active student reservation has priority.", 409);
            var loan = new BookIssue
            {
                TenantId = tenant, ClientRequestId = request.ClientRequestId, BookCopyId = copy.Id,
                StudentId = null, EmployeeId = employee.Id, IssueDate = issueDate,
                DueDate = request.DueDate, ReplacementPriceSnapshot = book.ReplacementPrice,
                IssuedByUserId = _user.UserId, State = BookIssueState.Issued,
                CreatedAt = now, CreatedBy = _user.UserId
            };
            copy.State = BookCopyState.Issued; copy.UpdatedBy = _user.UserId; copy.UpdatedAt = now;
            await _issues.AddAsync(loan);
            await _uow.SaveChangesAsync(ct);
            var dto = MapIssue(loan, copy, book, null, employee);
            tx.Complete();
            return new ApiResponse<BookIssueDto> { Success = true, StatusCode = 201,
                Message = "Employee loan issued.", Data = dto };
        }
        catch (DbUpdateConcurrencyException) { return Error("Employee loan changed concurrently.", 409); }
        catch (DbUpdateException) { return Error("Copy or request already claimed.", 409); }
        catch (TransactionAbortedException) { return Error("Concurrent circulation was rejected.", 409); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Employee circulation failed for tenant {TenantId}", tenant);
            return Error("Employee loan could not be recorded.", 500);
        }
    }

    public async Task<ApiResponse<PagedResult<BookIssueDto>>> GetMyEmployeeIssuesAsync(
        int page, int pageSize, string? status, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<PagedResult<BookIssueDto>>();
        if (_staffEmployees == null)
            return ApiResponse<PagedResult<BookIssueDto>>.ErrorResponse("Employee borrowing unavailable.", 503);
        status = (status ?? "all").Trim().ToLowerInvariant();
        if (page < 1 || pageSize is < 1 or > 100 || ((long)page - 1) * pageSize > int.MaxValue ||
            status is not ("all" or "issued" or "overdue" or "returned"))
            return ApiResponse<PagedResult<BookIssueDto>>.ErrorResponse("Invalid loan statement filters.");
        var tenant = _user.TenantId;
        var ownEmployeeIds = _staffEmployees.GetQueryable().AsNoTracking().Where(e =>
            e.TenantId == tenant && e.UserId == _user.UserId && !e.IsDeleted).Select(e => e.Id);
        var query = _issues.GetQueryable().AsNoTracking().Where(i => i.TenantId == tenant &&
            i.EmployeeId.HasValue && ownEmployeeIds.Contains(i.EmployeeId.Value) && !i.IsDeleted);
        if (status == "issued") query = query.Where(i => i.State == BookIssueState.Issued);
        if (status == "returned") query = query.Where(i => i.State == BookIssueState.Returned);
        if (status == "overdue")
        {
            var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
            query = query.Where(i => i.State == BookIssueState.Issued && i.DueDate < today);
        }
        var count = await query.CountAsync(ct);
        var issues = await query.OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var copyIds = issues.Select(i => i.BookCopyId).Distinct().ToArray();
        var copies = await _copies.GetQueryable().AsNoTracking().Where(c => c.TenantId == tenant &&
            copyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var bookIds = copies.Values.Select(c => c.BookId).Distinct().ToArray();
        var books = await _books.GetQueryable().AsNoTracking().Where(b => b.TenantId == tenant &&
            bookIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var employeeIds = issues.Select(i => i.EmployeeId!.Value).Distinct().ToArray();
        var employees = await _staffEmployees.GetQueryable().AsNoTracking().Where(e =>
            e.TenantId == tenant && employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var rows = issues.Select(i =>
        {
            copies.TryGetValue(i.BookCopyId, out var copy);
            return MapIssue(i, copy, copy == null ? null : books.GetValueOrDefault(copy.BookId),
                null, employees.GetValueOrDefault(i.EmployeeId!.Value));
        }).ToArray();
        return ApiResponse<PagedResult<BookIssueDto>>.SuccessResponse(new PagedResult<BookIssueDto>
        { Items = rows.ToList(), TotalCount = count, Page = page, PageSize = pageSize });
    }
}
