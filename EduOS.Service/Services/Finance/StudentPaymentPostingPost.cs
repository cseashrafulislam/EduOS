using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class StudentPaymentPostingService
{
    public async Task<ApiResponse<PostStudentPaymentJournalResultDto>> PostAsync(
        Guid paymentReference, PostStudentPaymentJournalRequestDto request, CancellationToken ct = default)
    {
        if (!CanPost) return Error<PostStudentPaymentJournalResultDto>("Institution administrator permission required.", 403);
        if (paymentReference == Guid.Empty || request == null || request.AccountingPeriodId <= 0 ||
            request.CashAccountId <= 0 || string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.RowVersion.Length > 256)
            return Error<PostStudentPaymentJournalResultDto>("Payment, open period, cash ledger and row version required.");
        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var payment = await db.StudentPayments.SingleOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == paymentReference && !x.IsDeleted, ct);
            if (payment == null) return Error<PostStudentPaymentJournalResultDto>("Payment not found.", 404);
            if (payment.JournalId.HasValue)
            {
                var prior = await db.Journals.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == payment.JournalId.Value && !x.IsDeleted, ct);
                if (prior == null || prior.SourceType != "StudentPayment" || prior.SourceId != payment.Id ||
                    prior.State != JournalState.Posted || prior.AccountingPeriodId != request.AccountingPeriodId)
                    return Error<PostStudentPaymentJournalResultDto>("Linked receipt journal requires reconciliation.", 409);
                var debit = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == tenant &&
                    x.JournalId == prior.Id && !x.IsDeleted && x.DebitAmount > 0m)
                    .Select(x => new { x.AccountId, x.DebitAmount }).ToListAsync(ct);
                if (debit.Count != 1 || debit[0].AccountId != request.CashAccountId ||
                    debit[0].DebitAmount != payment.Amount)
                    return Error<PostStudentPaymentJournalResultDto>("Receipt was posted to a different cash ledger.", 409);
                return ApiResponse<PostStudentPaymentJournalResultDto>.SuccessResponse(
                    new PostStudentPaymentJournalResultDto { PaymentReference = payment.PublicId,
                        JournalReference = prior.PublicId, JournalNumber = prior.JournalNumber,
                        Amount = payment.Amount, AlreadyPosted = true }, "Already posted.");
            }
            if (!Version(payment.RowVersion, request.RowVersion))
                return Error<PostStudentPaymentJournalResultDto>("Payment changed; reload.", 409);
            if (payment.State != PaymentState.Successful || payment.PaymentMethod != PaymentMethodType.Cash ||
                payment.BankAccountId.HasValue || payment.CurrencyCode != "BDT" ||
                !Money(payment.Amount) || payment.Amount <= 0m)
                return Error<PostStudentPaymentJournalResultDto>("Only successful BDT cash receipts can be posted.", 409);
            var period = await db.AccountingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AccountingPeriodId && !x.IsDeleted &&
                !x.IsClosed && x.StartDate <= payment.PaymentDate && x.EndDate >= payment.PaymentDate, ct);
            if (period == null) return Error<PostStudentPaymentJournalResultDto>("Open period must cover payment date.", 409);
            var cash = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == request.CashAccountId && !x.IsDeleted && x.IsActive && !x.IsGroup &&
                x.AllowsPosting && x.AccountType == AccountType.Asset && x.CurrencyCode == "BDT", ct);
            if (cash == null || !CashLedger(cash) || await db.BankAccounts.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.AccountId == cash.Id, ct))
                return Error<PostStudentPaymentJournalResultDto>("Select an active BDT cash-in-hand asset ledger, not a bank ledger.", 409);
            if (await db.Journals.AsNoTracking().AnyAsync(x => x.TenantId == tenant && !x.IsDeleted &&
                ((x.SourceType == "StudentPayment" && x.SourceId == payment.Id) ||
                x.IdempotencyKey == PostingKey(tenant, payment.PublicId)), ct))
                return Error<PostStudentPaymentJournalResultDto>("Unlinked receipt journal exists; reconcile first.", 409);

            var allocations = await db.PaymentAllocations.AsNoTracking().Where(x =>
                x.TenantId == tenant && x.StudentPaymentId == payment.Id && !x.IsDeleted)
                .OrderBy(x => x.Id).ToListAsync(ct);
            if (allocations.Count is < 1 or > 99 || allocations.Any(x => x.StudentInvoiceId <= 0 ||
                !Money(x.Amount) || x.Amount <= 0m) ||
                allocations.Select(x => x.StudentInvoiceId).Distinct().Count() != allocations.Count ||
                allocations.Sum(x => x.Amount) != payment.Amount)
                return Error<PostStudentPaymentJournalResultDto>("Payment allocations do not reconcile.", 409);
            var invoiceIds = allocations.Select(x => x.StudentInvoiceId).ToArray();
            var invoiceRows = await (from invoiceRow in db.StudentInvoices.AsNoTracking()
                join enrollment in db.StudentEnrollments.AsNoTracking()
                    on new { invoiceRow.TenantId, Id = invoiceRow.StudentEnrollmentId }
                    equals new { enrollment.TenantId, enrollment.Id }
                join campus in db.Campuses.AsNoTracking()
                    on new { enrollment.TenantId, Id = enrollment.CampusId }
                    equals new { campus.TenantId, campus.Id }
                where invoiceRow.TenantId == tenant && invoiceIds.Contains(invoiceRow.Id) &&
                    !invoiceRow.IsDeleted && !enrollment.IsDeleted && !campus.IsDeleted && campus.IsActive
                select new { Invoice = invoiceRow, enrollment.StudentId, enrollment.CampusId,
                    enrollment.AcademicProgramId }).ToListAsync(ct);
            if (invoiceRows.Count != allocations.Count || invoiceRows.Any(x =>
                x.StudentId != payment.StudentId || x.Invoice.CurrencyCode != "BDT" ||
                x.Invoice.State is not (InvoiceState.Issued or InvoiceState.PartiallyPaid or InvoiceState.Paid) ||
                !Money(x.Invoice.TotalAmount) || !Money(x.Invoice.PaidAmount) ||
                !Money(x.Invoice.DueAmount) || x.Invoice.PaidAmount + x.Invoice.DueAmount != x.Invoice.TotalAmount ||
                !x.Invoice.JournalId.HasValue))
                return Error<PostStudentPaymentJournalResultDto>("Each allocated invoice must be valid and posted to institutional GL.", 409);
            var campusId = invoiceRows[0].CampusId;
            if (invoiceRows.Any(x => x.CampusId != campusId))
                return Error<PostStudentPaymentJournalResultDto>("Split cross-campus cash receipts before posting.", 409);
            var journalIds = invoiceRows.Select(x => x.Invoice.JournalId!.Value).Distinct().ToArray();
            var sourceJournals = await db.Journals.AsNoTracking().Where(x => x.TenantId == tenant &&
                journalIds.Contains(x.Id) && !x.IsDeleted).ToListAsync(ct);
            if (sourceJournals.Count != journalIds.Length || await db.Journals.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.ReversalOfJournalId.HasValue &&
                journalIds.Contains(x.ReversalOfJournalId.Value) && x.State == JournalState.Posted, ct))
                return Error<PostStudentPaymentJournalResultDto>("An invoice journal is missing or reversed.", 409);
            var byJournal = sourceJournals.ToDictionary(x => x.Id);
            var invoiceJournalLines = await db.JournalLines.AsNoTracking().Where(x =>
                x.TenantId == tenant && !x.IsDeleted && journalIds.Contains(x.JournalId))
                .ToListAsync(ct);
            var credits = new List<(long AccountId, long ProgramId, decimal Amount)>();
            foreach (var allocation in allocations)
            {
                var row = invoiceRows.Single(x => x.Invoice.Id == allocation.StudentInvoiceId);
                var invoice = row.Invoice;
                var journal = byJournal[invoice.JournalId!.Value];
                if (journal.SourceType != "StudentInvoice" || journal.SourceId != invoice.Id ||
                    journal.State != JournalState.Posted || journal.CampusId != campusId ||
                    journal.CurrencyCode != "BDT" || journal.ReversalOfJournalId.HasValue ||
                    !Money(invoice.TotalAmount) || invoice.TotalAmount <= 0m ||
                    invoice.PaidAmount < allocation.Amount)
                    return Error<PostStudentPaymentJournalResultDto>("Invoice GL provenance or balances invalid.", 409);
                var lines = invoiceJournalLines.Where(x => x.JournalId == journal.Id).ToArray();
                var receivables = lines.Where(x => x.DebitAmount > 0m).ToArray();
                if (receivables.Length == 0 || receivables.Select(x => x.AccountId).Distinct().Count() != 1 ||
                    receivables.Any(x => x.StudentId != payment.StudentId || x.CampusId != campusId) ||
                    lines.Sum(x => x.DebitAmount) != invoice.TotalAmount ||
                    lines.Sum(x => x.CreditAmount) != invoice.TotalAmount)
                    return Error<PostStudentPaymentJournalResultDto>(
                        "Invoice has multiple receivable ledgers or unbalanced GL; allocation detail is required.", 409);
                credits.Add((receivables[0].AccountId, row.AcademicProgramId, allocation.Amount));
            }
            var receivableIds = credits.Select(x => x.AccountId).Distinct().ToArray();
            var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
                !x.IsDeleted && x.IsActive && !x.IsGroup && x.AllowsPosting &&
                x.CurrencyCode == "BDT" && x.AccountType == AccountType.Asset &&
                receivableIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct);
            if (accounts.Count != receivableIds.Length || receivableIds.Contains(cash.Id))
                return Error<PostStudentPaymentJournalResultDto>("Receivable ledgers must be distinct active BDT asset accounts.", 409);
            var grouped = credits.GroupBy(x => new { x.AccountId, x.ProgramId })
                .Select(g => new { g.Key.AccountId, g.Key.ProgramId, Amount = g.Sum(x => x.Amount) })
                .OrderBy(x => x.AccountId).ThenBy(x => x.ProgramId).ToArray();
            if (grouped.Length is < 1 or > 99 || grouped.Sum(x => x.Amount) != payment.Amount)
                return Error<PostStudentPaymentJournalResultDto>("Receipt journal is not balanced.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var journalId = Guid.NewGuid();
            var postedJournal = new Journal
            {
                TenantId = tenant, PublicId = journalId,
                IdempotencyKey = PostingKey(tenant, payment.PublicId),
                JournalNumber = "JRN-" + journalId.ToString("N").ToUpperInvariant(),
                JournalDate = payment.PaymentDate, AccountingPeriodId = period.Id,
                CampusId = campusId, ReferenceNumber = payment.ReceiptNumber,
                CurrencyCode = "BDT", State = JournalState.Posted,
                SourceType = "StudentPayment", SourceId = payment.Id,
                Description = "Student cash receipt " + payment.ReceiptNumber,
                ApprovedByUserId = user.UserId, ApprovedAt = now,
                PostedByUserId = user.UserId, PostedAt = now,
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.Journals.Add(postedJournal);
            await db.SaveChangesAsync(ct);
            var entries = new List<JournalLine>
            {
                new() { TenantId = tenant, JournalId = postedJournal.Id, LineNo = 1,
                    AccountId = cash.Id, CampusId = campusId, StudentId = payment.StudentId,
                    DebitAmount = payment.Amount, Description = "Cash received",
                    CreatedAt = now, CreatedBy = user.UserId }
            };
            var lineNo = 1;
            foreach (var item in grouped)
                entries.Add(new JournalLine { TenantId = tenant, JournalId = postedJournal.Id,
                    LineNo = ++lineNo, AccountId = item.AccountId, CampusId = campusId,
                    StudentId = payment.StudentId, AcademicProgramId = item.ProgramId,
                    CreditAmount = item.Amount, Description = "Student receivable settlement",
                    CreatedAt = now, CreatedBy = user.UserId });
            db.JournalLines.AddRange(entries);
            payment.JournalId = postedJournal.Id;
            payment.UpdatedAt = now; payment.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<PostStudentPaymentJournalResultDto>.SuccessResponse(
                new PostStudentPaymentJournalResultDto { PaymentReference = payment.PublicId,
                    JournalReference = postedJournal.PublicId, JournalNumber = postedJournal.JournalNumber,
                    Amount = payment.Amount }, "Institutional cash receipt journal posted.");
        }
        catch (OverflowException) { return Error<PostStudentPaymentJournalResultDto>("Payment exceeds ledger precision.", 409); }
        catch (DbUpdateConcurrencyException) { return Error<PostStudentPaymentJournalResultDto>("Payment changed; reload.", 409); }
        catch (DbUpdateException) { return Error<PostStudentPaymentJournalResultDto>("Posting conflict; refresh and retry.", 409); }
    }
}
