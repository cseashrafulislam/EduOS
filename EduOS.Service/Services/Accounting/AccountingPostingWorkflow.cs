using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingPostingService
{
    public async Task<ApiResponse<JournalDto>> SubmitJournalAsync(
        Guid journalReference, SubmitJournalRequestDto request, CancellationToken ct = default)
    {
        if (!CanWrite) return Denied<JournalDto>();
        if (request == null) return ApiResponse<JournalDto>.ErrorResponse("Row version required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var j = await Journals.FirstOrDefaultAsync(x => x.PublicId == journalReference, ct);
            if (j == null) return ApiResponse<JournalDto>.ErrorResponse("Journal not found.", 404);
            if (!Manual(j) || j.State != JournalState.Draft)
                return ApiResponse<JournalDto>.ErrorResponse("Only a manual draft journal may be submitted.", 409);
            if (!Version(j.RowVersion, request.RowVersion))
                return ApiResponse<JournalDto>.ErrorResponse("Journal changed; reload.", 409);
            if (await OpenPeriodAsync(j.AccountingPeriodId, j.JournalDate, ct) == null)
                return ApiResponse<JournalDto>.ErrorResponse("Accounting period is closed or invalid.", 409);
            var lines = await Lines.Where(x => x.JournalId == j.Id).ToListAsync(ct);
            var error = await ValidateLinesAsync(lines, ct);
            if (error != null) return ApiResponse<JournalDto>.ErrorResponse(error, 409);
            j.State = JournalState.Submitted; j.UpdatedAt = clock.GetUtcNow().UtcDateTime; j.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<JournalDto>.SuccessResponse(await DetailAsync(j, ct), "Journal submitted.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<JournalDto>.ErrorResponse("Journal changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<JournalDto>.ErrorResponse("Journal submission conflict.", 409); }
    }

    public async Task<ApiResponse<JournalDto>> ReviewJournalAsync(
        Guid journalReference, ReviewJournalRequestDto request, CancellationToken ct = default)
    {
        if (!CanWrite) return Denied<JournalDto>();
        if (request == null || (request.Note?.Length ?? 0) > 1000)
            return ApiResponse<JournalDto>.ErrorResponse("Invalid review request.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var j = await Journals.FirstOrDefaultAsync(x => x.PublicId == journalReference, ct);
            if (j == null) return ApiResponse<JournalDto>.ErrorResponse("Journal not found.", 404);
            var isReversal = ManualReversal(j);
            if ((!Manual(j) && !isReversal) || j.State != JournalState.Submitted)
                return ApiResponse<JournalDto>.ErrorResponse("Only submitted manual journals or their reversals may be reviewed.", 409);
            if (!Version(j.RowVersion, request.RowVersion))
                return ApiResponse<JournalDto>.ErrorResponse("Journal changed; reload.", 409);
            if (j.CreatedBy == user.UserId)
                return ApiResponse<JournalDto>.ErrorResponse("Maker and approver must be different users.", 403);
            if (request.Approve && await OpenPeriodAsync(j.AccountingPeriodId, j.JournalDate, ct) == null)
                return ApiResponse<JournalDto>.ErrorResponse("Accounting period is closed or invalid.", 409);
            if (request.Approve && isReversal)
            {
                var reversalError = await ValidateManualReversalAsync(j, ct);
                if (reversalError != null)
                    return ApiResponse<JournalDto>.ErrorResponse(reversalError, 409);
            }
            j.State = request.Approve ? JournalState.Approved : JournalState.Rejected;
            j.ApprovedByUserId = request.Approve ? user.UserId : null;
            j.ApprovedAt = request.Approve ? clock.GetUtcNow().UtcDateTime : null;
            j.UpdatedAt = clock.GetUtcNow().UtcDateTime; j.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<JournalDto>.SuccessResponse(await DetailAsync(j, ct), request.Approve ? "Journal approved." : "Journal rejected.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<JournalDto>.ErrorResponse("Journal changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<JournalDto>.ErrorResponse("Journal review conflict.", 409); }
    }

    public async Task<ApiResponse<JournalDto>> PostJournalAsync(
        Guid journalReference, PostJournalRequestDto request, CancellationToken ct = default)
    {
        if (!CanWrite) return Denied<JournalDto>();
        if (request == null) return ApiResponse<JournalDto>.ErrorResponse("Row version required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var j = await Journals.FirstOrDefaultAsync(x => x.PublicId == journalReference, ct);
            if (j == null) return ApiResponse<JournalDto>.ErrorResponse("Journal not found.", 404);
            var isReversal = ManualReversal(j);
            if ((!Manual(j) && !isReversal) || j.State != JournalState.Approved || j.ApprovedByUserId == null)
                return ApiResponse<JournalDto>.ErrorResponse("Journal must be independently approved before posting.", 409);
            if (!Version(j.RowVersion, request.RowVersion))
                return ApiResponse<JournalDto>.ErrorResponse("Journal changed; reload.", 409);
            if (await OpenPeriodAsync(j.AccountingPeriodId, j.JournalDate, ct) == null)
                return ApiResponse<JournalDto>.ErrorResponse("Accounting period is closed or invalid.", 409);
            var lines = await Lines.Where(x => x.JournalId == j.Id).ToListAsync(ct);
            var error = await ValidateLinesAsync(lines, ct);
            if (error != null) return ApiResponse<JournalDto>.ErrorResponse(error, 409);
            if (isReversal)
            {
                var reversalError = await ValidateManualReversalAsync(j, ct);
                if (reversalError != null)
                    return ApiResponse<JournalDto>.ErrorResponse(reversalError, 409);
            }
            j.State = JournalState.Posted; j.PostedAt = clock.GetUtcNow().UtcDateTime;
            j.PostedByUserId = user.UserId; j.UpdatedAt = j.PostedAt; j.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<JournalDto>.SuccessResponse(await DetailAsync(j, ct), "Journal posted.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<JournalDto>.ErrorResponse("Journal changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<JournalDto>.ErrorResponse("Journal posting conflict.", 409); }
    }
}
