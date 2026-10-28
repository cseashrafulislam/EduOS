using EduOS.Core.Common;
using EduOS.Core.DTOs.Auth;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace EduOS.Service.Services.Auth;

public sealed class LoginActivityService(ILoginHistoryRepository history, ICurrentUserService user) : ILoginActivityService
{
    // UTC calendar dates are inclusive; incomplete, inverted and >365-day ranges are rejected.
    // The exclusive upper bound is safe because DateOnly.MaxValue is rejected.
    private static bool ValidRange(DateOnly? from, DateOnly? to) =>
        (from is null && to is null) ||
        (from.HasValue && to.HasValue && from.Value <= to.Value &&
         to.Value < DateOnly.MaxValue && to.Value.DayNumber - from.Value.DayNumber <= 365);

    private IQueryable<LoginHistory> ScopedHistory(bool? success, DateOnly? from, DateOnly? to, bool unassigned)
    {
        // Do not remove the context query filter. One global account can belong to
        // multiple institutions; both user and current institution must match.
        var query = history.GetQueryable().AsNoTracking().Where(x => x.UserId == user.UserId);
        // Unassigned attempts are account-global, not another institution's history.
        // Keep the user predicate in both branches and never bypass query filters.
        query = unassigned
            ? query.Where(x => x.TenantId == null)
            : query.Where(x => x.TenantId == user.TenantId);
        if (success.HasValue) query = query.Where(x => x.IsSuccess == success.Value);
        if (from.HasValue && to.HasValue)
        {
            var start = DateTime.SpecifyKind(from.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var end = DateTime.SpecifyKind(to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            query = query.Where(x => x.OccurredAt >= start && x.OccurredAt < end);
        }
        return query;
    }

    public async Task<ApiResponse<PagedResult<LoginHistoryDto>>> GetMyHistoryAsync(
        int page, int pageSize, bool? success, CancellationToken cancellationToken = default,
        DateOnly? from = null, DateOnly? to = null, bool unassigned = false)
    {
        if (!user.IsAuthenticated || user.UserId <= 0 || user.TenantId <= 0)
            return ApiResponse<PagedResult<LoginHistoryDto>>.ErrorResponse("Institution session required.", 403);
        if (page < 1 || pageSize is < 1 or > 50 ||
            (long)(page - 1) * pageSize > int.MaxValue || !ValidRange(from, to))
            return ApiResponse<PagedResult<LoginHistoryDto>>.ErrorResponse("Invalid pagination or UTC date range.", 400);

        var query = ScopedHistory(success, from, to, unassigned);
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new LoginHistoryDto { Id = x.Id, OccurredAt = x.OccurredAt, IsSuccess = x.IsSuccess })
            .ToListAsync(cancellationToken);
        foreach (var item in items) item.OccurredAt = DateTime.SpecifyKind(item.OccurredAt, DateTimeKind.Utc);
        return ApiResponse<PagedResult<LoginHistoryDto>>.SuccessResponse(new PagedResult<LoginHistoryDto>
        { Items = items, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<string>> ExportMyHistoryCsvAsync(
        bool? success, CancellationToken cancellationToken = default,
        DateOnly? from = null, DateOnly? to = null, bool unassigned = false)
    {
        if (!user.IsAuthenticated || user.UserId <= 0 || user.TenantId <= 0)
            return ApiResponse<string>.ErrorResponse("Institution session required.", 403);
        if (!ValidRange(from, to))
            return ApiResponse<string>.ErrorResponse("Invalid UTC date range.", 400);

        // One stable, ordered SQL query. Reject rather than silently truncate evidence.
        // Never export IP, user agent, failure reason, user ID or another tenant's events.
        var rows = await ScopedHistory(success, from, to, unassigned)
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Take(1001)
            .Select(x => new { x.OccurredAt, x.IsSuccess })
            .ToListAsync(cancellationToken);
        if (rows.Count > 1000)
            return ApiResponse<string>.ErrorResponse("Sign-in history exceeds the 1,000-row export limit. Narrow the UTC date range.", 413);

        var csv = new StringBuilder("\uFEFFOccurred UTC,Result\r\n");
        foreach (var row in rows)
        {
            csv.Append(CsvCell(DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc).ToString("O")))
               .Append(',').Append(CsvCell(row.IsSuccess ? "Successful" : "Unsuccessful"))
               .Append("\r\n");
        }
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }

    // Defensive CSV encoding for future additions, even though current columns are fixed.
    public static string CsvCell(string? value)
    {
        var text = value ?? string.Empty;
        var trimmed = text.TrimStart(' ', '\t', '\r', '\n');
        if (trimmed.Length > 0 && (trimmed[0] is '=' or '+' or '-' or '@'))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
