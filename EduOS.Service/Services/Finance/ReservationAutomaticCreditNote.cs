using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.Finance;

/// <summary>
/// A reservation credit note is an additional Finance journal whose SourceId is the
/// original immutable StudentInvoice. The clearance owns its single-use approval;
/// no original invoice, receipt, allocation or cash balance is edited.
/// </summary>
public static class ReservationAutomaticCreditNote
{
    public const string SourceType = "StudentReservationCreditNote";

    /// <summary>Oldest unpaid receivable first; only excess previously paid value becomes a liability.</summary>
    public static (decimal ReceivableReduction, decimal StudentCreditPayable) Split(
        decimal amount, decimal remainingDue, decimal remainingCapacity)
    {
        if (!ReservationPeriodSettlement.ValidAmount(amount) || amount <= 0m ||
            !ReservationPeriodSettlement.ValidAmount(remainingDue) || remainingDue < 0m ||
            !ReservationPeriodSettlement.ValidAmount(remainingCapacity) ||
            remainingCapacity < 0m || amount > remainingCapacity ||
            remainingDue > remainingCapacity)
            throw new InvalidOperationException("Reservation credit cannot exceed reconciled invoice capacity.");
        var receivable = decimal.Min(amount, remainingDue);
        return (receivable, amount - receivable);
    }

