using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Library;

public sealed partial class LibraryLiabilityService
{
    internal static decimal CalculateOverdueAmount(LibraryFinePolicy policy, int daysOverdue)
    {
        if (daysOverdue <= policy.GraceDays || policy.DailyRate <= 0m) return 0m;
        var overdue = (decimal)(daysOverdue - policy.GraceDays) * policy.DailyRate;
        return policy.MaximumCharge.HasValue ? decimal.Min(overdue, policy.MaximumCharge.Value) : overdue;
    }

    /// <summary>Issue-date policy is immutable; creating a new revision never reprices a historical loan.</summary>
    public async Task<ApiResponse<PagedResult<LibraryFinePolicyDto>>> FinePoliciesAsync(
        long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Allowed) return Error<PagedResult<LibraryFinePolicyDto>>("Institution administrator permission required.", 403);
        if (!PageValid(page, pageSize) || campusId is <= 0)
            return Error<PagedResult<LibraryFinePolicyDto>>("Invalid campus or pagination.");
        var tenant = user.TenantId;
        var query = from policy in db.LibraryFinePolicies.AsNoTracking()
                    join campus in db.Campuses.AsNoTracking() on new { policy.TenantId, Id = policy.CampusId }
                        equals new { campus.TenantId, campus.Id }
                    where policy.TenantId == tenant && !policy.IsDeleted && !campus.IsDeleted
                    select new { policy, campus };
        if (campusId.HasValue) query = query.Where(x => x.policy.CampusId == campusId.Value);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.policy.EffectiveFrom).ThenByDescending(x => x.policy.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new LibraryFinePolicyDto
            {
                Id = x.policy.Id, CampusId = x.policy.CampusId, CampusName = x.campus.Name,
                EffectiveFrom = x.policy.EffectiveFrom, GraceDays = x.policy.GraceDays,
                DailyRate = x.policy.DailyRate, MaximumCharge = x.policy.MaximumCharge
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<LibraryFinePolicyDto>>.SuccessResponse(new PagedResult<LibraryFinePolicyDto>
        { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<LibraryFinePolicyDto>> CreateFinePolicyAsync(
        SaveLibraryFinePolicyDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Error<LibraryFinePolicyDto>("Institution administrator permission required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.CampusId <= 0 ||
            request.EffectiveFrom == default || request.GraceDays is < 0 or > 36500 ||
            request.DailyRate < 0m || decimal.Round(request.DailyRate, 2) != request.DailyRate ||
            request.DailyRate > 999999999999.99m ||
            (request.MaximumCharge.HasValue && !Money(request.MaximumCharge.Value)))
            return Error<LibraryFinePolicyDto>("Valid campus, effective date, days, BDT rate and request ID are required.");
        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var previous = await db.LibraryFinePolicies.FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, ct);
            if (previous != null)
            {
                if (previous.CampusId != request.CampusId || previous.EffectiveFrom != request.EffectiveFrom ||
                    previous.GraceDays != request.GraceDays || previous.DailyRate != request.DailyRate ||
                    previous.MaximumCharge != request.MaximumCharge)
                    return Error<LibraryFinePolicyDto>("Request ID belongs to a different policy revision.", 409);
                var campusName = await db.Campuses.AsNoTracking().Where(x => x.TenantId == tenant &&
                    x.Id == previous.CampusId).Select(x => x.Name).FirstOrDefaultAsync(ct);
                return ApiResponse<LibraryFinePolicyDto>.SuccessResponse(ToPolicyDto(previous, campusName ?? ""));
            }
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (request.EffectiveFrom < today)
                return Error<LibraryFinePolicyDto>("Policy revisions cannot be backdated over existing loans.", 409);
            var campus = await db.Campuses.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == request.CampusId && !x.IsDeleted && x.IsActive, ct);
            if (campus == null) return Error<LibraryFinePolicyDto>("Active institution campus not found.", 404);
            if (await db.LibraryFinePolicies.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant &&
                x.CampusId == request.CampusId && x.EffectiveFrom == request.EffectiveFrom, ct))
                return Error<LibraryFinePolicyDto>("A policy already begins on that date for this campus.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var policy = new LibraryFinePolicy
            {
                TenantId = tenant, CampusId = request.CampusId, ClientRequestId = request.ClientRequestId,
                EffectiveFrom = request.EffectiveFrom, GraceDays = request.GraceDays,
                DailyRate = request.DailyRate, MaximumCharge = request.MaximumCharge,
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.LibraryFinePolicies.Add(policy);
            await db.SaveChangesAsync(ct);
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenant, UserId = user.UserId, Action = "CreateLibraryFinePolicy",
                EntityName = "LibraryFinePolicy", EntityId = policy.Id,
                NewValue = $"Campus {request.CampusId}: {request.EffectiveFrom}: {request.DailyRate:0.00} BDT/day",
                RequestId = request.ClientRequestId.ToString("N"), OccurredAt = now, IsSuccess = true
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<LibraryFinePolicyDto>.SuccessResponse(ToPolicyDto(policy, campus.Name));
        }
        catch (DbUpdateConcurrencyException) { return Error<LibraryFinePolicyDto>("Policy was updated concurrently.", 409); }
        catch (DbUpdateException) { return Error<LibraryFinePolicyDto>("Policy revision conflicts with an existing entry.", 409); }
    }

    private static LibraryFinePolicyDto ToPolicyDto(LibraryFinePolicy policy, string campus) => new()
    {
        Id = policy.Id, CampusId = policy.CampusId, CampusName = campus,
        EffectiveFrom = policy.EffectiveFrom, GraceDays = policy.GraceDays,
        DailyRate = policy.DailyRate, MaximumCharge = policy.MaximumCharge
    };

    /// <summary>Only returned overdue student loans; candidate quotes are revalidated inside AssessAsync's transaction.</summary>
    public async Task<ApiResponse<PagedResult<LibraryIncidentDto>>> OverdueCandidatesAsync(
        string? search, long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Allowed) return Error<PagedResult<LibraryIncidentDto>>("Institution administrator permission required.", 403);
        if (!PageValid(page, pageSize) || campusId is <= 0 || search?.Length > 100)
            return Error<PagedResult<LibraryIncidentDto>>("Invalid overdue filters.");
        var tenant = user.TenantId;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var query = from issue in db.BookIssues.AsNoTracking()
                    join copy in db.BookCopies.AsNoTracking() on new { issue.TenantId, Id = issue.BookCopyId }
                        equals new { copy.TenantId, copy.Id }
                    join branch in db.LibraryBranches.AsNoTracking() on new { copy.TenantId, Id = copy.LibraryBranchId }
                        equals new { branch.TenantId, Id = (long?)branch.Id }
                    join book in db.Books.AsNoTracking() on new { copy.TenantId, Id = copy.BookId }
                        equals new { book.TenantId, book.Id }
                    join student in db.Students.AsNoTracking() on new { issue.TenantId, Id = issue.StudentId }
                        equals new { student.TenantId, student.Id }
                    where issue.TenantId == tenant && !issue.IsDeleted && !copy.IsDeleted &&
                        !branch.IsDeleted && !book.IsDeleted && !student.IsDeleted &&
                        issue.State == BookIssueState.Returned && issue.ReturnDate.HasValue &&
                        issue.ReturnDate.Value > issue.DueDate && issue.ReturnDate.Value <= today &&
                        !db.LibraryLiabilities.Any(c => c.TenantId == tenant && c.BookIssueId == issue.Id)
                    select new { issue, copy, branch, book, student };
        if (campusId.HasValue) query = query.Where(x => x.branch.CampusId == campusId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.copy.AccessionNumber.Contains(term) ||
                x.book.Title.Contains(term) || x.student.FullName.Contains(term) ||
                x.student.StudentCode.Contains(term));
        }
        var count = await query.CountAsync(ct);
        var pageRows = await query.OrderByDescending(x => x.issue.ReturnDate).ThenByDescending(x => x.issue.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.issue.PublicId, x.issue.IssueDate, x.issue.CreatedAt, x.issue.DueDate, x.issue.ReturnDate,
                x.branch.CampusId, x.copy.AccessionNumber, BookTitle = x.book.Title,
                StudentName = x.student.FullName, StudentCode = x.student.StudentCode
            }).ToListAsync(ct);
        var campusIds = pageRows.Select(x => x.CampusId).Distinct().ToArray();
        var latestIssueDate = pageRows.Count == 0 ? default : pageRows.Max(x => x.IssueDate);
        var latestIssueCreation = pageRows.Count == 0 ? default : pageRows.Max(x => x.CreatedAt);
        var policies = pageRows.Count == 0 ? new List<LibraryFinePolicy>() :
            await db.LibraryFinePolicies.AsNoTracking().Where(p => p.TenantId == tenant &&
                campusIds.Contains(p.CampusId) && !p.IsDeleted &&
                p.EffectiveFrom <= latestIssueDate && p.CreatedAt <= latestIssueCreation)
                .OrderByDescending(p => p.EffectiveFrom).ThenByDescending(p => p.Id).ToListAsync(ct);
        var result = pageRows.Select(x =>
        {
            var days = x.ReturnDate!.Value.DayNumber - x.DueDate.DayNumber;
            var activePolicy = policies.FirstOrDefault(p => p.CampusId == x.CampusId &&
                p.EffectiveFrom <= x.IssueDate && p.CreatedAt <= x.CreatedAt);
            var amount = activePolicy == null ? 0m : CalculateOverdueAmount(activePolicy, days);
            return new LibraryIncidentDto
            {
                IssueReference = x.PublicId, AccessionNumber = x.AccessionNumber,
                BookTitle = x.BookTitle, StudentName = x.StudentName, StudentCode = x.StudentCode,
                CampusId = x.CampusId, Kind = LibraryLiabilityKind.Overdue,
                CalculatedAmount = Money(amount) ? amount : null, OverdueDays = days,
                PolicyStatus = activePolicy == null ? "No issue-date policy" :
                    !Money(amount) ? "No billable amount" : "Ready",
                Assessed = false
            };
        }).ToList();
        return ApiResponse<PagedResult<LibraryIncidentDto>>.SuccessResponse(new PagedResult<LibraryIncidentDto>
        { Items = result, TotalCount = count, Page = page, PageSize = pageSize });
    }
}
