using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;
using System.Transactions;

namespace EduOS.Service.Services.Library;

public sealed partial class LibraryService
{
    // BookIssue due-date changes are already written to AuditLogs in the same SaveChanges
    // transaction. Reuse that history rather than introducing a competing editable ledger.
    private IQueryable<AuditLog> RenewalEvents(long tenant, long issueId) =>
        _renewalAudits!.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.EntityName == "BookIssue" && x.EntityId == issueId &&
            x.Action == "Update" && x.OldValue != null && x.NewValue != null &&
            x.OldValue.Contains("\"DueDate\"") && x.NewValue.Contains("\"DueDate\""));

    public async Task<ApiResponse<BookIssueDto>> RenewAsync(
        Guid reference, RenewBookRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<BookIssueDto>();
        if (reference == Guid.Empty || request == null || request.NewDueDate == default ||
            string.IsNullOrWhiteSpace(request.RowVersion))
            return Error("Issue, due date and rowversion are required.");
        if (_renewalAudits == null || _reservations == null)
            return Error("Renewal audit or reservation validation is unavailable.", 503);
        var tenant = _user.TenantId;
        try
        {
            using var tx = new TransactionScope(TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.Serializable },
                TransactionScopeAsyncFlowOption.Enabled);
            var issue = await _issues.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == reference && !x.IsDeleted, ct);
            if (issue == null) return Error("Issue not found.", 404);
            var copy = await _copies.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == issue.BookCopyId && !x.IsDeleted, ct);
            if (copy == null) return Error("Copy not found.", 409);
            if (!await StaffCanManageBranchAsync(copy.LibraryBranchId, ct))
                return Error("Copy is outside your assigned campus.", 403);
            if (issue.State != BookIssueState.Issued || copy.State != BookCopyState.Issued)
                return Error("Only outstanding loans may be renewed.", 409);
            var audit = RenewalEvents(tenant, issue.Id);
            // Same-date retry is harmless only when the immutable audit proves a renewal.
            if (request.NewDueDate == issue.DueDate &&
                await audit.AnyAsync(x => x.NewValue!.Contains(request.NewDueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ct))
            {
                var replay = await MapIssueAsync(issue, ct);
                tx.Complete();
                return ApiResponse<BookIssueDto>.SuccessResponse(replay, "Renewal already recorded.");
            }
            if (!MatchesVersion(issue.RowVersion, request.RowVersion))
                return Error("Loan changed. Reload and retry.", 409);
            var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
            if (issue.DueDate < today) return Error("Overdue loans cannot be renewed.", 409);
            if (request.NewDueDate <= issue.DueDate ||
                request.NewDueDate > issue.DueDate.AddDays(14) ||
                request.NewDueDate > today.AddDays(30))
                return Error("Renewal is limited to 14 extra days and 30 days from today.", 409);
            if (await audit.CountAsync(ct) >= 2)
                return Error("This loan has reached the two-renewal limit.", 409);
            var book = await _books.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == copy.BookId && x.IsActive && !x.IsDeleted, ct);
            if (book == null) return Error("Book is unavailable.", 409);
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == issue.StudentId &&
                x.StatusCode == "Active" && !x.IsDeleted, ct);
            if (student == null) return Error("Borrower is inactive.", 409);
            var enrollment = _enrollments.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.StudentId == student.Id && x.IsCurrent &&
                x.State == EnrollmentState.Active && !x.IsDeleted);
            if (!await enrollment.AnyAsync(ct))
                return Error("Current active enrollment required.", 409);
            long? campus = null;
            if (copy.LibraryBranchId.HasValue)
            {
                if (_branches == null) return Error("Branch lookup unavailable.", 503);
                campus = await _branches.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.Id == copy.LibraryBranchId.Value &&
                    x.IsActive && !x.IsDeleted)
                    .Select(x => (long?)x.CampusId).FirstOrDefaultAsync(ct);
                if (!campus.HasValue || !await enrollment.AnyAsync(x => x.CampusId == campus.Value, ct))
                    return Error("Borrower and copy campus differ.", 409);
            }
            var now = _clock.GetUtcNow().UtcDateTime;
            var queueCampus = campus;
            var waiting = _reservations.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.BookId == book.Id && !x.IsDeleted &&
                x.State == BookReservationState.Pending && x.ExpiresAt > now &&
                _students.GetQueryable().Any(s => s.TenantId == tenant && s.Id == x.StudentId &&
                    s.StatusCode == "Active" && !s.IsDeleted) &&
                _enrollments.GetQueryable().Any(e => e.TenantId == tenant && e.StudentId == x.StudentId &&
                    e.IsCurrent && e.State == EnrollmentState.Active && !e.IsDeleted &&
                    (!queueCampus.HasValue || e.CampusId == queueCampus.Value)));
            if (await waiting.AnyAsync(ct))
                return Error("An active reservation has priority over renewal.", 409);
            issue.DueDate = request.NewDueDate;
            issue.UpdatedAt = now;
            issue.UpdatedBy = _user.UserId;
            await _uow.SaveChangesAsync(ct); // BookIssue audit is persisted atomically.
            var response = MapIssue(issue, copy, book, student);
            tx.Complete();
            return ApiResponse<BookIssueDto>.SuccessResponse(response, "Loan renewed.");
        }
        catch (DbUpdateConcurrencyException) { return Error("Loan changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Library renewal conflict tenant {TenantId}", tenant);
            return Error("Loan renewal conflicts with another transaction.", 409);
        }
        catch (TransactionAbortedException) { return Error("Concurrent renewal rejected.", 409); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Library renewal failed tenant {TenantId}", tenant);
            return Error("Loan could not be renewed.", 500);
        }
    }

    public async Task<ApiResponse<PagedResult<BookRenewalHistoryDto>>> GetRenewalHistoryAsync(
        Guid reference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<BookRenewalHistoryDto>>();
        if (reference == Guid.Empty || page < 1 || pageSize is < 1 or > 50 ||
            ((long)page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<BookRenewalHistoryDto>>.ErrorResponse("Invalid renewal history request.");
        if (_renewalAudits == null)
            return ApiResponse<PagedResult<BookRenewalHistoryDto>>.ErrorResponse("Renewal audit unavailable.", 503);
        var tenant = _user.TenantId;
        var issue = await _issues.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.PublicId == reference && !x.IsDeleted)
            .Select(x => new { x.Id, x.BookCopyId }).FirstOrDefaultAsync(ct);
        if (issue == null)
            return ApiResponse<PagedResult<BookRenewalHistoryDto>>.ErrorResponse("Issue not found.", 404);
        var copy = await _copies.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.Id == issue.BookCopyId && !x.IsDeleted)
            .Select(x => new { x.LibraryBranchId }).FirstOrDefaultAsync(ct);
        if (copy == null)
            return ApiResponse<PagedResult<BookRenewalHistoryDto>>.ErrorResponse("Copy not found.", 404);
        if (!await StaffCanManageBranchAsync(copy.LibraryBranchId, ct))
            return Denied<PagedResult<BookRenewalHistoryDto>>();
        var query = RenewalEvents(tenant, issue.Id);
        var count = await query.CountAsync(ct);
        var entries = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.OldValue, x.NewValue, x.OccurredAt }).ToListAsync(ct);
        var rows = new List<BookRenewalHistoryDto>();
        foreach (var entry in entries)
        {
            if (ReadDueDate(entry.OldValue, out var previous) &&
                ReadDueDate(entry.NewValue, out var next) && next > previous)
                rows.Add(new BookRenewalHistoryDto
                { PreviousDueDate = previous, NewDueDate = next, ChangedAtUtc = entry.OccurredAt });
        }
        return ApiResponse<PagedResult<BookRenewalHistoryDto>>.SuccessResponse(
            new PagedResult<BookRenewalHistoryDto>
            { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    private static bool ReadDueDate(string? json, out DateOnly due)
    {
        due = default;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("DueDate", out var field) &&
                field.ValueKind == JsonValueKind.String &&
                DateOnly.TryParseExact(field.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out due);
        }
        catch (JsonException) { return false; }
    }
}
