using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingPostingService
{
    public async Task<ApiResponse<JournalDto>> CreateJournalAsync(CreateJournalRequestDto request, CancellationToken ct = default)
    {
        if (!CanWrite) return Denied<JournalDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.JournalDate == default ||
            request.AccountingPeriodId <= 0 || (request.Description?.Length ?? 0) > 1000 ||
            request.Lines == null || request.Lines.Count < 2 || request.Lines.Count > 100)
            return ApiResponse<JournalDto>.ErrorResponse("Valid date, period, request key and 2-100 lines required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var previous = await Journals.FirstOrDefaultAsync(x => x.IdempotencyKey == request.ClientRequestId, ct);
            if (previous != null)
            {
                var oldLines = await Lines.AsNoTracking().Where(x => x.JournalId == previous.Id)
                    .OrderBy(x => x.LineNo).ToListAsync(ct);
                var same = Manual(previous) && previous.JournalDate == request.JournalDate &&
                    previous.AccountingPeriodId == request.AccountingPeriodId &&
                    previous.Description == Clean(request.Description) &&
                    oldLines.Count == request.Lines.Count &&
                    oldLines.Zip(request.Lines.OrderBy(x => x.LineNo),
                        (a, b) => a.LineNo == b.LineNo && a.AccountId == b.AccountId &&
                            a.DebitAmount == b.DebitAmount && a.CreditAmount == b.CreditAmount &&
                            a.Description == Clean(b.Description)).All(x => x);
                if (!same) return ApiResponse<JournalDto>.ErrorResponse("Idempotency key belongs to another journal request.", 409);
                return ApiResponse<JournalDto>.SuccessResponse(await DetailAsync(previous, ct), "Existing journal returned.");
            }
            if (await OpenPeriodAsync(request.AccountingPeriodId, request.JournalDate, ct) == null)
                return ApiResponse<JournalDto>.ErrorResponse("Accounting period missing, closed or outside journal date.", 409);
            var lines = request.Lines.Select(x => new JournalLine
            {
                TenantId = user.TenantId, LineNo = x.LineNo, AccountId = x.AccountId,
                DebitAmount = x.DebitAmount, CreditAmount = x.CreditAmount, Description = Clean(x.Description)
            }).ToList();
            if (lines.Any(x => (x.Description?.Length ?? 0) > 500))
                return ApiResponse<JournalDto>.ErrorResponse("Line description exceeds 500 characters.");
            var validation = await ValidateLinesAsync(lines, ct);
            if (validation != null) return ApiResponse<JournalDto>.ErrorResponse(validation);
            var now = clock.GetUtcNow().UtcDateTime;
            var j = new Journal
            {
                TenantId = user.TenantId, PublicId = Guid.NewGuid(), IdempotencyKey = request.ClientRequestId,
                JournalNumber = "JRN-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
                JournalDate = request.JournalDate, AccountingPeriodId = request.AccountingPeriodId,
                CurrencyCode = "BDT", SourceType = "Manual", State = JournalState.Draft,
                Description = Clean(request.Description), CreatedAt = now, CreatedBy = user.UserId
            };
            db.Journals.Add(j);
            await db.SaveChangesAsync(ct);
            foreach (var line in lines) { line.JournalId = j.Id; line.CreatedAt = now; line.CreatedBy = user.UserId; }
            db.JournalLines.AddRange(lines);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<JournalDto>.SuccessResponse(await DetailAsync(j, ct), "Draft journal created.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<JournalDto>.ErrorResponse("Journal changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<JournalDto>.ErrorResponse("Journal number or request conflict; retry same request key.", 409); }
    }
}
