using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingPostingService
{
    public async Task<ApiResponse<JournalDto>> ReverseJournalAsync(
        Guid journalReference, ReverseJournalRequestDto request, CancellationToken ct = default)
    {
        if (!CanWrite) return Denied<JournalDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.ReversalDate == default ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            return ApiResponse<JournalDto>.ErrorResponse("Valid reversal date, reason and request key required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var original = await Journals.FirstOrDefaultAsync(x => x.PublicId == journalReference, ct);
            if (original == null) return ApiResponse<JournalDto>.ErrorResponse("Journal not found.", 404);
            if (!Manual(original) || original.State != JournalState.Posted)
                return ApiResponse<JournalDto>.ErrorResponse("Only posted manual journals can be reversed here.", 409);
            var existingKey = await Journals.FirstOrDefaultAsync(x => x.IdempotencyKey == request.ClientRequestId, ct);
            if (existingKey != null)
            {
                if (existingKey.ReversalOfJournalId != original.Id || existingKey.SourceType != "ManualReversal" ||
                    existingKey.JournalDate != request.ReversalDate || existingKey.Description != Clean(request.Reason))
                    return ApiResponse<JournalDto>.ErrorResponse("Idempotency key belongs to another operation.", 409);
                return ApiResponse<JournalDto>.SuccessResponse(await DetailAsync(existingKey, ct), "Existing reversal returned.");
            }
            if (!Version(original.RowVersion, request.RowVersion))
                return ApiResponse<JournalDto>.ErrorResponse("Journal changed; reload.", 409);
            if (await Journals.AnyAsync(x => x.ReversalOfJournalId == original.Id, ct))
                return ApiResponse<JournalDto>.ErrorResponse("Journal already has a reversal.", 409);
            var period = await Periods.FirstOrDefaultAsync(x => !x.IsClosed &&
                x.StartDate <= request.ReversalDate && x.EndDate >= request.ReversalDate, ct);
            if (period == null) return ApiResponse<JournalDto>.ErrorResponse("No open period covers the reversal date.", 409);
            var lines = await Lines.AsNoTracking().Where(x => x.JournalId == original.Id)
                .OrderBy(x => x.LineNo).ToListAsync(ct);
            var error = await ValidateLinesAsync(lines, ct);
            if (error != null) return ApiResponse<JournalDto>.ErrorResponse(error, 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var reversal = new Journal
            {
                TenantId = user.TenantId, PublicId = Guid.NewGuid(), IdempotencyKey = request.ClientRequestId,
                JournalNumber = "REV-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
                JournalDate = request.ReversalDate, AccountingPeriodId = period.Id, CurrencyCode = "BDT",
                SourceType = "ManualReversal", SourceId = original.Id, ReversalOfJournalId = original.Id,
                Description = Clean(request.Reason), State = JournalState.Posted,
                ApprovedByUserId = user.UserId, ApprovedAt = now,
                PostedByUserId = user.UserId, PostedAt = now, CreatedAt = now, CreatedBy = user.UserId
            };
            db.Journals.Add(reversal);
            await db.SaveChangesAsync(ct);
            var reversedLines = lines.Select(x => new JournalLine
            {
                TenantId = user.TenantId, JournalId = reversal.Id, LineNo = x.LineNo,
                AccountId = x.AccountId, CampusId = null, StudentId = x.StudentId, EmployeeId = x.EmployeeId,
                AcademicProgramId = x.AcademicProgramId, FeeHeadId = x.FeeHeadId,
                DebitAmount = x.CreditAmount, CreditAmount = x.DebitAmount,
                Description = x.Description, CreatedAt = now, CreatedBy = user.UserId
            }).ToList();
            db.JournalLines.AddRange(reversedLines);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<JournalDto>.SuccessResponse(await DetailAsync(reversal, ct), "Compensating reversal posted.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<JournalDto>.ErrorResponse("Journal changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<JournalDto>.ErrorResponse("Reversal conflict; retry same request key.", 409); }
    }
}
