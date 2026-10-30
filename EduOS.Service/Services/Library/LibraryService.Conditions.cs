using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;
using System.Transactions;

namespace EduOS.Service.Services.Library;

public sealed partial class LibraryService
{
    private const string CopyConditionAction = "LibraryCopy.Condition";

    /// <summary>Tenant/campus-scoped, bounded stock register. No borrower names or person IDs.</summary>
    public async Task<ApiResponse<PagedResult<LibraryCopyConditionDto>>> GetCopyConditionsAsync(
        string? accession, string? state, long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<LibraryCopyConditionDto>>();
        if (_branches == null)
            return ApiResponse<PagedResult<LibraryCopyConditionDto>>.ErrorResponse("Branch lookup unavailable.", 503);
        if (page < 1 || pageSize is < 1 or > 50 || accession?.Length > 100 || campusId is <= 0 ||
            ((long)page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<LibraryCopyConditionDto>>.ErrorResponse("Invalid copy condition filters.");
        state = (state ?? "all").Trim().ToLowerInvariant();
        BookCopyState parsedState = default;
        if (state != "all" && (!Enum.TryParse(state, true, out parsedState) ||
            !Enum.IsDefined(parsedState)))
            return ApiResponse<PagedResult<LibraryCopyConditionDto>>.ErrorResponse("Invalid copy condition state.");

        var tenant = _user.TenantId;
        var branches = _branches.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted);
        var copies = _copies.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted);
        if (IsCampusScopedStaff)
        {
            var allowed = AllowedStaffCampusIds(tenant,
                DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime));
            if (allowed == null) return Denied<PagedResult<LibraryCopyConditionDto>>();
            var branchIds = branches.Where(x => allowed.Contains(x.CampusId)).Select(x => x.Id);
            copies = copies.Where(x => x.LibraryBranchId.HasValue &&
                branchIds.Contains(x.LibraryBranchId.Value));
        }
        if (campusId.HasValue)
        {
            var branchIds = branches.Where(x => x.CampusId == campusId.Value).Select(x => x.Id);
            copies = copies.Where(x => x.LibraryBranchId.HasValue &&
                branchIds.Contains(x.LibraryBranchId.Value));
        }
        if (state != "all") copies = copies.Where(x => x.State == parsedState);
        if (!string.IsNullOrWhiteSpace(accession))
        {
            var term = accession.Trim();
            copies = copies.Where(x => x.AccessionNumber.Contains(term));
        }
        var count = await copies.CountAsync(ct);
        var pageCopies = await copies.OrderBy(x => x.AccessionNumber).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var copyIds = pageCopies.Select(x => x.Id).ToArray();
        var bookIds = pageCopies.Select(x => x.BookId).Distinct().ToArray();
        var branchIdsPage = pageCopies.Where(x => x.LibraryBranchId.HasValue)
            .Select(x => x.LibraryBranchId!.Value).Distinct().ToArray();
        var bookTitles = await _books.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && bookIds.Contains(x.Id) && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Id, x => x.Title, ct);
        var branchRows = await branches.Where(x => branchIdsPage.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var activeLoans = await _issues.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.State == BookIssueState.Issued &&
            copyIds.Contains(x.BookCopyId)).ToListAsync(ct);
        var grouped = activeLoans.GroupBy(x => x.BookCopyId).ToDictionary(x => x.Key, x => x.ToList());
        var result = pageCopies.Select(copy =>
        {
            branchRows.TryGetValue(copy.LibraryBranchId ?? 0, out var branch);
            grouped.TryGetValue(copy.Id, out var loans);
            var loan = loans?.FirstOrDefault();
            return new LibraryCopyConditionDto
            {
                CopyId = copy.Id, AccessionNumber = copy.AccessionNumber,
                BookTitle = bookTitles.GetValueOrDefault(copy.BookId) ?? "(missing book)",
                CampusId = branch?.CampusId, BranchCode = branch?.Code,
                State = copy.State, RowVersion = Convert.ToBase64String(copy.RowVersion),
                OpenIssueCount = loans?.Count ?? 0, OpenIssueReference = loan?.PublicId,
                OpenIssueRowVersion = loan == null ? null : Convert.ToBase64String(loan.RowVersion)
            };
        }).ToList();
        return ApiResponse<PagedResult<LibraryCopyConditionDto>>.SuccessResponse(
            new PagedResult<LibraryCopyConditionDto>
            { Items = result, TotalCount = count, Page = page, PageSize = pageSize });
    }

    /// <summary>
    /// A lost/damaged loan closes as an incident, never as a return or payment.
    /// No Finance posting is attempted without an approved V1 liability contract.
    /// </summary>
    public async Task<ApiResponse<LibraryCopyConditionDto>> ChangeCopyConditionAsync(
        long copyId, ChangeLibraryCopyConditionRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<LibraryCopyConditionDto>();
        if (copyId <= 0 || request == null || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RowVersion) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 300 ||
            request.TargetState is not (BookCopyState.Available or BookCopyState.Lost or
                BookCopyState.Damaged or BookCopyState.Retired))
            return ApiResponse<LibraryCopyConditionDto>.ErrorResponse("Valid state, reason, request ID and rowversion required.");
        if (request.TargetState is BookCopyState.Available or BookCopyState.Retired && !_user.IsTenantAdmin)
            return Denied<LibraryCopyConditionDto>();
        if (_renewalAudits == null)
            return ApiResponse<LibraryCopyConditionDto>.ErrorResponse("Immutable condition audit unavailable.", 503);
        var tenant = _user.TenantId;
        var reason = request.Reason.Trim();
        var requestId = request.ClientRequestId.ToString("N");
        var serialized = JsonSerializer.Serialize(new { State = request.TargetState.ToString(), Reason = reason });
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var copy = await _copies.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == copyId && !x.IsDeleted, token);
                if (copy == null)
                    return ApiResponse<LibraryCopyConditionDto>.ErrorResponse("Copy not found.", 404);
                if (!await StaffCanManageBranchAsync(copy.LibraryBranchId, token))
                    return Denied<LibraryCopyConditionDto>();
                var previousEvent = await _renewalAudits.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Action == CopyConditionAction &&
                    x.RequestId == requestId, token);
                if (previousEvent != null)
                {
                    if (previousEvent.EntityId != copyId || previousEvent.NewValue != serialized ||
                        copy.State != request.TargetState)
                        return ApiResponse<LibraryCopyConditionDto>.ErrorResponse("Request ID was used for a different transition.", 409);
                    return ApiResponse<LibraryCopyConditionDto>.SuccessResponse(
                        await ConditionSnapshotAsync(copy, token), "Condition already recorded.");
                }
                if (!MatchesVersion(copy.RowVersion, request.RowVersion))
                    return ApiResponse<LibraryCopyConditionDto>.ErrorResponse("Copy changed. Reload and retry.", 409);
                if (copy.State == BookCopyState.Retired || copy.State == request.TargetState)
                    return ApiResponse<LibraryCopyConditionDto>.ErrorResponse("Copy is already in this or a terminal state.", 409);
                var open = await _issues.GetQueryable().Where(x =>
                    x.TenantId == tenant && x.BookCopyId == copyId && !x.IsDeleted &&
                    x.State == BookIssueState.Issued).Take(2).ToListAsync(token);
                if (open.Count > 1 || (copy.State == BookCopyState.Issued) != (open.Count == 1))
                    return ApiResponse<LibraryCopyConditionDto>.ErrorResponse("Copy/loan integrity conflict; reconcile before changing condition.", 409);
                if (copy.State == BookCopyState.Issued)
                {
                    if (request.TargetState is not (BookCopyState.Lost or BookCopyState.Damaged))
                        return ApiResponse<LibraryCopyConditionDto>.ErrorResponse("Outstanding loans may only be closed as lost or damaged.", 409);
                    if (!MatchesVersion(open[0].RowVersion, request.OpenIssueRowVersion))
                        return ApiResponse<LibraryCopyConditionDto>.ErrorResponse("Loan changed. Reload and retry.", 409);
                    open[0].State = request.TargetState == BookCopyState.Lost
                        ? BookIssueState.Lost : BookIssueState.Damaged;
                    open[0].UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                    open[0].UpdatedBy = _user.UserId;
                    // Keep ReturnDate and FineAmount untouched: no fictitious return or receivable.
                }
                else if (copy.State is not (BookCopyState.Available or BookCopyState.Lost or BookCopyState.Damaged))
                    return ApiResponse<LibraryCopyConditionDto>.ErrorResponse("Copy is reserved or otherwise unavailable.", 409);
                var before = copy.State;
                var now = _clock.GetUtcNow().UtcDateTime;
                copy.State = request.TargetState;
                copy.UpdatedAt = now; copy.UpdatedBy = _user.UserId;
                await _renewalAudits.AddAsync(new AuditLog
                {
                    TenantId = tenant, UserId = _user.UserId, Action = CopyConditionAction,
                    EntityName = "BookCopy", EntityId = copy.Id,
                    OldValue = before.ToString(), NewValue = serialized,
                    RequestId = requestId, OccurredAt = now, IsSuccess = true
                });
                await _uow.SaveChangesAsync(token);
                return ApiResponse<LibraryCopyConditionDto>.SuccessResponse(
                    await ConditionSnapshotAsync(copy, token), "Copy condition recorded.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        { return ApiResponse<LibraryCopyConditionDto>.ErrorResponse("Concurrent copy or loan update rejected.", 409); }
        catch (DbUpdateException)
        { return ApiResponse<LibraryCopyConditionDto>.ErrorResponse("Concurrent condition write rejected.", 409); }
        catch (TransactionAbortedException)
        { return ApiResponse<LibraryCopyConditionDto>.ErrorResponse("Concurrent condition transaction rejected.", 409); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Library condition transition failed tenant {TenantId}", tenant);
            return ApiResponse<LibraryCopyConditionDto>.ErrorResponse("Copy condition could not be recorded.", 500);
        }
    }

    public async Task<ApiResponse<PagedResult<LibraryCopyConditionHistoryDto>>> GetCopyConditionHistoryAsync(
        long copyId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<LibraryCopyConditionHistoryDto>>();
        if (copyId <= 0 || page < 1 || pageSize is < 1 or > 50 ||
            ((long)page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<LibraryCopyConditionHistoryDto>>.ErrorResponse("Invalid condition history request.");
        if (_renewalAudits == null)
            return ApiResponse<PagedResult<LibraryCopyConditionHistoryDto>>.ErrorResponse("Condition audit unavailable.", 503);
        var tenant = _user.TenantId;
        var copy = await _copies.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == copyId && !x.IsDeleted, ct);
        if (copy == null)
            return ApiResponse<PagedResult<LibraryCopyConditionHistoryDto>>.ErrorResponse("Copy not found.", 404);
        if (!await StaffCanManageBranchAsync(copy.LibraryBranchId, ct))
            return Denied<PagedResult<LibraryCopyConditionHistoryDto>>();
        var query = _renewalAudits.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.EntityName == "BookCopy" &&
            x.EntityId == copyId && x.Action == CopyConditionAction);
        var count = await query.CountAsync(ct);
        var entries = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.OldValue, x.NewValue, x.OccurredAt }).ToListAsync(ct);
        var rows = new List<LibraryCopyConditionHistoryDto>();
        foreach (var entry in entries)
        {
            if (entry.NewValue == null) continue;
            try
            {
                using var json = JsonDocument.Parse(entry.NewValue);
                rows.Add(new LibraryCopyConditionHistoryDto
                {
                    PreviousState = entry.OldValue ?? "",
                    NewState = json.RootElement.GetProperty("State").GetString() ?? "",
                    Reason = json.RootElement.GetProperty("Reason").GetString() ?? "",
                    OccurredAtUtc = entry.OccurredAt
                });
            }
            catch (JsonException) { /* Preserve legacy malformed audit, never invent a transition. */ }
            catch (KeyNotFoundException) { }
        }
        return ApiResponse<PagedResult<LibraryCopyConditionHistoryDto>>.SuccessResponse(
            new PagedResult<LibraryCopyConditionHistoryDto>
            { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    private async Task<LibraryCopyConditionDto> ConditionSnapshotAsync(BookCopy copy, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var title = await _books.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Id == copy.BookId && !x.IsDeleted)
            .Select(x => x.Title).FirstOrDefaultAsync(ct);
        var branch = copy.LibraryBranchId.HasValue && _branches != null
            ? await _branches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == copy.LibraryBranchId.Value && !x.IsDeleted, ct)
            : null;
        var open = await _issues.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.BookCopyId == copy.Id && !x.IsDeleted &&
            x.State == BookIssueState.Issued).Take(2).ToListAsync(ct);
        return new LibraryCopyConditionDto
        {
            CopyId = copy.Id, AccessionNumber = copy.AccessionNumber,
            BookTitle = title ?? "(missing book)", CampusId = branch?.CampusId,
            BranchCode = branch?.Code, State = copy.State,
            RowVersion = Convert.ToBase64String(copy.RowVersion),
            OpenIssueCount = open.Count, OpenIssueReference = open.FirstOrDefault()?.PublicId,
            OpenIssueRowVersion = open.Count == 0 ? null : Convert.ToBase64String(open[0].RowVersion)
        };
    }
}
