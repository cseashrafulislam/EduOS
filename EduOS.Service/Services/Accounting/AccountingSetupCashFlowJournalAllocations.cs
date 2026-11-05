using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingSetupService
{
    private IQueryable<CashFlowJournalAllocation> FlowAllocations => db.CashFlowJournalAllocations
        .Where(x => x.TenantId == user.TenantId && !x.IsDeleted);

    private static CashFlowJournalAllocationDto AllocationMap(CashFlowJournalAllocation x, Journal journal) => new()
    {
        Id = x.Id, JournalId = x.JournalId, JournalNumber = journal.JournalNumber,
        JournalDate = journal.JournalDate, Direction = x.Direction,
        OperatingAmount = x.OperatingAmount, InvestingAmount = x.InvestingAmount,
        FinancingAmount = x.FinancingAmount, StateCode = x.StateCode, Reason = x.Reason,
        SubmittedByUserId = x.SubmittedByUserId, SubmittedAtUtc = x.SubmittedAtUtc,
        ReviewedByUserId = x.ReviewedByUserId, ReviewedAtUtc = x.ReviewedAtUtc,
        ReviewReason = x.ReviewReason, RowVersion = V(x.RowVersion)
    };

    private async Task<(Journal? Journal, string Direction, decimal Amount, string Fingerprint)> JournalCashProof(
        long journalId, CancellationToken ct)
    {
        var journal = await db.Journals.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && x.Id == journalId && x.State == JournalState.Posted && x.CurrencyCode == "BDT", ct);
        if (journal == null) return (null, "", 0m, "");
        var lines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.JournalId == journalId && !x.IsDeleted).OrderBy(x => x.LineNo).ThenBy(x => x.Id).ToListAsync(ct);
        if (lines.Count < 2 || lines.Any(x => x.DebitAmount < 0 || x.CreditAmount < 0 ||
            (x.DebitAmount > 0) == (x.CreditAmount > 0)) ||
            lines.Sum(x => x.DebitAmount) != lines.Sum(x => x.CreditAmount)) return (null, "", 0m, "");
        var bankIds = await db.BankAccounts.AsNoTracking().Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
            x.CurrencyCode == "BDT").Select(x => x.AccountId).ToListAsync(ct);
        var cashIds = await db.Accounts.AsNoTracking().Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
            x.IsCashInHand && x.IsActive && !x.IsGroup && x.AllowsPosting && x.CurrencyCode == "BDT" &&
            x.AccountType == AccountType.Asset).Select(x => x.Id).ToListAsync(ct);
        if (bankIds.Count != bankIds.Distinct().Count() || cashIds.Intersect(bankIds).Any())
            return (null, "", 0m, "");
        var liquidIds = cashIds.Concat(bankIds).ToHashSet();
        var cashLines = lines.Where(x => liquidIds.Contains(x.AccountId)).ToArray();
        if (cashLines.Length == 0 || cashLines.Length == lines.Count) return (null, "", 0m, "");
        var inflows = cashLines.Sum(x => x.DebitAmount);
        var outflows = cashLines.Sum(x => x.CreditAmount);
        // The existing institution cash-movement control prohibits mixed
        // cash-direction journals. A manual allocation must not bypass it.
        if ((inflows > 0m) == (outflows > 0m)) return (null, "", 0m, "");
        return (journal, inflows > 0m ? "Inflow" : "Outflow", inflows > 0m ? inflows : outflows,
            CashFlowJournalProof.Fingerprint(journal, lines));
    }

    public async Task<ApiResponse<PagedResult<CashFlowJournalAllocationDto>>> GetCashFlowJournalAllocationsAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        if (!await CashFlowActor(false, ct)) return Denied<PagedResult<CashFlowJournalAllocationDto>>();
        if (page is < 1 or > 100000 || pageSize is < 1 or > 50)
            return ApiResponse<PagedResult<CashFlowJournalAllocationDto>>.ErrorResponse("Valid page and size required.", 400);
        var count = await FlowAllocations.CountAsync(ct);
        var rows = await (from a in FlowAllocations.AsNoTracking()
            join j in db.Journals.AsNoTracking().Where(x => x.TenantId == user.TenantId && !x.IsDeleted)
                on a.JournalId equals j.Id
            orderby a.Id descending
            select new { Allocation = a, Journal = j })
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<CashFlowJournalAllocationDto>>.SuccessResponse(new()
        {
            Page = page, PageSize = pageSize, TotalCount = count,
            Items = rows.Select(x => AllocationMap(x.Allocation, x.Journal)).ToList()
        });
    }

    public async Task<ApiResponse<CashFlowJournalAllocationDto>> SubmitCashFlowJournalAllocationAsync(
        SubmitCashFlowJournalAllocationDto request, CancellationToken ct = default)
    {
        if (!await CashFlowActor(false, ct)) return Denied<CashFlowJournalAllocationDto>();
        if (request == null || request.JournalId <= 0 || request.ClientRequestId == Guid.Empty ||
            request.OperatingAmount < 0m || request.InvestingAmount < 0m || request.FinancingAmount < 0m ||
            request.OperatingAmount > 9999999999999999m || request.InvestingAmount > 9999999999999999m ||
            request.FinancingAmount > 9999999999999999m ||
            decimal.Round(request.OperatingAmount, 2) != request.OperatingAmount ||
            decimal.Round(request.InvestingAmount, 2) != request.InvestingAmount ||
            decimal.Round(request.FinancingAmount, 2) != request.FinancingAmount ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 20 or > 500)
            return ApiResponse<CashFlowJournalAllocationDto>.ErrorResponse("Valid journal, exact BDT allocations and 20–500 character evidence required.", 400);
        var reason = request.Reason.Trim();
        try
        {
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var previous = await FlowAllocations.FirstOrDefaultAsync(x => x.ClientRequestId == request.ClientRequestId, ct);
            if (previous != null)
            {
                if (previous.SubmittedByUserId != user.UserId || previous.JournalId != request.JournalId ||
                    previous.OperatingAmount != request.OperatingAmount || previous.InvestingAmount != request.InvestingAmount ||
                    previous.FinancingAmount != request.FinancingAmount || previous.Reason != reason)
                    return ApiResponse<CashFlowJournalAllocationDto>.ErrorResponse("Request reference already used with different allocation.", 409);
                var oldJournal = await db.Journals.AsNoTracking().SingleAsync(x => x.TenantId == user.TenantId && x.Id == previous.JournalId, ct);
                return ApiResponse<CashFlowJournalAllocationDto>.SuccessResponse(AllocationMap(previous, oldJournal));
            }
            var proof = await JournalCashProof(request.JournalId, ct);
            if (proof.Journal == null || request.OperatingAmount + request.InvestingAmount + request.FinancingAmount != proof.Amount)
                return ApiResponse<CashFlowJournalAllocationDto>.ErrorResponse("Allocation must equal the exact posted external cash movement on this valid journal.", 409);
            if (await FlowAllocations.AnyAsync(x => x.JournalId == request.JournalId &&
                (x.StateCode == "Submitted" || x.StateCode == "Approved"), ct))
                return ApiResponse<CashFlowJournalAllocationDto>.ErrorResponse("A pending or approved journal allocation already exists.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var allocation = new CashFlowJournalAllocation
            {
                TenantId = user.TenantId, JournalId = request.JournalId,
                JournalFingerprint = proof.Fingerprint, Direction = proof.Direction,
                OperatingAmount = request.OperatingAmount, InvestingAmount = request.InvestingAmount,
                FinancingAmount = request.FinancingAmount, ClientRequestId = request.ClientRequestId,
                Reason = reason, SubmittedByUserId = user.UserId, SubmittedAtUtc = now,
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.CashFlowJournalAllocations.Add(allocation);
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<CashFlowJournalAllocationDto>.SuccessResponse(AllocationMap(allocation, proof.Journal));
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<CashFlowJournalAllocationDto>.ErrorResponse("Allocation changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<CashFlowJournalAllocationDto>.ErrorResponse("Concurrent allocation request; reload.", 409); }
    }

    public async Task<ApiResponse<CashFlowJournalAllocationDto>> ReviewCashFlowJournalAllocationAsync(
        long id, ReviewCashFlowRuleDto request, CancellationToken ct = default)
    {
        if (!await CashFlowActor(true, ct)) return Denied<CashFlowJournalAllocationDto>();
        if (id <= 0 || request == null || request.ReviewRequestId == Guid.Empty ||
            request.Reason?.Length > 500 || !request.Approve && string.IsNullOrWhiteSpace(request.Reason))
            return ApiResponse<CashFlowJournalAllocationDto>.ErrorResponse("Review reference and rejection reason required.", 400);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        try
        {
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var allocation = await FlowAllocations.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (allocation == null) return ApiResponse<CashFlowJournalAllocationDto>.ErrorResponse("Allocation not found.", 404);
            var journal = await db.Journals.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.Id == allocation.JournalId && !x.IsDeleted, ct);
            if (journal == null) return ApiResponse<CashFlowJournalAllocationDto>.ErrorResponse("Source journal missing.", 409);
            if (allocation.ReviewRequestId == request.ReviewRequestId &&
                allocation.StateCode == (request.Approve ? "Approved" : "Rejected") &&
                allocation.ReviewedByUserId == user.UserId && allocation.ReviewReason == reason)
                return ApiResponse<CashFlowJournalAllocationDto>.SuccessResponse(AllocationMap(allocation, journal));
            if (allocation.StateCode != "Submitted")
                return ApiResponse<CashFlowJournalAllocationDto>.ErrorResponse("Allocation already reviewed; history is immutable.", 409);
            if (allocation.SubmittedByUserId == user.UserId)
                return ApiResponse<CashFlowJournalAllocationDto>.ErrorResponse("Maker cannot approve their own allocation.", 403);
            if (!Version(allocation.RowVersion, request.RowVersion))
                return ApiResponse<CashFlowJournalAllocationDto>.ErrorResponse("Allocation changed; reload.", 409);
            if (request.Approve)
            {
                var proof = await JournalCashProof(allocation.JournalId, ct);
                if (proof.Journal == null || proof.Fingerprint != allocation.JournalFingerprint ||
                    proof.Direction != allocation.Direction ||
                    proof.Amount != allocation.OperatingAmount + allocation.InvestingAmount + allocation.FinancingAmount)
                    return ApiResponse<CashFlowJournalAllocationDto>.ErrorResponse("Posted source journal changed; reject and resubmit allocation.", 409);
            }
            var now = clock.GetUtcNow().UtcDateTime;
            allocation.StateCode = request.Approve ? "Approved" : "Rejected";
            allocation.ReviewRequestId = request.ReviewRequestId;
            allocation.ReviewedByUserId = user.UserId; allocation.ReviewedAtUtc = now;
            allocation.ReviewReason = reason; allocation.UpdatedAt = now; allocation.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<CashFlowJournalAllocationDto>.SuccessResponse(AllocationMap(allocation, journal));
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<CashFlowJournalAllocationDto>.ErrorResponse("Allocation review changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<CashFlowJournalAllocationDto>.ErrorResponse("Concurrent allocation review; reload.", 409); }
    }
}
