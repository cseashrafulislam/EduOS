using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingSetupService
{
    private IQueryable<CashLedgerDesignationRequest> CashProposals => db.CashLedgerDesignationRequests
        .Where(x => x.TenantId == user.TenantId && !x.IsDeleted);

    private static CashLedgerDesignationDto CashMap(CashLedgerDesignationRequest x, Account a) => new()
    {
        Id = x.Id, AccountId = x.AccountId, AccountCode = a.Code, AccountName = a.Name,
        StateCode = x.StateCode, Reason = x.Reason, SubmittedByUserId = x.SubmittedByUserId,
        SubmittedAtUtc = x.SubmittedAtUtc, ReviewedByUserId = x.ReviewedByUserId,
        ReviewedAtUtc = x.ReviewedAtUtc, ReviewReason = x.ReviewReason,
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };

    public async Task<ApiResponse<PagedResult<CashLedgerDesignationDto>>> GetCashDesignationRequestsAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        if (!Writable) return Denied<PagedResult<CashLedgerDesignationDto>>();
        if (page is < 1 or > 100000 || pageSize is < 1 or > 50)
            return ApiResponse<PagedResult<CashLedgerDesignationDto>>.ErrorResponse("Valid page and size required.", 400);
        var q = from proposal in CashProposals.AsNoTracking()
            join account in Accounts.AsNoTracking() on proposal.AccountId equals account.Id
            select new { Proposal = proposal, Account = account };
        var total = await q.CountAsync(ct);
        var records = await q.OrderByDescending(x => x.Proposal.SubmittedAtUtc)
            .ThenByDescending(x => x.Proposal.Id).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<CashLedgerDesignationDto>>.SuccessResponse(new()
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = records.Select(x => CashMap(x.Proposal, x.Account)).ToList()
        });
    }

    public async Task<ApiResponse<CashLedgerDesignationDto>> SubmitHistoricCashDesignationAsync(
        SubmitCashLedgerDesignationDto request, CancellationToken ct = default)
    {
        if (!Writable) return Denied<CashLedgerDesignationDto>();
        if (request == null || request.AccountId <= 0 || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 20 or > 500)
            return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("A valid ledger, unique reference and 20–500 character justification are required.", 400);
        var reason = request.Reason.Trim();
        try
        {
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var existing = await CashProposals.FirstOrDefaultAsync(x => x.ClientRequestId == request.ClientRequestId, ct);
            if (existing != null)
            {
                if (existing.SubmittedByUserId != user.UserId || existing.AccountId != request.AccountId || existing.Reason != reason)
                    return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("Idempotency key belongs to a different proposal.", 409);
                var previousAccount = await Accounts.AsNoTracking().SingleAsync(x => x.Id == existing.AccountId, ct);
                return ApiResponse<CashLedgerDesignationDto>.SuccessResponse(CashMap(existing, previousAccount));
            }
            var account = await Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.AccountId, ct);
            if (account == null || account.IsCashInHand || account.IsGroup || !account.IsActive ||
                !account.AllowsPosting || account.AccountType != AccountType.Asset || account.CurrencyCode != "BDT")
                return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("Select an existing active, unclassified BDT asset posting ledger.", 409);
            if (await db.BankAccounts.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.AccountId == account.Id && !x.IsDeleted, ct))
                return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("Bank-mapped ledger cannot be designated as cash.", 409);
            if (!await db.JournalLines.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.AccountId == account.Id, ct))
                return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("No journal history: use normal account setup for new cash ledgers.", 409);
            if (await CashProposals.AnyAsync(x => x.AccountId == account.Id && x.StateCode == "Submitted", ct))
                return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("A cash designation request for this ledger is awaiting review.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var proposal = new CashLedgerDesignationRequest
            {
                TenantId = user.TenantId, AccountId = account.Id,
                ClientRequestId = request.ClientRequestId, Reason = reason,
                StateCode = "Submitted", SubmittedByUserId = user.UserId, SubmittedAtUtc = now,
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.CashLedgerDesignationRequests.Add(proposal);
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<CashLedgerDesignationDto>.SuccessResponse(CashMap(proposal, account),
                "Cash designation submitted for independent historical review.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("Cash designation changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("Cash designation request conflict; reload.", 409); }
    }

    public async Task<ApiResponse<CashLedgerDesignationDto>> ReviewHistoricCashDesignationAsync(
        long requestId, ReviewCashLedgerDesignationDto request, CancellationToken ct = default)
    {
        if (!TenantMatched || !user.IsTenantAdmin) return Denied<CashLedgerDesignationDto>();
        if (requestId <= 0 || request == null || request.ReviewRequestId == Guid.Empty ||
            request.Reason?.Length > 500 || (!request.Approve && string.IsNullOrWhiteSpace(request.Reason)))
            return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("Valid review reference and rejection reason required.", 400);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        try
        {
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var proposal = await CashProposals.SingleOrDefaultAsync(x => x.Id == requestId, ct);
            if (proposal == null) return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("Cash designation request not found.", 404);
            var account = await Accounts.SingleOrDefaultAsync(x => x.Id == proposal.AccountId, ct);
            if (account == null) return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("Cash ledger missing.", 409);
            if (proposal.ReviewRequestId == request.ReviewRequestId &&
                proposal.StateCode == (request.Approve ? "Approved" : "Rejected") &&
                proposal.ReviewedByUserId == user.UserId && proposal.ReviewReason == reason)
                return ApiResponse<CashLedgerDesignationDto>.SuccessResponse(CashMap(proposal, account));
            if (proposal.StateCode != "Submitted")
                return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("Request already reviewed; corrections require a new audited request.", 409);
            if (proposal.SubmittedByUserId == user.UserId)
                return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("Submitter cannot approve their own historical classification.", 403);
            if (!Version(proposal.RowVersion, request.RowVersion))
                return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("Request changed; reload before review.", 409);
            if (request.Approve)
            {
                if (account.IsCashInHand || account.IsGroup || !account.IsActive || !account.AllowsPosting ||
                    account.AccountType != AccountType.Asset || account.CurrencyCode != "BDT" ||
                    await db.BankAccounts.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                        x.AccountId == account.Id && !x.IsDeleted, ct) ||
                    !await db.JournalLines.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                        x.TenantId == user.TenantId && x.AccountId == account.Id, ct))
                    return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("Historical ledger scope changed or conflicts with bank mapping; reject and resubmit.", 409);
            }
            var now = clock.GetUtcNow().UtcDateTime;
            proposal.StateCode = request.Approve ? "Approved" : "Rejected";
            proposal.ReviewRequestId = request.ReviewRequestId;
            proposal.ReviewedByUserId = user.UserId;
            proposal.ReviewedAtUtc = now; proposal.ReviewReason = reason;
            proposal.UpdatedAt = now; proposal.UpdatedBy = user.UserId;
            if (request.Approve)
            {
                account.IsCashInHand = true;
                account.UpdatedAt = now; account.UpdatedBy = user.UserId;
            }
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<CashLedgerDesignationDto>.SuccessResponse(CashMap(proposal, account),
                request.Approve ? "Historical cash ledger approved." : "Cash designation rejected.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("Cash designation changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<CashLedgerDesignationDto>.ErrorResponse("Cash designation review conflict; reload.", 409); }
    }
}