    public static Guid CreditKey(long tenantId, string kind, long clearanceId)
    {
        if (tenantId <= 0 || clearanceId <= 0 || kind is not ("HOSTEL" or "TRANSPORT"))
            throw new ArgumentOutOfRangeException(nameof(clearanceId));
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"EduOS:ReservationCredit:{tenantId}:{kind}:{clearanceId}")).AsSpan(0, 16));
    }

    private sealed record Source(StudentInvoice Invoice, StudentEnrollment Enrollment,
        Journal Journal, long ReceivableId, long IncomeId, long FeeHeadId,
        decimal RemainingDue, decimal RemainingCapacity);

    /// <summary>Read the original invoice and independently posted GL, refusing ambiguous history.</summary>
    private static async Task<Source?> LoadSourceAsync(EduOSDbContext db, long tenant,
        long campus, long student, long enrollmentId, long invoiceId,
        long? excludeJournalId, CancellationToken ct)
    {
        if (db.CurrentTenantId != tenant || tenant <= 0 || invoiceId <= 0) return null;
        var invoice = await db.StudentInvoices.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == invoiceId && !x.IsDeleted &&
            x.StudentEnrollmentId == enrollmentId && x.JournalId.HasValue &&
            x.CurrencyCode == "BDT" &&
            (x.State == InvoiceState.Issued || x.State == InvoiceState.PartiallyPaid ||
             x.State == InvoiceState.Paid), ct);
        var enrollment = await db.StudentEnrollments.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == enrollmentId && x.StudentId == student &&
            x.CampusId == campus && !x.IsDeleted && x.IsCurrent &&
            x.State == EnrollmentState.Active, ct);
        if (invoice == null || enrollment == null || invoice.TotalAmount <= 0m ||
            !ReservationPeriodSettlement.ValidAmount(invoice.TotalAmount) ||
            !ReservationPeriodSettlement.ValidAmount(invoice.PaidAmount) ||
            !ReservationPeriodSettlement.ValidAmount(invoice.DueAmount) ||
            invoice.PaidAmount < 0m || invoice.DueAmount < 0m ||
            invoice.TotalAmount != invoice.PaidAmount + invoice.DueAmount) return null;

        var journal = await db.Journals.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == invoice.JournalId!.Value && !x.IsDeleted &&
            x.State == JournalState.Posted && x.SourceType == "StudentInvoice" &&
            x.SourceId == invoiceId && x.CampusId == campus && x.CurrencyCode == "BDT", ct);
        if (journal == null || await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.State == JournalState.Posted &&
            x.ReversalOfJournalId == journal.Id, ct)) return null;
        var lines = await db.JournalLines.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.JournalId == journal.Id).Take(101).ToArrayAsync(ct);
        var invoiceLines = await db.StudentInvoiceLines.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.StudentInvoiceId == invoiceId).Take(3).ToArrayAsync(ct);
        if (lines.Length != 2 || invoiceLines.Length != 1 || invoiceLines[0].IsDeleted ||
            invoiceLines[0].StudentFineId.HasValue || invoiceLines[0].NetAmount != invoice.TotalAmount ||
            lines.Any(x => x.IsDeleted || x.CampusId != campus || x.StudentId != student ||
                x.TenantId != tenant || x.DebitAmount < 0m || x.CreditAmount < 0m) ||
            lines.Sum(x => x.DebitAmount) != invoice.TotalAmount ||
            lines.Sum(x => x.CreditAmount) != invoice.TotalAmount ||
            lines.Count(x => x.DebitAmount == invoice.TotalAmount && x.CreditAmount == 0m) != 1 ||
            lines.Count(x => x.CreditAmount == invoice.TotalAmount && x.DebitAmount == 0m &&
                x.FeeHeadId == invoiceLines[0].FeeHeadId) != 1) return null;
        var receivable = lines.Single(x => x.DebitAmount > 0m).AccountId;
        var income = lines.Single(x => x.CreditAmount > 0m).AccountId;
        var accountTypes = await db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
            (x.Id == receivable || x.Id == income) && !x.IsDeleted && x.IsActive &&
            !x.IsGroup && x.AllowsPosting && x.CurrencyCode == "BDT")
            .Select(x => new { x.Id, x.AccountType }).ToArrayAsync(ct);
        if (!accountTypes.Any(x => x.Id == receivable && x.AccountType == AccountType.Asset) ||
            !accountTypes.Any(x => x.Id == income && x.AccountType == AccountType.Income)) return null;

        // Any historic refund of this invoice may already represent a credit or a
        // cash contra. Fail closed rather than issuing a second independent credit.
        var allocations = await db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.StudentInvoiceId == invoiceId)
            .Take(101).ToArrayAsync(ct);
        if (allocations.Length > 100 || allocations.Any(x => x.IsDeleted || x.Amount <= 0m ||
            !ReservationPeriodSettlement.ValidAmount(x.Amount)) ||
            allocations.Sum(x => x.Amount) != invoice.PaidAmount) return null;
        var allocationIds = allocations.Select(x => x.Id).ToArray();
        if (await db.RefundAllocations.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && allocationIds.Contains(x.PaymentAllocationId), ct)) return null;
        var paymentIds = allocations.Select(x => x.StudentPaymentId).Distinct().ToArray();
        var payments = await db.StudentPayments.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TenantId == tenant && paymentIds.Contains(x.Id)).ToArrayAsync(ct);
        if (payments.Length != paymentIds.Length || payments.Any(x => x.IsDeleted ||
            x.StudentId != student || x.CurrencyCode != "BDT" ||
            x.State != PaymentState.Successful || !x.JournalId.HasValue)) return null;
        var paymentJournalIds = payments.Select(x => x.JournalId!.Value).Distinct().ToArray();
        var receiptJournals = await db.Journals.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TenantId == tenant && paymentJournalIds.Contains(x.Id)).ToArrayAsync(ct);
        if (receiptJournals.Length != paymentJournalIds.Length ||
            payments.Any(x => !receiptJournals.Any(j => j.Id == x.JournalId &&
                j.State == JournalState.Posted && j.SourceType == "StudentPayment" &&
                j.SourceId == x.Id && j.CampusId == campus && !j.IsDeleted)) ||
            await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.State == JournalState.Posted &&
                x.ReversalOfJournalId.HasValue && paymentJournalIds.Contains(x.ReversalOfJournalId.Value), ct))
            return null;

        var notes = await db.Journals.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.SourceType == SourceType && x.SourceId == invoiceId &&
            (!excludeJournalId.HasValue || x.Id != excludeJournalId.Value))
            .Take(101).ToArrayAsync(ct);
        if (notes.Length > 100 || notes.Any(x => x.IsDeleted ||
            x.State is not (JournalState.Approved or JournalState.Posted) ||
            x.CampusId != campus || x.CurrencyCode != "BDT")) return null;
        // No second auto approval while a previous credit is awaiting Operations.
        if (notes.Any(x => x.State == JournalState.Approved)) return null;
        var noteIds = notes.Select(x => x.Id).ToArray();
        var noteLines = await db.JournalLines.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TenantId == tenant && noteIds.Contains(x.JournalId)).Take(301).ToArrayAsync(ct);
        if (noteLines.Length > 300 ||
            await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.State == JournalState.Posted &&
                x.ReversalOfJournalId.HasValue && noteIds.Contains(x.ReversalOfJournalId.Value), ct)) return null;
        decimal priorTotal = 0m, priorDue = 0m;
        foreach (var note in notes)
        {
            var entries = noteLines.Where(x => x.JournalId == note.Id).ToArray();
            var debit = entries.Sum(x => x.DebitAmount);
            var credit = entries.Sum(x => x.CreditAmount);
            if (entries.Length is < 2 or > 3 || debit <= 0m || debit != credit ||
                entries.Any(x => x.IsDeleted || x.StudentId != student || x.CampusId != campus ||
                    x.DebitAmount < 0m || x.CreditAmount < 0m) ||
                entries.Count(x => x.AccountId == income && x.DebitAmount == debit) != 1 ||
                entries.Any(x => x.DebitAmount > 0m && x.AccountId != income)) return null;
            priorTotal += debit;
            priorDue += entries.Where(x => x.AccountId == receivable).Sum(x => x.CreditAmount);
        }
        if (priorTotal > invoice.TotalAmount || priorDue > invoice.DueAmount) return null;
        return new Source(invoice, enrollment, journal, receivable, income,
            invoiceLines[0].FeeHeadId, invoice.DueAmount - priorDue,
            invoice.TotalAmount - priorTotal);
    }

    public static async Task<(long? JournalId, string? Error)> PrepareAsync(EduOSDbContext db,
        long tenant, long reviewerId, long makerId, string kind, long clearanceId,
        long campus, long student, long enrollmentId, decimal signedAmount,
        Guid? sourceInvoiceReference, long? liabilityAccountId, long? accountingPeriodId,
        DateOnly date, DateTime nowUtc, CancellationToken ct)
    {
        if (db.CurrentTenantId != tenant || tenant <= 0 || reviewerId <= 0 ||
            makerId <= 0 || makerId == reviewerId || clearanceId <= 0 ||
            kind is not ("HOSTEL" or "TRANSPORT") || campus <= 0 || student <= 0 ||
            enrollmentId <= 0 || !ReservationPeriodSettlement.ValidAmount(signedAmount) ||
            signedAmount >= 0m || !sourceInvoiceReference.HasValue ||
            sourceInvoiceReference.Value == Guid.Empty ||
            !accountingPeriodId.HasValue || accountingPeriodId <= 0)
            return (null, "A negative credit requires the original invoice, amount and open period.");
        var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == accountingPeriodId && !x.IsDeleted &&
            !x.IsClosed && x.StartDate <= date && x.EndDate >= date, ct);
        var invoiceId = await db.StudentInvoices.AsNoTracking().Where(x =>
            x.TenantId == tenant && x.PublicId == sourceInvoiceReference)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (period == null || invoiceId <= 0)
            return (null, "Original invoice or accounting period unavailable.");
        var source = await LoadSourceAsync(db, tenant, campus, student, enrollmentId,
            invoiceId, null, ct);
        var amount = decimal.Abs(signedAmount);
        if (source == null || amount > source.RemainingCapacity)
            return (null, "Original posted single-fee-head invoice or remaining credit capacity cannot be reconciled.");
        var (unpaid, liability) = Split(amount, source.RemainingDue, source.RemainingCapacity);
        if (liability > 0m && (!liabilityAccountId.HasValue || liabilityAccountId <= 0 ||
            liabilityAccountId == source.ReceivableId || liabilityAccountId == source.IncomeId ||
            !await db.Accounts.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.Id == liabilityAccountId && !x.IsDeleted && x.IsActive && !x.IsGroup &&
                x.AllowsPosting && x.CurrencyCode == "BDT" && x.AccountType == AccountType.Liability, ct)))
            return (null, "A configured active BDT student-credit liability account is required for the paid portion.");
        var key = CreditKey(tenant, kind, clearanceId);
        if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.IdempotencyKey == key, ct))
            return (null, "This clearance already has a credit journal; reconcile before retry.");
        var reference = Guid.NewGuid();
        var journal = new Journal
        {
            TenantId = tenant, PublicId = reference, IdempotencyKey = key,
            JournalNumber = "JRN-" + reference.ToString("N").ToUpperInvariant(),
            JournalDate = date, AccountingPeriodId = period.Id, CampusId = campus,
            ReferenceNumber = $"{kind}-CREDIT-{clearanceId}", CurrencyCode = "BDT",
            State = JournalState.Approved, SourceType = SourceType, SourceId = invoiceId,
            Description = $"{kind} clearance #{clearanceId} original invoice {source.Invoice.InvoiceNumber} credit note",
            ApprovedByUserId = reviewerId, ApprovedAt = nowUtc,
            CreatedAt = nowUtc, CreatedBy = reviewerId
        };
        db.Journals.Add(journal);
        await db.SaveChangesAsync(ct);
        var entries = new List<JournalLine>
        {
            new() { TenantId = tenant, JournalId = journal.Id, LineNo = 1,
                AccountId = source.IncomeId, CampusId = campus, StudentId = student,
                AcademicProgramId = source.Enrollment.AcademicProgramId,
                FeeHeadId = source.FeeHeadId, DebitAmount = amount,
                Description = "Original posted fee income reversal", CreatedAt = nowUtc, CreatedBy = reviewerId }
        };
        if (unpaid > 0m) entries.Add(new JournalLine
        {
            TenantId = tenant, JournalId = journal.Id, LineNo = entries.Count + 1,
            AccountId = source.ReceivableId, CampusId = campus, StudentId = student,
            AcademicProgramId = source.Enrollment.AcademicProgramId, CreditAmount = unpaid,
            Description = "Unpaid invoice receivable reduction", CreatedAt = nowUtc, CreatedBy = reviewerId
        });
        if (liability > 0m) entries.Add(new JournalLine
        {
            TenantId = tenant, JournalId = journal.Id, LineNo = entries.Count + 1,
            AccountId = liabilityAccountId!.Value, CampusId = campus, StudentId = student,
            AcademicProgramId = source.Enrollment.AcademicProgramId, CreditAmount = liability,
            Description = "Student credit payable; not a cash payout", CreatedAt = nowUtc, CreatedBy = reviewerId
        });
        db.JournalLines.AddRange(entries);
        await db.SaveChangesAsync(ct);
        return (journal.Id, null);
    }

    public static async Task<bool> PreparedAsync(EduOSDbContext db, long tenant, string kind,
        long clearanceId, long journalId, long campus, long student, long enrollment,
        decimal signedAmount, CancellationToken ct)
    {
        if (db.CurrentTenantId != tenant || journalId <= 0 || signedAmount >= 0m ||
            !ReservationPeriodSettlement.ValidAmount(signedAmount)) return false;
        var key = CreditKey(tenant, kind, clearanceId);
        var journal = await db.Journals.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == journalId && !x.IsDeleted &&
            x.IdempotencyKey == key && x.State == JournalState.Approved &&
            x.SourceType == SourceType && x.SourceId.HasValue && x.CampusId == campus &&
            x.CurrencyCode == "BDT" && x.ApprovedByUserId.HasValue &&
            !x.PostedByUserId.HasValue, ct);
        if (journal == null || !await db.AccountingPeriods.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == journal.AccountingPeriodId && !x.IsDeleted &&
            !x.IsClosed && x.StartDate <= journal.JournalDate && x.EndDate >= journal.JournalDate, ct) ||
            await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.State == JournalState.Posted &&
                x.ReversalOfJournalId == journalId, ct)) return false;
        var source = await LoadSourceAsync(db, tenant, campus, student, enrollment,
            journal.SourceId!.Value, journalId, ct);
        if (source == null || decimal.Abs(signedAmount) > source.RemainingCapacity) return false;
        var entries = await db.JournalLines.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.JournalId == journalId).Take(4).ToArrayAsync(ct);
        var amount = decimal.Abs(signedAmount);
        var (unpaid, liability) = Split(amount, source.RemainingDue, source.RemainingCapacity);
        if (entries.Length is < 2 or > 3 || entries.Any(x => x.IsDeleted ||
            x.StudentId != student || x.CampusId != campus || x.TenantId != tenant ||
            x.DebitAmount < 0m || x.CreditAmount < 0m) ||
            entries.Sum(x => x.DebitAmount) != amount || entries.Sum(x => x.CreditAmount) != amount ||
            entries.Count(x => x.AccountId == source.IncomeId && x.FeeHeadId == source.FeeHeadId &&
                x.DebitAmount == amount && x.CreditAmount == 0m) != 1 ||
            entries.Where(x => x.AccountId == source.ReceivableId).Sum(x => x.CreditAmount) != unpaid ||
            entries.Any(x => x.DebitAmount > 0m && x.AccountId != source.IncomeId) ||
            entries.Where(x => x.CreditAmount > 0m && x.AccountId != source.ReceivableId)
                .Sum(x => x.CreditAmount) != liability) return false;
        if (liability > 0m)
        {
            var liabilities = entries.Where(x => x.CreditAmount > 0m &&
                x.AccountId != source.ReceivableId).ToArray();
            if (liabilities.Length != 1 || !await db.Accounts.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == liabilities[0].AccountId && !x.IsDeleted &&
                x.IsActive && !x.IsGroup && x.AllowsPosting && x.CurrencyCode == "BDT" &&
                x.AccountType == AccountType.Liability, ct)) return false;
        }
        return true;
    }

    public static async Task<bool> FinalizeAsync(EduOSDbContext db, long tenant, string kind,
        long clearanceId, long journalId, long campus, long student, long enrollment,
        decimal signedAmount, long userId, DateTime nowUtc, CancellationToken ct)
    {
        if (!await PreparedAsync(db, tenant, kind, clearanceId, journalId, campus,
            student, enrollment, signedAmount, ct)) return false;
        var journal = await db.Journals.FirstAsync(x => x.TenantId == tenant && x.Id == journalId, ct);
        journal.State = JournalState.Posted;
        journal.PostedByUserId = userId;
        journal.PostedAt = nowUtc;
        journal.UpdatedBy = userId;
        journal.UpdatedAt = nowUtc;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
