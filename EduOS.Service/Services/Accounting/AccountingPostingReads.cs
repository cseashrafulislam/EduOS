using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingPostingService
{
    private static JournalDto Header(Journal j, string periodName, decimal debit, decimal credit) => new()
    {
        Id = j.Id, Reference = j.PublicId, JournalNumber = j.JournalNumber, JournalDate = j.JournalDate,
        AccountingPeriodId = j.AccountingPeriodId, AccountingPeriodName = periodName, State = j.State,
        SourceType = j.SourceType, SourceId = j.SourceId, ReversalOfJournalId = j.ReversalOfJournalId,
        Description = j.Description, TotalDebit = debit, TotalCredit = credit,
        ApprovedByUserId = j.ApprovedByUserId, ApprovedAt = j.ApprovedAt,
        PostedByUserId = j.PostedByUserId, PostedAt = j.PostedAt, RowVersion = V(j.RowVersion)
    };
    private async Task<JournalDto> DetailAsync(Journal j, CancellationToken ct)
    {
        var period = await Periods.AsNoTracking().Where(x => x.Id == j.AccountingPeriodId)
            .Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "[Missing period]";
        var lines = await Lines.AsNoTracking().Where(x => x.JournalId == j.Id)
            .OrderBy(x => x.LineNo).ToListAsync(ct);
        var ids = lines.Select(x => x.AccountId).Distinct().ToArray();
        var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && ids.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, ct);
        var result = Header(j, period, lines.Sum(x => x.DebitAmount), lines.Sum(x => x.CreditAmount));
        result.Lines = lines.Select(x =>
        {
            accounts.TryGetValue(x.AccountId, out var account);
            return new JournalLineDto
            {
                Id = x.Id, LineNo = x.LineNo, AccountId = x.AccountId,
                AccountCode = account?.Code ?? "[Missing]", AccountName = account?.Name ?? "[Missing]",
                DebitAmount = x.DebitAmount, CreditAmount = x.CreditAmount, Description = x.Description
            };
        }).ToArray();
        return result;
    }
    public async Task<ApiResponse<PagedResult<JournalDto>>> GetJournalsAsync(
        DateOnly fromDate, DateOnly toDate, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead) return Denied<PagedResult<JournalDto>>();
        if (fromDate == default || toDate < fromDate || toDate.DayNumber - fromDate.DayNumber > 3660)
            return ApiResponse<PagedResult<JournalDto>>.ErrorResponse("Valid date range up to ten years required.");
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = Journals.AsNoTracking().Where(x => x.JournalDate >= fromDate && x.JournalDate <= toDate);
        var total = await query.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        var rows = offset > int.MaxValue ? new List<Journal>() : await query
            .OrderByDescending(x => x.JournalDate).ThenByDescending(x => x.Id)
            .Skip((int)offset).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var periodIds = rows.Select(x => x.AccountingPeriodId).Distinct().ToArray();
        var sums = await Lines.AsNoTracking().Where(x => ids.Contains(x.JournalId))
            .GroupBy(x => x.JournalId)
            .Select(g => new { Id = g.Key, Debit = g.Sum(x => x.DebitAmount), Credit = g.Sum(x => x.CreditAmount) })
            .ToDictionaryAsync(x => x.Id, ct);
        var periods = await Periods.AsNoTracking().Where(x => periodIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var items = rows.Select(x =>
        {
            sums.TryGetValue(x.Id, out var sum);
            return Header(x, periods.GetValueOrDefault(x.AccountingPeriodId) ?? "[Missing period]",
                sum?.Debit ?? 0m, sum?.Credit ?? 0m);
        }).ToList();
        return ApiResponse<PagedResult<JournalDto>>.SuccessResponse(new PagedResult<JournalDto>
        { Items = items, TotalCount = total, Page = page, PageSize = pageSize });
    }
    public async Task<ApiResponse<JournalDto>> GetJournalAsync(Guid journalReference, CancellationToken ct = default)
    {
        if (!CanRead) return Denied<JournalDto>();
        var j = await Journals.AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == journalReference, ct);
        if (j == null) return ApiResponse<JournalDto>.ErrorResponse("Journal not found.", 404);
        var dto = await DetailAsync(j, ct);
        if (dto.AccountingPeriodName == "[Missing period]" || dto.Lines.Any(x => x.AccountCode == "[Missing]"))
            return ApiResponse<JournalDto>.ErrorResponse("Journal contains missing accounting references.", 409);
        return ApiResponse<JournalDto>.SuccessResponse(dto);
    }
}
